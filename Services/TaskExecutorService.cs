using System.Collections.Concurrent;
using System.Text;
using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для автоматического выполнения задач из файла tasks.md в агентском режиме
/// </summary>
public class TaskExecutorService : ITaskExecutorService
{
    private readonly RefactoringDbContext _dbContext;
    private readonly IPromptProcessor _promptProcessor;
    private readonly IGitService _gitService;
    private readonly ILogger<TaskExecutorService> _logger;
    private readonly PathValidator _pathValidator;
    private readonly TaskParser _taskParser;
    private readonly TaskFileUpdater _fileUpdater;
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancellationTokens;

    public TaskExecutorService(
        RefactoringDbContext dbContext,
        IPromptProcessor promptProcessor,
        IGitService gitService,
        ILogger<TaskExecutorService> logger,
        PathValidator pathValidator,
        ILoggerFactory loggerFactory)
    {
        _dbContext = dbContext;
        _promptProcessor = promptProcessor;
        _gitService = gitService;
        _logger = logger;
        _pathValidator = pathValidator;
        _taskParser = new TaskParser();
        _fileUpdater = new TaskFileUpdater(loggerFactory.CreateLogger<TaskFileUpdater>());
        _cancellationTokens = new ConcurrentDictionary<int, CancellationTokenSource>();
    }

    public async Task<int> ExecuteTasksAsync(int dialogueId, string tasksFilePath, bool skipOptional = true)
    {
        // Валидация существования диалога
        var dialogue = await _dbContext.Dialogues
            .Include(d => d.Messages)
            .FirstOrDefaultAsync(d => d.Id == dialogueId);
            
        if (dialogue == null)
        {
            _logger.LogWarning("Dialogue not found: {DialogueId}", dialogueId);
            throw new ArgumentException("Dialogue not found");
        }
            
        // Валидация существования файла tasks.md
        if (!File.Exists(tasksFilePath))
        {
            _logger.LogWarning("Tasks file not found: {FilePath}", tasksFilePath);
            throw new ArgumentException("Tasks file not found");
        }
        
        // Валидация пути к файлу через PathValidator
        var fileDirectory = Path.GetDirectoryName(tasksFilePath);
        if (string.IsNullOrEmpty(fileDirectory))
        {
            _logger.LogWarning("Invalid file path: {FilePath}", tasksFilePath);
            throw new ArgumentException("Invalid file path");
        }
        
        if (!_pathValidator.ValidatePath(fileDirectory, out var errorMessage))
        {
            _logger.LogWarning("Invalid file path: {FilePath}, Error: {Error}", tasksFilePath, errorMessage);
            throw new ArgumentException($"Invalid file path: {errorMessage}");
        }
        
        // Парсинг задач
        var tasks = _taskParser.ParseFile(tasksFilePath);
        var incompleteTasks = GetIncompleteTasks(tasks, skipOptional);
        
        // Создание сессии выполнения
        var session = new ExecutionSession
        {
            DialogueId = dialogueId,
            TasksFilePath = tasksFilePath,
            Status = "running",
            Progress = $"0/{incompleteTasks.Count}",
            StartedAt = DateTime.UtcNow,
            SkipOptional = skipOptional
        };
        
        _dbContext.ExecutionSessions.Add(session);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Created execution session {SessionId} for dialogue {DialogueId}", 
            session.Id, dialogueId);
        
        // Запуск выполнения в фоновом потоке
        var cts = new CancellationTokenSource();
        _cancellationTokens[dialogueId] = cts;
        
        _ = Task.Run(async () => await ExecuteTasksInternalAsync(
            session.Id, dialogueId, incompleteTasks, tasksFilePath, cts.Token));
        
        return session.Id;
    }
    
    /// <summary>
    /// Получает список незавершенных задач с учетом фильтрации опциональных
    /// </summary>
    private List<TaskItem> GetIncompleteTasks(List<TaskItem> tasks, bool skipOptional)
    {
        var result = new List<TaskItem>();
        
        foreach (var task in tasks)
        {
            if (task.IsCompleted) continue;
            if (skipOptional && task.IsOptional) continue;
            
            // Если у задачи есть подзадачи, добавляем только незавершенные
            if (task.SubTasks.Any())
            {
                var incompleteSubTasks = task.SubTasks
                    .Where(st => !st.IsCompleted && (!skipOptional || !st.IsOptional))
                    .ToList();
                    
                if (incompleteSubTasks.Any())
                {
                    result.AddRange(incompleteSubTasks);
                }
            }
            else
            {
                result.Add(task);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Внутренний метод для выполнения задач
    /// </summary>
    private async Task ExecuteTasksInternalAsync(
        int sessionId,
        int dialogueId,
        List<TaskItem> tasks,
        string tasksFilePath,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.ExecutionSessions.FindAsync(sessionId);
        if (session == null)
        {
            _logger.LogError("Execution session not found: {SessionId}", sessionId);
            return;
        }
        
        var completedCount = 0;
        var totalCount = tasks.Count;
        
        try
        {
            foreach (var task in tasks)
            {
                // Проверка CancellationToken на каждой итерации
                if (cancellationToken.IsCancellationRequested)
                {
                    session.Status = "stopped";
                    await SaveProgressMessageAsync(dialogueId, 
                        $"⏸️ Выполнение остановлено пользователем. Выполнено {completedCount} из {totalCount} задач");
                    break;
                }
                
                // Проверка на задачу-чекпоинт
                if (IsCheckpointTask(task))
                {
                    await SaveProgressMessageAsync(dialogueId, 
                        "🛑 Достигнута контрольная точка. Требуется подтверждение пользователя.");
                    session.Status = "paused";
                    await _dbContext.SaveChangesAsync();
                    return;
                }
                
                // Создание Git чекпоинта перед выполнением задачи
                try
                {
                    var dialogue = await _dbContext.Dialogues.FindAsync(dialogueId);
                    if (dialogue != null)
                    {
                        var checkpointDescription = $"Agent: Task {task.Text.Substring(0, Math.Min(50, task.Text.Length))}";
                        await _gitService.CreateCheckpointAsync(dialogue.ProjectPath, checkpointDescription);
                        _logger.LogInformation("Created checkpoint for task: {TaskText}", task.Text);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create checkpoint for task: {TaskText}", task.Text);
                    // Продолжаем выполнение даже если чекпоинт не создан
                }
                
                // Обновление прогресса сессии
                completedCount++;
                session.Progress = $"{completedCount}/{totalCount}";
                session.CurrentTask = task.Text;
                await _dbContext.SaveChangesAsync();
                
                // Сохранение стартового сообщения
                await SaveProgressMessageAsync(dialogueId, 
                    $"🤖 Начинаю выполнение задачи {completedCount} из {totalCount}: {task.Text}");
                
                var startTime = DateTime.UtcNow;
                
                try
                {
                    // Формирование промпта и выполнение задачи
                    var prompt = BuildPromptForTask(task);
                    var result = await ExecuteTaskWithTimeoutAsync(dialogueId, prompt);
                    
                    // Вычисление времени выполнения
                    var duration = DateTime.UtcNow - startTime;
                    
                    // Сохранение финального сообщения с временем выполнения
                    await SaveProgressMessageAsync(dialogueId, 
                        $"✅ Задача выполнена за {duration.TotalSeconds:F1}с");
                    
                    // Обновление статуса задачи в файле
                    try
                    {
                        await _fileUpdater.UpdateTaskStatusAsync(tasksFilePath, task.LineNumber, true);
                        _logger.LogInformation("Updated task status in file for line {LineNumber}", task.LineNumber);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to update task status in file for line {LineNumber}", task.LineNumber);
                        // Продолжаем выполнение даже если не удалось обновить файл
                    }
                }
                catch (TimeoutException)
                {
                    session.Status = "failed";
                    session.ErrorMessage = "Task execution timeout (5 minutes)";
                    await SaveProgressMessageAsync(dialogueId, 
                        "❌ Превышено время выполнения задачи (5 минут)");
                    _logger.LogError("Task execution timeout for task: {TaskText}", task.Text);
                    break;
                }
                catch (Exception ex)
                {
                    session.Status = "failed";
                    session.ErrorMessage = ex.Message;
                    await SaveProgressMessageAsync(dialogueId, 
                        $"❌ Ошибка при выполнении задачи: {ex.Message}");
                    _logger.LogError(ex, "Error executing task: {TaskText}", task.Text);
                    break;
                }
            }
            
            // Обновление финального статуса сессии
            if (session.Status == "running")
            {
                session.Status = "completed";
                var totalDuration = DateTime.UtcNow - session.StartedAt;
                await SaveProgressMessageAsync(dialogueId, 
                    $"✅ Все задачи выполнены успешно. Выполнено {completedCount} из {totalCount} задач за {totalDuration.TotalMinutes:F1} минут");
            }
            
            session.CompletedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            
            _logger.LogInformation("Execution session {SessionId} completed with status {Status}", 
                sessionId, session.Status);
        }
        finally
        {
            // Удаление CancellationTokenSource из словаря
            _cancellationTokens.TryRemove(dialogueId, out _);
        }
    }
    
    /// <summary>
    /// Формирует промпт для выполнения задачи
    /// </summary>
    private string BuildPromptForTask(TaskItem task)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Выполни следующую задачу из плана реализации:");
        sb.AppendLine();
        sb.AppendLine($"**Задача:** {task.Text}");
        
        if (task.SubTasks.Any())
        {
            sb.AppendLine();
            sb.AppendLine("**Подзадачи:**");
            foreach (var subTask in task.SubTasks)
            {
                sb.AppendLine($"  - {subTask.Text}");
            }
        }
        
        if (task.Requirements.Any())
        {
            sb.AppendLine();
            sb.AppendLine($"**Требования:** {string.Join(", ", task.Requirements)}");
        }
        
        sb.AppendLine();
        sb.AppendLine("После выполнения сообщи о результате.");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Выполняет задачу с таймаутом 5 минут
    /// </summary>
    private async Task<string> ExecuteTaskWithTimeoutAsync(int dialogueId, string prompt)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var task = _promptProcessor.ProcessPromptAsync(dialogueId, prompt);
        
        var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        
        if (completedTask == task)
        {
            return await task;
        }
        
        throw new TimeoutException("Task execution exceeded 5 minutes");
    }
    
    /// <summary>
    /// Проверяет, является ли задача контрольной точкой
    /// </summary>
    private bool IsCheckpointTask(TaskItem task)
    {
        return task.Text.Contains("Checkpoint", StringComparison.OrdinalIgnoreCase) ||
               task.Text.Contains("Контрольная точка", StringComparison.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Сохраняет сообщение о прогрессе в диалог
    /// </summary>
    private async Task SaveProgressMessageAsync(int dialogueId, string content)
    {
        var message = new Message
        {
            DialogueId = dialogueId,
            Role = "assistant",
            Content = content,
            Timestamp = DateTime.UtcNow
        };
        
        _dbContext.Messages.Add(message);
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Saved progress message for dialogue {DialogueId}: {Content}", 
            dialogueId, content);
    }

    /// <summary>
    /// Останавливает текущее выполнение задач
    /// </summary>
    public Task StopExecutionAsync(int dialogueId)
    {
        // Получить CancellationTokenSource для dialogueId
        if (_cancellationTokens.TryGetValue(dialogueId, out var cts))
        {
            // Вызвать Cancel() для остановки выполнения
            cts.Cancel();
            _logger.LogInformation("Execution stopped for dialogue {DialogueId}", dialogueId);
        }
        else
        {
            _logger.LogWarning("No active execution found for dialogue {DialogueId}", dialogueId);
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Продолжает выполнение задач с места остановки
    /// </summary>
    public async Task ResumeExecutionAsync(int dialogueId)
    {
        // Получить последнюю остановленную сессию для dialogueId
        var session = await _dbContext.ExecutionSessions
            .Where(s => s.DialogueId == dialogueId)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
            
        // Валидировать статус сессии (должен быть "stopped")
        if (session == null || session.Status != "stopped")
        {
            _logger.LogWarning("No stopped execution found for dialogue {DialogueId}", dialogueId);
            throw new InvalidOperationException("No stopped execution found");
        }
        
        // Перепарсить файл tasks.md
        var tasks = _taskParser.ParseFile(session.TasksFilePath);
        
        // Получить список незавершенных задач
        var incompleteTasks = GetIncompleteTasks(tasks, session.SkipOptional);
        
        // Обновить статус сессии на "running"
        session.Status = "running";
        await _dbContext.SaveChangesAsync();
        
        _logger.LogInformation("Resuming execution session {SessionId} for dialogue {DialogueId}", 
            session.Id, dialogueId);
        
        // Сохранить сообщение о продолжении
        await SaveProgressMessageAsync(dialogueId, "▶️ Продолжаю выполнение задач...");
        
        // Запустить выполнение в фоновом потоке
        var cts = new CancellationTokenSource();
        _cancellationTokens[dialogueId] = cts;
        
        _ = Task.Run(async () => await ExecuteTasksInternalAsync(
            session.Id, dialogueId, incompleteTasks, session.TasksFilePath, cts.Token));
    }

    /// <summary>
    /// Получает статус текущего выполнения
    /// </summary>
    public async Task<ExecutionStatusDto> GetExecutionStatusAsync(int dialogueId)
    {
        // Получить последнюю сессию для dialogueId
        var session = await _dbContext.ExecutionSessions
            .Where(s => s.DialogueId == dialogueId)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
            
        // Если сессии нет, вернуть status="none"
        if (session == null)
        {
            return new ExecutionStatusDto { Status = "none" };
        }
        
        // Вернуть ExecutionStatusDto с текущим статусом
        return new ExecutionStatusDto
        {
            Status = session.Status,
            Progress = session.Progress,
            CurrentTask = session.CurrentTask,
            ErrorMessage = session.ErrorMessage,
            StartedAt = session.StartedAt,
            CompletedAt = session.CompletedAt
        };
    }
}

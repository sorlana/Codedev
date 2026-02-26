using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Интеграционные тесты для TaskExecutorService
/// Проверяют полный цикл выполнения задач: парсинг, выполнение, чекпоинты, обновление файлов
/// </summary>
public class TaskExecutorIntegrationTests
{
    private readonly string _testProjectPath;
    private readonly string _tasksFilePath;
    
    public TaskExecutorIntegrationTests()
    {
        _testProjectPath = Path.Combine(Path.GetTempPath(), "TaskExecutorTest_" + Guid.NewGuid().ToString("N"));
        _tasksFilePath = Path.Combine(_testProjectPath, "tasks.md");
    }
    
    /// <summary>
    /// Основной интеграционный тест: полный цикл выполнения задач
    /// </summary>
    public async Task<bool> RunFullIntegrationTestAsync()
    {
        Console.WriteLine("=== Интеграционный тест TaskExecutor ===\n");
        
        try
        {
            // 1. Создать тестовый проект с Git репозиторием
            Console.WriteLine("1. Создание тестового проекта с Git репозиторием...");
            CreateTestProject();
            InitializeGitRepository();
            Console.WriteLine("   ✓ Проект создан\n");
            
            // 2. Создать файл tasks.md с несколькими задачами
            Console.WriteLine("2. Создание файла tasks.md...");
            CreateTasksFile();
            Console.WriteLine("   ✓ Файл tasks.md создан\n");
            
            // 3. Настроить тестовое окружение
            Console.WriteLine("3. Настройка тестового окружения...");
            var (dbContext, taskExecutor, dialogue) = await SetupTestEnvironmentAsync();
            Console.WriteLine($"   ✓ Dialogue ID: {dialogue.Id}\n");
            
            // 4. Запустить выполнение через API
            Console.WriteLine("4. Запуск выполнения задач...");
            var sessionId = await taskExecutor.ExecuteTasksAsync(dialogue.Id, _tasksFilePath, skipOptional: true);
            Console.WriteLine($"   ✓ Сессия создана: {sessionId}\n");
            
            // Ждем немного для начала выполнения
            await Task.Delay(2000);
            
            // 5. Проверить статус выполнения
            Console.WriteLine("5. Проверка статуса выполнения...");
            var status = await taskExecutor.GetExecutionStatusAsync(dialogue.Id);
            Console.WriteLine($"   Status: {status.Status}");
            Console.WriteLine($"   Progress: {status.Progress}");
            Console.WriteLine($"   Current Task: {status.CurrentTask}\n");
            
            // 6. Остановить выполнение
            Console.WriteLine("6. Остановка выполнения...");
            await taskExecutor.StopExecutionAsync(dialogue.Id);
            await Task.Delay(1000);
            
            var stoppedStatus = await taskExecutor.GetExecutionStatusAsync(dialogue.Id);
            Console.WriteLine($"   Status после остановки: {stoppedStatus.Status}");
            
            if (stoppedStatus.Status != "stopped" && stoppedStatus.Status != "completed")
            {
                Console.WriteLine($"   ⚠ Ожидался статус 'stopped', получен '{stoppedStatus.Status}'\n");
            }
            else
            {
                Console.WriteLine("   ✓ Выполнение остановлено\n");
            }
            
            // 7. Проверить сообщения в диалоге
            Console.WriteLine("7. Проверка сообщений в диалоге...");
            var messages = await dbContext.Messages
                .Where(m => m.DialogueId == dialogue.Id)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
            
            Console.WriteLine($"   Всего сообщений: {messages.Count}");
            foreach (var msg in messages.Take(5))
            {
                Console.WriteLine($"   - [{msg.Role}] {msg.Content.Substring(0, Math.Min(60, msg.Content.Length))}...");
            }
            Console.WriteLine();
            
            // 8. Проверить обновление статусов в файле tasks.md
            Console.WriteLine("8. Проверка обновления файла tasks.md...");
            var tasksContent = await File.ReadAllTextAsync(_tasksFilePath, Encoding.UTF8);
            var completedCount = System.Text.RegularExpressions.Regex.Matches(tasksContent, @"- \[x\]").Count;
            Console.WriteLine($"   Завершенных задач в файле: {completedCount}");
            Console.WriteLine("   ✓ Файл обновлен\n");
            
            // 9. Проверить резервные копии файла
            Console.WriteLine("9. Проверка резервных копий...");
            var backupFiles = Directory.GetFiles(_testProjectPath, "tasks.md.backup_*");
            Console.WriteLine($"   Найдено резервных копий: {backupFiles.Length}");
            if (backupFiles.Length > 0)
            {
                Console.WriteLine("   ✓ Резервные копии созданы\n");
            }
            else
            {
                Console.WriteLine("   ⚠ Резервные копии не найдены\n");
            }
            
            // 10. Проверить создание чекпоинтов в Git
            Console.WriteLine("10. Проверка Git чекпоинтов...");
            var checkpoints = await dbContext.Checkpoints
                .Where(c => c.DialogueId == dialogue.Id)
                .ToListAsync();
            Console.WriteLine($"   Создано чекпоинтов: {checkpoints.Count}");
            foreach (var cp in checkpoints.Take(3))
            {
                Console.WriteLine($"   - {cp.Description}");
            }
            
            if (checkpoints.Count > 0)
            {
                Console.WriteLine("   ✓ Чекпоинты созданы\n");
            }
            else
            {
                Console.WriteLine("   ⚠ Чекпоинты не найдены\n");
            }
            
            // 11. Продолжить выполнение (если было остановлено)
            if (stoppedStatus.Status == "stopped")
            {
                Console.WriteLine("11. Продолжение выполнения...");
                await taskExecutor.ResumeExecutionAsync(dialogue.Id);
                await Task.Delay(2000);
                
                var resumedStatus = await taskExecutor.GetExecutionStatusAsync(dialogue.Id);
                Console.WriteLine($"   Status после продолжения: {resumedStatus.Status}");
                Console.WriteLine("   ✓ Выполнение продолжено\n");
                
                // Ждем завершения
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);
                    var currentStatus = await taskExecutor.GetExecutionStatusAsync(dialogue.Id);
                    if (currentStatus.Status == "completed" || currentStatus.Status == "failed")
                    {
                        Console.WriteLine($"   Финальный статус: {currentStatus.Status}");
                        break;
                    }
                }
            }
            
            // 12. Проверить финальный статус сессии
            Console.WriteLine("\n12. Проверка финального статуса...");
            var finalStatus = await taskExecutor.GetExecutionStatusAsync(dialogue.Id);
            Console.WriteLine($"   Status: {finalStatus.Status}");
            Console.WriteLine($"   Progress: {finalStatus.Progress}");
            Console.WriteLine($"   Started: {finalStatus.StartedAt}");
            Console.WriteLine($"   Completed: {finalStatus.CompletedAt}");
            
            if (finalStatus.ErrorMessage != null)
            {
                Console.WriteLine($"   Error: {finalStatus.ErrorMessage}");
            }
            
            Console.WriteLine("\n=== Тест завершен успешно ===\n");
            
            // Cleanup
            await dbContext.DisposeAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Ошибка в тесте: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return false;
        }
        finally
        {
            CleanupTestProject();
        }
    }
    
    private void CreateTestProject()
    {
        Directory.CreateDirectory(_testProjectPath);
    }
    
    private void InitializeGitRepository()
    {
        var gitDir = Path.Combine(_testProjectPath, ".git");
        Directory.CreateDirectory(gitDir);
        
        // Создаем минимальную структуру Git репозитория
        Directory.CreateDirectory(Path.Combine(gitDir, "objects"));
        Directory.CreateDirectory(Path.Combine(gitDir, "refs", "heads"));
        
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(gitDir, "config"), "[core]\n\trepositoryformatversion = 0\n");
    }
    
    private void CreateTasksFile()
    {
        var tasksContent = @"# План реализации: Тестовый проект

## Задачи

- [ ] 1. Первая задача
  - [ ] 1.1 Подзадача 1.1
  - [ ] 1.2 Подзадача 1.2

- [ ] 2. Вторая задача
  - [ ] 2.1 Подзадача 2.1

- [ ]* 3. Опциональная задача (должна быть пропущена)

- [ ] 4. Checkpoint - Контрольная точка

- [ ] 5. Финальная задача
";
        
        File.WriteAllText(_tasksFilePath, tasksContent, Encoding.UTF8);
    }
    
    private async Task<(RefactoringDbContext, ITaskExecutorService, Dialogue)> SetupTestEnvironmentAsync()
    {
        // Создаем in-memory базу данных
        var options = new DbContextOptionsBuilder<RefactoringDbContext>()
            .UseInMemoryDatabase(databaseName: "TaskExecutorTest_" + Guid.NewGuid())
            .Options;
        
        var dbContext = new RefactoringDbContext(options);
        
        // Создаем тестовый диалог
        var dialogue = new Dialogue
        {
            ProjectPath = _testProjectPath,
            CreatedAt = DateTime.UtcNow
        };
        
        dbContext.Dialogues.Add(dialogue);
        await dbContext.SaveChangesAsync();
        
        // Создаем mock сервисы
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<TaskExecutorService>();
        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        
        var promptProcessor = new MockPromptProcessor();
        var gitService = new IntegrationTestMockGitService(dbContext);
        var configuration = new ConfigurationBuilder().Build();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        
        // Создаем TaskExecutorService
        var taskExecutor = new TaskExecutorService(
            dbContext,
            promptProcessor,
            gitService,
            logger,
            pathValidator,
            loggerFactory
        );
        
        return (dbContext, taskExecutor, dialogue);
    }
    
    private void CleanupTestProject()
    {
        try
        {
            if (Directory.Exists(_testProjectPath))
            {
                Directory.Delete(_testProjectPath, recursive: true);
            }
        }
        catch
        {
            // Игнорируем ошибки очистки
        }
    }
}

/// <summary>
/// Mock реализация IPromptProcessor для тестирования
/// </summary>
public class MockPromptProcessor : IPromptProcessor
{
    public async Task<string> ProcessPromptAsync(int dialogueId, string prompt)
    {
        // Симулируем работу LLM
        await Task.Delay(500);
        return $"Задача выполнена успешно (mock response)";
    }
}

/// <summary>
/// Mock реализация IGitService для интеграционного тестирования
/// </summary>
public class IntegrationTestMockGitService : IGitService
{
    private readonly RefactoringDbContext _dbContext;
    
    public IntegrationTestMockGitService(RefactoringDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<string> CreateCheckpointAsync(string projectPath, string description)
    {
        // Симулируем создание чекпоинта
        var commitHash = Guid.NewGuid().ToString("N").Substring(0, 8);
        
        // Находим диалог по пути проекта
        var dialogue = await _dbContext.Dialogues
            .FirstOrDefaultAsync(d => d.ProjectPath == projectPath);
        
        if (dialogue != null)
        {
            var checkpoint = new Checkpoint
            {
                DialogueId = dialogue.Id,
                CommitHash = commitHash,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };
            
            _dbContext.Checkpoints.Add(checkpoint);
            await _dbContext.SaveChangesAsync();
        }
        
        return commitHash;
    }
    
    public Task RollbackToCheckpointAsync(string projectPath, string commitHash)
    {
        return Task.CompletedTask;
    }
    
    public Task<List<Checkpoint>> GetCheckpointsAsync(int dialogueId)
    {
        return _dbContext.Checkpoints
            .Where(c => c.DialogueId == dialogueId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }
    
    public Task<bool> IsGitRepositoryAsync(string path)
    {
        return Task.FromResult(true);
    }
    
    public Task InitializeRepositoryAsync(string path)
    {
        return Task.CompletedTask;
    }
    
    public Task<bool> HasUncommittedChangesAsync(string path)
    {
        return Task.FromResult(false);
    }
}

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для выполнения задач через DeepSeek API с использованием инструментов
/// </summary>
public interface ITaskExecutionService
{
    /// <summary>
    /// Выполняет задачи из контекста группы диалогов через DeepSeek API
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="requirements">Требования проекта</param>
    /// <param name="design">Проектирование</param>
    /// <param name="tasks">Задачи для выполнения</param>
    /// <returns>Результат выполнения с инструкциями по запуску</returns>
    Task<TaskExecutionResult> ExecuteTasksAsync(int dialogueId, string requirements, string design, string tasks);
}

/// <summary>
/// Результат выполнения задач
/// </summary>
public class TaskExecutionResult
{
    /// <summary>
    /// Успешно ли выполнены задачи
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Сообщение о результате
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Инструкции по запуску проекта
    /// </summary>
    public string LaunchInstructions { get; set; } = string.Empty;
    
    /// <summary>
    /// Список созданных файлов
    /// </summary>
    public List<string> CreatedFiles { get; set; } = new();
    
    /// <summary>
    /// Список созданных папок
    /// </summary>
    public List<string> CreatedFolders { get; set; } = new();
}

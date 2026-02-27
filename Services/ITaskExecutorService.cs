using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для управления автоматическим выполнением задач из файла tasks.md
/// </summary>
public interface ITaskExecutorService
{
    /// <summary>
    /// Запускает выполнение задач из файла tasks.md
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="tasksFilePath">Путь к файлу tasks.md</param>
    /// <param name="skipOptional">Пропускать опциональные задачи (по умолчанию true)</param>
    /// <returns>ID созданной сессии выполнения</returns>
    Task<int> ExecuteTasksAsync(int dialogueId, string tasksFilePath, bool skipOptional = true);
    
    /// <summary>
    /// Запускает выполнение конкретной задачи по номеру из файла tasks.md
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="tasksFilePath">Путь к файлу tasks.md</param>
    /// <param name="taskNumber">Номер задачи для выполнения</param>
    /// <returns>ID созданной сессии выполнения</returns>
    Task<int> ExecuteSpecificTaskAsync(int dialogueId, string tasksFilePath, int taskNumber);
    
    /// <summary>
    /// Останавливает текущее выполнение задач
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    Task StopExecutionAsync(int dialogueId);
    
    /// <summary>
    /// Продолжает выполнение задач с места остановки
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    Task ResumeExecutionAsync(int dialogueId);
    
    /// <summary>
    /// Получает статус текущего выполнения
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <returns>Статус выполнения задач</returns>
    Task<ExecutionStatusDto> GetExecutionStatusAsync(int dialogueId);
}

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для использования reasoning модели для планирования задач
/// </summary>
public interface IReasoningService
{
    /// <summary>
    /// Создаёт детальный план выполнения задачи используя reasoning модель
    /// </summary>
    /// <param name="taskDescription">Описание задачи</param>
    /// <param name="projectPath">Путь к проекту</param>
    /// <returns>Детальный план выполнения</returns>
    Task<string> CreateTaskPlanAsync(string taskDescription, string projectPath);
}

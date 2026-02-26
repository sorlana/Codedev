using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для управления списком C# проектов
/// </summary>
public interface IProjectManagementService
{
    /// <summary>
    /// Получает все проекты из списка
    /// </summary>
    /// <returns>Список всех проектов, отсортированных по выбранному статусу и дате добавления</returns>
    Task<List<Project>> GetAllProjectsAsync();
    
    /// <summary>
    /// Получает текущий выбранный проект
    /// </summary>
    /// <returns>Выбранный проект или null, если проект не выбран</returns>
    Task<Project?> GetSelectedProjectAsync();
    
    /// <summary>
    /// Добавляет новый проект в список
    /// </summary>
    /// <param name="projectPath">Путь к директории проекта</param>
    /// <returns>Добавленный проект</returns>
    /// <exception cref="ArgumentException">Если путь невалиден</exception>
    /// <exception cref="InvalidOperationException">Если проект уже существует в списке</exception>
    Task<Project> AddProjectAsync(string projectPath);
    
    /// <summary>
    /// Удаляет проект из списка
    /// </summary>
    /// <param name="projectId">ID проекта для удаления</param>
    /// <exception cref="ArgumentException">Если проект не найден</exception>
    Task DeleteProjectAsync(int projectId);
    
    /// <summary>
    /// Устанавливает проект как выбранный
    /// </summary>
    /// <param name="projectId">ID проекта для выбора</param>
    /// <exception cref="ArgumentException">Если проект не найден</exception>
    Task SelectProjectAsync(int projectId);
}

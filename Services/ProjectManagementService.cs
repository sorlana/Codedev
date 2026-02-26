using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;
using Microsoft.EntityFrameworkCore;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для управления списком C# проектов
/// </summary>
public class ProjectManagementService : IProjectManagementService
{
    private readonly RefactoringDbContext _dbContext;
    private readonly PathValidator _pathValidator;
    private readonly ILogger<ProjectManagementService> _logger;

    public ProjectManagementService(
        RefactoringDbContext dbContext,
        PathValidator pathValidator,
        ILogger<ProjectManagementService> logger)
    {
        _dbContext = dbContext;
        _pathValidator = pathValidator;
        _logger = logger;
    }

    /// <summary>
    /// Получает все проекты, отсортированные по выбранному статусу и дате добавления
    /// </summary>
    public async Task<List<Project>> GetAllProjectsAsync()
    {
        return await _dbContext.Projects
            .OrderByDescending(p => p.IsSelected)
            .ThenByDescending(p => p.AddedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Получает текущий выбранный проект
    /// </summary>
    public async Task<Project?> GetSelectedProjectAsync()
    {
        return await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.IsSelected);
    }

    /// <summary>
    /// Добавляет новый проект в список
    /// </summary>
    public async Task<Project> AddProjectAsync(string projectPath)
    {
        // Валидация пути
        if (!_pathValidator.ValidatePath(projectPath, out var errorMessage))
        {
            _logger.LogWarning("Попытка добавить невалидный путь проекта: {ProjectPath}. Ошибка: {ErrorMessage}", 
                projectPath, errorMessage);
            throw new ArgumentException(errorMessage);
        }

        // Проверка на дубликат
        var existing = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Path == projectPath);
        
        if (existing != null)
        {
            _logger.LogWarning("Попытка добавить дубликат проекта: {ProjectPath}", projectPath);
            throw new InvalidOperationException("Проект уже добавлен в список");
        }

        // Извлечение имени проекта из пути
        var projectName = System.IO.Path.GetFileName(projectPath.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = projectPath;
        }

        var project = new Project
        {
            Name = projectName,
            Path = projectPath,
            AddedAt = DateTime.UtcNow,
            IsSelected = false
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Добавлен проект: {ProjectName} по пути {ProjectPath}", projectName, projectPath);

        return project;
    }

    /// <summary>
    /// Удаляет проект из списка
    /// </summary>
    public async Task DeleteProjectAsync(int projectId)
    {
        var project = await _dbContext.Projects.FindAsync(projectId);
        
        if (project == null)
        {
            _logger.LogWarning("Попытка удалить несуществующий проект с ID: {ProjectId}", projectId);
            throw new ArgumentException("Проект не найден");
        }

        _dbContext.Projects.Remove(project);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Удален проект: {ProjectName} (ID: {ProjectId})", project.Name, projectId);
    }

    /// <summary>
    /// Устанавливает проект как выбранный
    /// </summary>
    public async Task SelectProjectAsync(int projectId)
    {
        // Снимаем выбор со всех проектов
        var allProjects = await _dbContext.Projects.ToListAsync();
        foreach (var p in allProjects)
        {
            p.IsSelected = false;
        }

        // Выбираем указанный проект
        var project = allProjects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
        {
            _logger.LogWarning("Попытка выбрать несуществующий проект с ID: {ProjectId}", projectId);
            throw new ArgumentException("Проект не найден");
        }

        project.IsSelected = true;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Выбран проект: {ProjectName} (ID: {ProjectId})", project.Name, projectId);
    }
}

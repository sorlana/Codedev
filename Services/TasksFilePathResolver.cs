namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для разрешения пути к файлу tasks.md
/// </summary>
public class TasksFilePathResolver
{
    private readonly PathValidator _pathValidator;
    private readonly ILogger<TasksFilePathResolver> _logger;

    public TasksFilePathResolver(PathValidator pathValidator, ILogger<TasksFilePathResolver> logger)
    {
        _pathValidator = pathValidator;
        _logger = logger;
    }

    /// <summary>
    /// Разрешает путь к файлу tasks.md
    /// </summary>
    /// <param name="userProvidedPath">Путь, указанный пользователем (может быть null)</param>
    /// <param name="projectPath">Абсолютный путь к корню проекта</param>
    /// <returns>Абсолютный путь к файлу tasks.md</returns>
    /// <exception cref="FileNotFoundException">Файл tasks.md не найден</exception>
    /// <exception cref="InvalidOperationException">Путь невалиден</exception>
    public async Task<string> ResolveTasksFilePathAsync(string? userProvidedPath, string projectPath)
    {
        // Случай 1: Пользователь указал путь
        if (!string.IsNullOrWhiteSpace(userProvidedPath))
        {
            return await ResolveUserProvidedPathAsync(userProvidedPath, projectPath);
        }

        // Случай 2: Поиск tasks.md в корне проекта
        return await ResolveDefaultPathAsync(projectPath);
    }

    /// <summary>
    /// Обрабатывает случай с указанным пользователем путем
    /// </summary>
    private async Task<string> ResolveUserProvidedPathAsync(string userProvidedPath, string projectPath)
    {
        _logger.LogInformation("Разрешение пути, указанного пользователем: {UserPath}", userProvidedPath);

        // Комбинируем с корнем проекта для получения абсолютного пути
        string fullPath;
        try
        {
            fullPath = Path.IsPathFullyQualified(userProvidedPath)
                ? userProvidedPath
                : Path.Combine(projectPath, userProvidedPath);

            fullPath = Path.GetFullPath(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось построить полный путь из {UserPath}", userProvidedPath);
            throw new InvalidOperationException(
                $"Невалидный путь к файлу: {userProvidedPath}. Проверьте формат пути.", ex);
        }

        // Валидация пути через PathValidator
        if (!_pathValidator.ValidatePath(Path.GetDirectoryName(fullPath)!, out var errorMessage))
        {
            _logger.LogWarning("Валидация пути не прошла: {Path}, ошибка: {Error}", fullPath, errorMessage);
            throw new InvalidOperationException(
                $"Невалидный путь к файлу: {userProvidedPath}. {errorMessage}");
        }

        // Проверка существования файла
        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("Файл не найден по указанному пути: {Path}", fullPath);
            throw new FileNotFoundException(
                $"Файл не найден: {userProvidedPath}. Проверьте путь и попробуйте снова.");
        }

        _logger.LogInformation("Путь успешно разрешен: {FullPath}", fullPath);
        return fullPath;
    }

    /// <summary>
    /// Обрабатывает случай без указанного пути (поиск в корне проекта)
    /// </summary>
    private async Task<string> ResolveDefaultPathAsync(string projectPath)
    {
        _logger.LogInformation("Поиск tasks.md в корне проекта: {ProjectPath}", projectPath);

        // Валидация корня проекта
        if (!_pathValidator.ValidatePath(projectPath, out var errorMessage))
        {
            _logger.LogWarning("Валидация корня проекта не прошла: {Path}, ошибка: {Error}", 
                projectPath, errorMessage);
            throw new InvalidOperationException(
                $"Невалидный путь к проекту: {projectPath}. {errorMessage}");
        }

        // Поиск tasks.md в корне проекта
        var defaultPath = Path.Combine(projectPath, "tasks.md");

        if (!File.Exists(defaultPath))
        {
            _logger.LogWarning("Файл tasks.md не найден в корне проекта: {Path}", defaultPath);
            throw new FileNotFoundException(
                "Файл tasks.md не найден в корне проекта. Укажите путь к файлу.");
        }

        _logger.LogInformation("Файл tasks.md найден в корне проекта: {Path}", defaultPath);
        return defaultPath;
    }
}

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для обновления статуса задач в файле tasks.md
/// </summary>
public class TaskFileUpdater
{
    private readonly ILogger<TaskFileUpdater> _logger;
    
    public TaskFileUpdater(ILogger<TaskFileUpdater> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Обновляет статус задачи в файле
    /// </summary>
    /// <param name="filePath">Путь к файлу tasks.md</param>
    /// <param name="lineNumber">Номер строки с задачей (0-based)</param>
    /// <param name="isCompleted">Новый статус задачи (true = завершена, false = незавершена)</param>
    public async Task UpdateTaskStatusAsync(string filePath, int lineNumber, bool isCompleted)
    {
        try
        {
            // Создаем резервную копию
            await CreateBackupAsync(filePath);
            
            // Читаем файл с UTF-8 кодировкой
            var lines = await File.ReadAllLinesAsync(filePath, System.Text.Encoding.UTF8);
            
            // Обновляем статус
            if (lineNumber >= 0 && lineNumber < lines.Length)
            {
                var line = lines[lineNumber];
                if (isCompleted)
                {
                    lines[lineNumber] = line.Replace("- [ ]", "- [x]");
                }
                else
                {
                    lines[lineNumber] = line.Replace("- [x]", "- [ ]");
                }
            }
            
            // Записываем обратно с UTF-8 кодировкой
            await File.WriteAllLinesAsync(filePath, lines, System.Text.Encoding.UTF8);
            
            _logger.LogInformation("Обновлен статус задачи на строке {LineNumber} в файле {FilePath}", 
                lineNumber, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить статус задачи в файле {FilePath}", filePath);
            throw;
        }
    }
    
    /// <summary>
    /// Создает резервную копию файла с timestamp в имени
    /// </summary>
    /// <param name="filePath">Путь к файлу для резервного копирования</param>
    private Task CreateBackupAsync(string filePath)
    {
        var backupPath = $"{filePath}.backup_{DateTime.Now:yyyyMMddHHmmss}";
        File.Copy(filePath, backupPath);
        _logger.LogInformation("Создана резервная копия: {BackupPath}", backupPath);
        return Task.CompletedTask;
    }
}

namespace CSharpRefactoringAssistant.Models;

/// <summary>
/// Сессия выполнения задач из файла tasks.md в агентском режиме
/// </summary>
public class ExecutionSession
{
    /// <summary>
    /// Уникальный идентификатор сессии
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// ID диалога, к которому привязана сессия
    /// </summary>
    public int DialogueId { get; set; }
    
    /// <summary>
    /// Навигационное свойство к диалогу
    /// </summary>
    public Dialogue Dialogue { get; set; } = null!;
    
    /// <summary>
    /// Путь к файлу tasks.md
    /// </summary>
    public string TasksFilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Статус выполнения: running, completed, failed, stopped, paused
    /// </summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>
    /// Прогресс выполнения в формате "N/M"
    /// </summary>
    public string Progress { get; set; } = string.Empty;
    
    /// <summary>
    /// Текст текущей выполняемой задачи
    /// </summary>
    public string? CurrentTask { get; set; }
    
    /// <summary>
    /// Сообщение об ошибке (если выполнение завершилось с ошибкой)
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Время начала выполнения
    /// </summary>
    public DateTime StartedAt { get; set; }
    
    /// <summary>
    /// Время завершения выполнения
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Флаг пропуска опциональных задач
    /// </summary>
    public bool SkipOptional { get; set; }
}

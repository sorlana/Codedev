namespace CSharpRefactoringAssistant.Models;

/// <summary>
/// Группа диалогов с общим контекстом
/// </summary>
public class DialogueGroup
{
    public int Id { get; set; }
    
    /// <summary>
    /// Название группы
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Путь к проекту
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Контекст: Требования (requirements.md)
    /// </summary>
    public string? Requirements { get; set; }
    
    /// <summary>
    /// Контекст: Проектирование (design.md)
    /// </summary>
    public string? Design { get; set; }
    
    /// <summary>
    /// Контекст: Задачи (tasks.md)
    /// </summary>
    public string? Tasks { get; set; }
    
    /// <summary>
    /// Дата создания группы
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Свернута ли группа в UI
    /// </summary>
    public bool IsCollapsed { get; set; } = false;
    
    /// <summary>
    /// Диалоги в группе
    /// </summary>
    public List<Dialogue> Dialogues { get; set; } = new();
}

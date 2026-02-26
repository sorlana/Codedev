namespace CSharpRefactoringAssistant.Models;

/// <summary>
/// Представляет элемент задачи из файла tasks.md
/// </summary>
public class TaskItem
{
    /// <summary>
    /// Номер строки в файле tasks.md (0-based)
    /// </summary>
    public int LineNumber { get; set; }
    
    /// <summary>
    /// Уровень вложенности задачи (0 = корневая задача, 1 = подзадача первого уровня и т.д.)
    /// Вычисляется как количество отступов / 2 (2 пробела = 1 уровень)
    /// </summary>
    public int IndentLevel { get; set; }
    
    /// <summary>
    /// Флаг завершенности задачи (true если чекбокс [x], false если [ ])
    /// </summary>
    public bool IsCompleted { get; set; }
    
    /// <summary>
    /// Флаг опциональности задачи (true если чекбокс [ ]*)
    /// </summary>
    public bool IsOptional { get; set; }
    
    /// <summary>
    /// Текст задачи (без чекбокса и маркера опциональности)
    /// </summary>
    public string Text { get; set; } = string.Empty;
    
    /// <summary>
    /// Список требований, связанных с задачей (извлекается из строки "_Требования: X.Y, X.Z_")
    /// </summary>
    public List<string> Requirements { get; set; } = new();
    
    /// <summary>
    /// Список подзадач (вложенные задачи с большим IndentLevel)
    /// </summary>
    public List<TaskItem> SubTasks { get; set; } = new();
}

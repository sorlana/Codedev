namespace CSharpRefactoringAssistant.Models;

public class Dialogue
{
    public int Id { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// ID группы диалогов (nullable для обратной совместимости)
    /// </summary>
    public int? DialogueGroupId { get; set; }
    
    /// <summary>
    /// Группа диалогов
    /// </summary>
    public DialogueGroup? DialogueGroup { get; set; }
    
    public List<Message> Messages { get; set; } = new();
    public List<Checkpoint> Checkpoints { get; set; } = new();
    public List<ExecutionSession> ExecutionSessions { get; set; } = new();
}

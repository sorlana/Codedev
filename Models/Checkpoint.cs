namespace CSharpRefactoringAssistant.Models;

public class Checkpoint
{
    public int Id { get; set; }
    public int DialogueId { get; set; }
    public Dialogue Dialogue { get; set; } = null!;
    public string CommitHash { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

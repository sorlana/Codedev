namespace CSharpRefactoringAssistant.Models;

public class Dialogue
{
    public int Id { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<Message> Messages { get; set; } = new();
    public List<Checkpoint> Checkpoints { get; set; } = new();
}

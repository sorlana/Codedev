namespace CSharpRefactoringAssistant.Models;

public class Message
{
    public int Id { get; set; }
    public int DialogueId { get; set; }
    public Dialogue Dialogue { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

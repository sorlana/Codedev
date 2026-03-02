namespace CSharpRefactoringAssistant.Models;

using System.ComponentModel.DataAnnotations.Schema;

public class Message
{
    public int Id { get; set; }
    public int DialogueId { get; set; }
    public Dialogue Dialogue { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Цепочка рассуждений модели (reasoning_content) для multi-turn tool calling
    /// </summary>
    public string? ReasoningContent { get; set; }
    
    // Временные поля для multi-turn tool calling (не сохраняются в БД)
    [NotMapped]
    public List<Dictionary<string, object>>? ToolCalls { get; set; }
    
    [NotMapped]
    public string? ToolCallId { get; set; }
}

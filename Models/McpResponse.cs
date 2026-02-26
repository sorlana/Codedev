namespace CSharpRefactoringAssistant.Models;

public class McpResponse
{
    public bool IsSuccess { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
}

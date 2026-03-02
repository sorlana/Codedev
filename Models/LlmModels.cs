namespace CSharpRefactoringAssistant.Models;

public class LlmResponse
{
    public string? TextContent { get; set; }
    public List<FunctionCall>? FunctionCalls { get; set; }
}

public class FunctionCall
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
}

public class FunctionDefinition
{
    public string Type { get; set; } = "function"; // Для DeepSeek API
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

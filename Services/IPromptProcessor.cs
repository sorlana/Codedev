namespace CSharpRefactoringAssistant.Services;

public interface IPromptProcessor
{
    Task<string> ProcessPromptAsync(int dialogueId, string prompt);
    
    /// <summary>
    /// Получает список доступных инструментов для LLM
    /// </summary>
    List<CSharpRefactoringAssistant.Models.FunctionDefinition> GetAvailableTools();
    
    /// <summary>
    /// Выполняет функцию по имени с заданными аргументами
    /// </summary>
    Task<string> ExecuteFunctionAsync(string functionName, Dictionary<string, object> arguments, string projectPath);
}

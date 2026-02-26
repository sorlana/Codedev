using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public interface ILlmService
{
    Task<LlmResponse> SendPromptAsync(string prompt, List<Message> history, List<FunctionDefinition> tools);
}

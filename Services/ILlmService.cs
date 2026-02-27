using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public interface ILlmService
{
    Task<LlmResponse> SendPromptAsync(string prompt, List<Message> history, List<FunctionDefinition> tools);
    
    // Потоковая передача ответа LLM по частям
    IAsyncEnumerable<string> StreamPromptAsync(
        string prompt, 
        List<Message> history, 
        List<FunctionDefinition> tools,
        CancellationToken cancellationToken = default);
}

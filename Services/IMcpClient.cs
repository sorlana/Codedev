using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public interface IMcpClient
{
    Task<McpResponse> CallToolAsync(string toolName, Dictionary<string, object> parameters);
    Task InitializeAsync();
    Task ShutdownAsync();
    bool IsConnected { get; }
}

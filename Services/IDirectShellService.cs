namespace CSharpRefactoringAssistant.Services;

public interface IDirectShellService
{
    Task<string> ExecuteCommandAsync(string command, string workingDirectory);
    Task<string> ReadFileAsync(string filePath, string workingDirectory);
}

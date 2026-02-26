namespace CSharpRefactoringAssistant.Services;

public interface ISerenaService
{
    Task<string> ActivateProjectAsync(string projectPath);
    Task<string> FindSymbolAsync(string symbolName);
    Task<string> FindReferencingSymbolsAsync(string symbolId);
    Task<string> ReplaceSymbolBodyAsync(string symbolId, string newBody);
    Task<string> ExecuteShellCommandAsync(string command, string workingDirectory);
    Task<string> ReadFileAsync(string filePath);
    Task<string> InsertBeforeSymbolAsync(string symbolId, string content);
    Task<string> DeleteLinesAsync(string filePath, int startLine, int endLine);
}

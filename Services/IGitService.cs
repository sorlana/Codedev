namespace CSharpRefactoringAssistant.Services;

public interface IGitService
{
    Task<bool> IsGitRepositoryAsync(string path);
    Task InitializeRepositoryAsync(string path);
    Task<string> CreateCheckpointAsync(string path, string message);
    Task RollbackToCheckpointAsync(string path, string commitHash);
    Task<bool> HasUncommittedChangesAsync(string path);
}

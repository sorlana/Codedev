namespace CSharpRefactoringAssistant.Services;

public interface IPromptProcessor
{
    Task<string> ProcessPromptAsync(int dialogueId, string prompt);
}

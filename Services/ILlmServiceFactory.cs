namespace CSharpRefactoringAssistant.Services;

public interface ILlmServiceFactory
{
    ILlmService CreateLlmService();
}

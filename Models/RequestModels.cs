namespace CSharpRefactoringAssistant.Models;

public record CreateDialogueRequest(string ProjectPath);

public record SendMessageRequest(string Content);

public record RollbackRequest(int CheckpointId);

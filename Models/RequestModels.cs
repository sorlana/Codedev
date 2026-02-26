namespace CSharpRefactoringAssistant.Models;

public record CreateDialogueRequest(string ProjectPath);

public record SendMessageRequest(string Content);

public record RollbackRequest(int CheckpointId);

public record AddProjectRequest(string ProjectPath);

public record CreateCheckpointRequest(string? Description);

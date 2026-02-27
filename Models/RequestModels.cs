namespace CSharpRefactoringAssistant.Models;

public record CreateDialogueRequest(string ProjectPath);

public record SendMessageRequest(string Content);

public record RollbackRequest(int CheckpointId);

public record AddProjectRequest(string ProjectPath);

public record CreateCheckpointRequest(string? Description);
public record ExecuteTasksRequest(string TasksFilePath, bool SkipOptional = true);

public enum AgentCommandType
{
    StartExecution,
    StopExecution,
    ResumeExecution,
    ShowStatus
}

public class ExecutionStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string? Progress { get; set; }
    public string? CurrentTask { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

// DTO для работы с группами диалогов
public record CreateDialogueGroupRequest(string Name, string ProjectPath);

public record UpdateDialogueGroupContextRequest(
    string? Requirements, 
    string? Design, 
    string? Tasks
);

public record UpdateDialogueGroupRequest(string Name, bool IsCollapsed);

public record CreateDialogueInGroupRequest(int GroupId);

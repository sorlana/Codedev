namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Service for validating system state at application startup
/// </summary>
public interface IStartupValidationService
{
    /// <summary>
    /// Validates connection to the configured LLM model
    /// </summary>
    /// <returns>Validation result with connection status and error message if applicable</returns>
    Task<ModelConnectionResult> ValidateModelConnectionAsync();
}

/// <summary>
/// Result of model connection validation
/// </summary>
public class ModelConnectionResult
{
    /// <summary>
    /// Whether the model connection was successful
    /// </summary>
    public bool IsConnected { get; set; }
    
    /// <summary>
    /// Name of the model that was validated
    /// </summary>
    public string? ModelName { get; set; }
    
    /// <summary>
    /// Error message if connection failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

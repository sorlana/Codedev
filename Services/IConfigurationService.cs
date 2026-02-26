using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Service interface for managing LLM configuration settings.
/// Handles reading, writing, and validating configuration for both cloud-based
/// providers (OpenAI-compatible) and local Ollama installations.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Retrieves the current LLM configuration from application settings.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains the current LlmConfiguration.
    /// </returns>
    /// <remarks>
    /// Reads configuration from appsettings.json via IConfiguration.
    /// Maps the Llm section to an LlmConfiguration object.
    /// Validates: Requirements 4.2
    /// </remarks>
    Task<LlmConfiguration> GetConfigurationAsync();

    /// <summary>
    /// Saves the provided LLM configuration to application settings.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// Validates the configuration before persisting.
    /// Updates the Llm section in appsettings.json.
    /// Reloads IConfiguration to apply changes immediately.
    /// Validates: Requirements 4.1, 4.3
    /// </remarks>
    Task SaveConfigurationAsync(LlmConfiguration config);

    /// <summary>
    /// Validates the provided LLM configuration.
    /// </summary>
    /// <param name="config">The configuration to validate.</param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// The task result contains true if the configuration is valid, false otherwise.
    /// </returns>
    /// <remarks>
    /// Validates required fields based on provider type:
    /// - For OpenAI provider: API key, base URL, and model must not be empty
    /// - For Ollama provider: base URL and model must not be empty
    /// Validates URL format for base URLs.
    /// Validates: Requirements 2.5, 2.6, 3.3, 3.4
    /// </remarks>
    Task<bool> ValidateConfigurationAsync(LlmConfiguration config);
}

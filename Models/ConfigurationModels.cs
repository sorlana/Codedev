namespace CSharpRefactoringAssistant.Models;

/// <summary>
/// Represents the complete LLM configuration including provider type and settings.
/// </summary>
public class LlmConfiguration
{
    /// <summary>
    /// The LLM provider type: "OpenAI" or "Ollama"
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    
    /// <summary>
    /// Settings for OpenAI-compatible providers (F5AI, DeepSeek, etc.)
    /// </summary>
    public ProviderSettings? OpenAI { get; set; }
    
    /// <summary>
    /// Settings for local Ollama installations
    /// </summary>
    public OllamaSettings? Ollama { get; set; }
}

/// <summary>
/// Settings for OpenAI-compatible cloud providers.
/// </summary>
public class ProviderSettings
{
    /// <summary>
    /// API key for authentication with the provider
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// The model name to use (e.g., "deepseek-chat", "gpt-4")
    /// </summary>
    public string Model { get; set; } = string.Empty;
    
    /// <summary>
    /// Base URL for the provider API (e.g., "https://api.deepseek.com/v1")
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Settings for local Ollama installations.
/// </summary>
public class OllamaSettings
{
    /// <summary>
    /// Base URL for the Ollama instance (e.g., "http://localhost:11434")
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// The model name to use (e.g., "llama2", "codellama")
    /// </summary>
    public string Model { get; set; } = string.Empty;
}

/// <summary>
/// Request model for saving configuration.
/// </summary>
public class SaveConfigurationRequest
{
    /// <summary>
    /// The LLM provider type: "OpenAI" or "Ollama"
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    
    /// <summary>
    /// Settings for OpenAI-compatible providers
    /// </summary>
    public ProviderSettings? OpenAI { get; set; }
    
    /// <summary>
    /// Settings for local Ollama installations
    /// </summary>
    public OllamaSettings? Ollama { get; set; }
}

/// <summary>
/// Response model for configuration operations.
/// </summary>
public class ConfigurationResponse
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Optional message providing details about the operation result
    /// </summary>
    public string? Message { get; set; }
    
    /// <summary>
    /// The current configuration (returned on successful GET or POST operations)
    /// </summary>
    public LlmConfiguration? Configuration { get; set; }
}

/// <summary>
/// Response model for Ollama models list endpoint.
/// </summary>
public class OllamaModelsResponse
{
    /// <summary>
    /// List of available model names from the Ollama instance
    /// </summary>
    public List<string> Models { get; set; } = new();
}

/// <summary>
/// Response model for connection test endpoint.
/// </summary>
public class TestConnectionResponse
{
    /// <summary>
    /// Indicates whether the connection test was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Message providing details about the test result
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

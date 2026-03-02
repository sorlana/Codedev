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
    /// Use DeepSeek API instead of local Ollama (applies globally)
    /// </summary>
    public bool UseDeepSeekApi { get; set; } = false;
    
    /// <summary>
    /// Settings for OpenAI-compatible providers (F5AI, DeepSeek, etc.)
    /// </summary>
    public ProviderSettings? OpenAI { get; set; }
    
    /// <summary>
    /// Settings for local Ollama installations
    /// </summary>
    public OllamaSettings? Ollama { get; set; }
    
    /// <summary>
    /// Settings for DeepSeek API
    /// </summary>
    public DeepSeekSettings? DeepSeek { get; set; }
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
    
    /// <summary>
    /// The reasoning model name for task planning (e.g., "deepseek-r1:7b")
    /// If not specified, uses the main Model for both planning and execution
    /// </summary>
    public string? ReasoningModel { get; set; }
}

/// <summary>
/// Settings for DeepSeek API.
/// </summary>
public class DeepSeekSettings
{
    /// <summary>
    /// DeepSeek API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Base URL for DeepSeek API (default: "https://api.deepseek.com")
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    
    /// <summary>
    /// Model for chat (default: "deepseek-chat")
    /// </summary>
    public string ChatModel { get; set; } = "deepseek-chat";
    
    /// <summary>
    /// Model for reasoning/planning (default: "deepseek-reasoner")
    /// </summary>
    public string ReasonerModel { get; set; } = "deepseek-reasoner";
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
    /// Use DeepSeek API instead of local Ollama
    /// </summary>
    public bool? UseDeepSeekApi { get; set; }
    
    /// <summary>
    /// Settings for OpenAI-compatible providers
    /// </summary>
    public ProviderSettings? OpenAI { get; set; }
    
    /// <summary>
    /// Settings for local Ollama installations
    /// </summary>
    public OllamaSettings? Ollama { get; set; }
    
    /// <summary>
    /// Settings for DeepSeek API
    /// </summary>
    public DeepSeekSettings? DeepSeek { get; set; }
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

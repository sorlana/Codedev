using CSharpRefactoringAssistant.Models;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Service for managing LLM configuration settings.
/// Handles reading, writing, and validating configuration for both cloud-based
/// providers (OpenAI-compatible) and local Ollama installations.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configFilePath;

    public ConfigurationService(
        IConfiguration configuration,
        ILogger<ConfigurationService> logger,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _logger = logger;
        _configFilePath = Path.Combine(environment.ContentRootPath, "appsettings.json");
    }

    /// <summary>
    /// Retrieves the current LLM configuration from application settings.
    /// Reads from IConfiguration and maps to LlmConfiguration DTO.
    /// </summary>
    public Task<LlmConfiguration> GetConfigurationAsync()
    {
        try
        {
            var config = new LlmConfiguration
            {
                Provider = _configuration["Llm:Provider"] ?? "OpenAI"
            };

            // Read OpenAI settings
            var openAiSection = _configuration.GetSection("Llm:OpenAI");
            if (openAiSection.Exists())
            {
                config.OpenAI = new ProviderSettings
                {
                    ApiKey = openAiSection["ApiKey"] ?? string.Empty,
                    Model = openAiSection["Model"] ?? string.Empty,
                    BaseUrl = openAiSection["BaseUrl"] ?? string.Empty
                };
            }

            // Read Ollama settings
            var ollamaSection = _configuration.GetSection("Llm:Ollama");
            if (ollamaSection.Exists())
            {
                config.Ollama = new OllamaSettings
                {
                    BaseUrl = ollamaSection["BaseUrl"] ?? string.Empty,
                    Model = ollamaSection["Model"] ?? string.Empty,
                    ReasoningModel = ollamaSection["ReasoningModel"]
                };
            }

            _logger.LogInformation("Successfully loaded LLM configuration. Provider: {Provider}", config.Provider);
            return Task.FromResult(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading LLM configuration");
            throw;
        }
    }

    /// <summary>
    /// Saves the provided LLM configuration to application settings.
    /// Reads appsettings.json, updates the Llm section, and writes back with file locking.
    /// </summary>
    public async Task SaveConfigurationAsync(LlmConfiguration config)
    {
        // Validate configuration before saving
        var isValid = await ValidateConfigurationAsync(config);
        if (!isValid)
        {
            throw new ArgumentException("Configuration validation failed. Cannot save invalid configuration.");
        }

        try
        {
            // Use file locking to prevent concurrent writes
            using var fileStream = new FileStream(
                _configFilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None); // Exclusive lock

            // Read existing configuration
            JsonDocument jsonDoc;
            try
            {
                jsonDoc = await JsonDocument.ParseAsync(fileStream);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse appsettings.json");
                throw new InvalidOperationException("Configuration file is not valid JSON", ex);
            }

            // Create a mutable dictionary from the existing configuration
            var configDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonDoc.RootElement.GetRawText())
                ?? new Dictionary<string, JsonElement>();

            // Build the Llm section
            var llmSection = new Dictionary<string, object?>
            {
                ["Provider"] = config.Provider
            };

            // Add OpenAI settings if present
            if (config.OpenAI != null)
            {
                llmSection["OpenAI"] = new Dictionary<string, string?>
                {
                    ["ApiKey"] = config.OpenAI.ApiKey,
                    ["Model"] = config.OpenAI.Model,
                    ["BaseUrl"] = config.OpenAI.BaseUrl
                };
            }

            // Add Ollama settings if present
            if (config.Ollama != null)
            {
                llmSection["Ollama"] = new Dictionary<string, string?>
                {
                    ["BaseUrl"] = config.Ollama.BaseUrl,
                    ["Model"] = config.Ollama.Model
                };
            }

            // Update the Llm section in the configuration dictionary
            configDict["Llm"] = JsonSerializer.SerializeToElement(llmSection);

            // Write back to file
            fileStream.SetLength(0); // Clear the file
            fileStream.Position = 0;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            await JsonSerializer.SerializeAsync(fileStream, configDict, options);
            await fileStream.FlushAsync();

            _logger.LogInformation("Successfully saved LLM configuration. Provider: {Provider}", config.Provider);

            // Note: IConfiguration reload happens automatically in ASP.NET Core
            // when the appsettings.json file is modified
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error while saving configuration. File may be locked by another process.");
            throw new InvalidOperationException("Unable to save configuration. File may be locked by another process.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied while saving configuration. Check file permissions.");
            throw new InvalidOperationException("Unable to save configuration. Access denied.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while saving configuration");
            throw;
        }
    }

    /// <summary>
    /// Validates the provided LLM configuration.
    /// Checks required fields based on provider type and validates URL formats.
    /// </summary>
    public Task<bool> ValidateConfigurationAsync(LlmConfiguration config)
    {
        try
        {
            if (config == null)
            {
                _logger.LogWarning("Configuration validation failed: config is null");
                return Task.FromResult(false);
            }

            if (string.IsNullOrWhiteSpace(config.Provider))
            {
                _logger.LogWarning("Configuration validation failed: Provider is required");
                return Task.FromResult(false);
            }

            // Validate based on provider type
            if (config.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                if (!ValidateOpenAIConfiguration(config.OpenAI))
                {
                    return Task.FromResult(false);
                }
            }
            else if (config.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                if (!ValidateOllamaConfiguration(config.Ollama))
                {
                    return Task.FromResult(false);
                }
            }
            else
            {
                _logger.LogWarning("Configuration validation failed: Unknown provider '{Provider}'", config.Provider);
                return Task.FromResult(false);
            }

            _logger.LogInformation("Configuration validation successful for provider: {Provider}", config.Provider);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during configuration validation");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Validates OpenAI provider configuration.
    /// Requires: API key, base URL, and model name.
    /// </summary>
    private bool ValidateOpenAIConfiguration(ProviderSettings? settings)
    {
        if (settings == null)
        {
            _logger.LogWarning("OpenAI configuration validation failed: settings are null");
            return false;
        }

        // Validate API key is not empty
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            _logger.LogWarning("OpenAI configuration validation failed: API key is required");
            return false;
        }

        // Validate base URL is not empty
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            _logger.LogWarning("OpenAI configuration validation failed: Base URL is required");
            return false;
        }

        // Validate base URL format
        if (!IsValidUrl(settings.BaseUrl))
        {
            _logger.LogWarning("OpenAI configuration validation failed: Base URL '{BaseUrl}' is not a valid URL", settings.BaseUrl);
            return false;
        }

        // Validate model is not empty
        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            _logger.LogWarning("OpenAI configuration validation failed: Model is required");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates Ollama configuration.
    /// Requires: base URL and model name.
    /// </summary>
    private bool ValidateOllamaConfiguration(OllamaSettings? settings)
    {
        if (settings == null)
        {
            _logger.LogWarning("Ollama configuration validation failed: settings are null");
            return false;
        }

        // Validate base URL is not empty
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            _logger.LogWarning("Ollama configuration validation failed: Base URL is required");
            return false;
        }

        // Validate base URL format
        if (!IsValidUrl(settings.BaseUrl))
        {
            _logger.LogWarning("Ollama configuration validation failed: Base URL '{BaseUrl}' is not a valid URL", settings.BaseUrl);
            return false;
        }

        // Validate model is not empty
        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            _logger.LogWarning("Ollama configuration validation failed: Model is required");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates if a string is a well-formed URL.
    /// Accepts both HTTP and HTTPS schemes.
    /// </summary>
    private bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // Try to create a URI and validate it's well-formed
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult))
        {
            return false;
        }

        // Ensure the scheme is HTTP or HTTPS
        return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
    }
}

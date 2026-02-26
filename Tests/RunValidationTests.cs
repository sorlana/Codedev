using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Standalone test runner for configuration validation tests
/// Run with: dotnet run --project CSharpRefactoringAssistant.csproj -- test-validation
/// </summary>
public class ValidationTestRunner
{
    public static async Task RunAllValidationTests()
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("CONFIGURATION VALIDATION TESTS");
        Console.WriteLine(new string('=', 60));

        try
        {
            await TestValidateOpenAIConfiguration();
            await TestValidateOllamaConfiguration();
            await TestUrlFormatValidation();
            await TestGeneralValidation();
            
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("✓✓✓ ALL VALIDATION TESTS PASSED! ✓✓✓");
            Console.WriteLine(new string('=', 60));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static async Task TestValidateOpenAIConfiguration()
    {
        Console.WriteLine("\n=== Testing OpenAI Configuration Validation ===");
        
        var service = CreateConfigurationService();

        // Test 1: Valid OpenAI configuration
        var validConfig = new LlmConfiguration
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "sk-test-key",
                Model = "deepseek-chat",
                BaseUrl = "https://api.deepseek.com/v1"
            }
        };
        var result = await service.ValidateConfigurationAsync(validConfig);
        if (!result)
            throw new Exception("Expected valid OpenAI configuration to pass validation");
        Console.WriteLine("✓ Valid OpenAI configuration passed");

        // Test 2: Missing API key
        var missingApiKey = new LlmConfiguration
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "",
                Model = "deepseek-chat",
                BaseUrl = "https://api.deepseek.com/v1"
            }
        };
        result = await service.ValidateConfigurationAsync(missingApiKey);
        if (result)
            throw new Exception("Expected configuration with empty API key to fail validation");
        Console.WriteLine("✓ Empty API key rejected");

        // Test 3: Whitespace-only API key
        var whitespaceApiKey = new LlmConfiguration
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "   ",
                Model = "deepseek-chat",
                BaseUrl = "https://api.deepseek.com/v1"
            }
        };
        result = await service.ValidateConfigurationAsync(whitespaceApiKey);
        if (result)
            throw new Exception("Expected configuration with whitespace-only API key to fail validation");
        Console.WriteLine("✓ Whitespace-only API key rejected");

        // Test 4: Missing base URL
        var missingBaseUrl = new LlmConfiguration
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "sk-test-key",
                Model = "deepseek-chat",
                BaseUrl = ""
            }
        };
        result = await service.ValidateConfigurationAsync(missingBaseUrl);
        if (result)
            throw new Exception("Expected configuration with empty base URL to fail validation");
        Console.WriteLine("✓ Empty base URL rejected");

        // Test 5: Invalid base URL format
        var invalidBaseUrl = new LlmConfiguration
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "sk-test-key",
                Model = "deepseek-chat",
                BaseUrl = "not-a-valid-url"
            }
        };
        result = await service.ValidateConfigurationAsync(invalidBaseUrl);
        if (result)
            throw new Exception("Expected configuration with invalid base URL to fail validation");
        Console.WriteLine("✓ Invalid base URL format rejected");

        // Test 6: Missing model
        var missingModel = new LlmConfiguration
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "sk-test-key",
                Model = "",
                BaseUrl = "https://api.deepseek.com/v1"
            }
        };
        result = await service.ValidateConfigurationAsync(missingModel);
        if (result)
            throw new Exception("Expected configuration with empty model to fail validation");
        Console.WriteLine("✓ Empty model rejected");

        // Test 7: Null OpenAI settings
        var nullSettings = new LlmConfiguration
        {
            Provider = "OpenAI",
            OpenAI = null
        };
        result = await service.ValidateConfigurationAsync(nullSettings);
        if (result)
            throw new Exception("Expected configuration with null OpenAI settings to fail validation");
        Console.WriteLine("✓ Null OpenAI settings rejected");

        Console.WriteLine("\n✓ All OpenAI validation tests passed!");
    }

    private static async Task TestValidateOllamaConfiguration()
    {
        Console.WriteLine("\n=== Testing Ollama Configuration Validation ===");
        
        var service = CreateConfigurationService();

        // Test 1: Valid Ollama configuration
        var validConfig = new LlmConfiguration
        {
            Provider = "Ollama",
            Ollama = new OllamaSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = "llama2"
            }
        };
        var result = await service.ValidateConfigurationAsync(validConfig);
        if (!result)
            throw new Exception("Expected valid Ollama configuration to pass validation");
        Console.WriteLine("✓ Valid Ollama configuration passed");

        // Test 2: Missing base URL
        var missingBaseUrl = new LlmConfiguration
        {
            Provider = "Ollama",
            Ollama = new OllamaSettings
            {
                BaseUrl = "",
                Model = "llama2"
            }
        };
        result = await service.ValidateConfigurationAsync(missingBaseUrl);
        if (result)
            throw new Exception("Expected configuration with empty base URL to fail validation");
        Console.WriteLine("✓ Empty base URL rejected");

        // Test 3: Invalid base URL format
        var invalidBaseUrl = new LlmConfiguration
        {
            Provider = "Ollama",
            Ollama = new OllamaSettings
            {
                BaseUrl = "invalid-url",
                Model = "llama2"
            }
        };
        result = await service.ValidateConfigurationAsync(invalidBaseUrl);
        if (result)
            throw new Exception("Expected configuration with invalid base URL to fail validation");
        Console.WriteLine("✓ Invalid base URL format rejected");

        // Test 4: Missing model
        var missingModel = new LlmConfiguration
        {
            Provider = "Ollama",
            Ollama = new OllamaSettings
            {
                BaseUrl = "http://localhost:11434",
                Model = ""
            }
        };
        result = await service.ValidateConfigurationAsync(missingModel);
        if (result)
            throw new Exception("Expected configuration with empty model to fail validation");
        Console.WriteLine("✓ Empty model rejected");

        // Test 5: Null Ollama settings
        var nullSettings = new LlmConfiguration
        {
            Provider = "Ollama",
            Ollama = null
        };
        result = await service.ValidateConfigurationAsync(nullSettings);
        if (result)
            throw new Exception("Expected configuration with null Ollama settings to fail validation");
        Console.WriteLine("✓ Null Ollama settings rejected");

        Console.WriteLine("\n✓ All Ollama validation tests passed!");
    }

    private static async Task TestUrlFormatValidation()
    {
        Console.WriteLine("\n=== Testing URL Format Validation ===");
        
        var service = CreateConfigurationService();

        // Test various URL formats
        var testCases = new[]
        {
            ("https://api.openai.com", true, "HTTPS URL"),
            ("http://localhost:11434", true, "HTTP localhost"),
            ("http://192.168.1.100:8080", true, "HTTP with IP and port"),
            ("https://api.deepseek.com/v1", true, "HTTPS with path"),
            ("not-a-url", false, "Invalid URL"),
            ("htp://missing-t", false, "Invalid scheme"),
            ("://no-scheme", false, "Missing scheme"),
            ("http://", false, "Incomplete URL"),
            ("ftp://ftp.example.com", false, "FTP scheme (not HTTP/HTTPS)"),
            ("", false, "Empty string"),
            ("   ", false, "Whitespace only")
        };

        foreach (var (url, shouldPass, description) in testCases)
        {
            var config = new LlmConfiguration
            {
                Provider = "OpenAI",
                OpenAI = new ProviderSettings
                {
                    ApiKey = "test-key",
                    Model = "test-model",
                    BaseUrl = url
                }
            };

            var result = await service.ValidateConfigurationAsync(config);
            if (result != shouldPass)
            {
                throw new Exception($"URL validation failed for '{description}': expected {shouldPass}, got {result}");
            }
            Console.WriteLine($"✓ {description}: {(shouldPass ? "accepted" : "rejected")} as expected");
        }

        Console.WriteLine("\n✓ All URL format validation tests passed!");
    }

    private static async Task TestGeneralValidation()
    {
        Console.WriteLine("\n=== Testing General Validation ===");
        
        var service = CreateConfigurationService();

        // Test 1: Null configuration
        var result = await service.ValidateConfigurationAsync(null!);
        if (result)
            throw new Exception("Expected null configuration to fail validation");
        Console.WriteLine("✓ Null configuration rejected");

        // Test 2: Empty provider
        var emptyProvider = new LlmConfiguration
        {
            Provider = "",
            OpenAI = new ProviderSettings
            {
                ApiKey = "test-key",
                Model = "test-model",
                BaseUrl = "https://api.example.com"
            }
        };
        result = await service.ValidateConfigurationAsync(emptyProvider);
        if (result)
            throw new Exception("Expected configuration with empty provider to fail validation");
        Console.WriteLine("✓ Empty provider rejected");

        // Test 3: Unknown provider
        var unknownProvider = new LlmConfiguration
        {
            Provider = "UnknownProvider",
            OpenAI = new ProviderSettings
            {
                ApiKey = "test-key",
                Model = "test-model",
                BaseUrl = "https://api.example.com"
            }
        };
        result = await service.ValidateConfigurationAsync(unknownProvider);
        if (result)
            throw new Exception("Expected configuration with unknown provider to fail validation");
        Console.WriteLine("✓ Unknown provider rejected");

        // Test 4: Case-insensitive provider matching
        var lowerCaseProvider = new LlmConfiguration
        {
            Provider = "openai",
            OpenAI = new ProviderSettings
            {
                ApiKey = "test-key",
                Model = "test-model",
                BaseUrl = "https://api.example.com"
            }
        };
        result = await service.ValidateConfigurationAsync(lowerCaseProvider);
        if (!result)
            throw new Exception("Expected case-insensitive provider matching to work");
        Console.WriteLine("✓ Case-insensitive provider matching works");

        Console.WriteLine("\n✓ All general validation tests passed!");
    }

    private static ConfigurationService CreateConfigurationService()
    {
        var configuration = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var logger = loggerFactory.CreateLogger<ConfigurationService>();
        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };
        return new ConfigurationService(configuration, logger, environment);
    }
}

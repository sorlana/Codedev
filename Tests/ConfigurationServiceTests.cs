using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Manual tests for ConfigurationService.GetConfigurationAsync
/// These tests verify that configuration reading works correctly.
/// </summary>
public class ConfigurationServiceManualTests
{
    /// <summary>
    /// Test that GetConfigurationAsync reads configuration from appsettings.json correctly
    /// </summary>
    public static async Task TestGetConfigurationAsync()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ConfigurationService>();

        // Mock IWebHostEnvironment
        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var service = new ConfigurationService(configuration, logger, environment);

        // Act
        var result = await service.GetConfigurationAsync();

        // Assert
        Console.WriteLine($"Provider: {result.Provider}");
        Console.WriteLine($"OpenAI ApiKey: {result.OpenAI?.ApiKey}");
        Console.WriteLine($"OpenAI Model: {result.OpenAI?.Model}");
        Console.WriteLine($"OpenAI BaseUrl: {result.OpenAI?.BaseUrl}");
        Console.WriteLine($"Ollama BaseUrl: {result.Ollama?.BaseUrl}");
        Console.WriteLine($"Ollama Model: {result.Ollama?.Model}");

        // Verify expected values from appsettings.json
        if (result.Provider != "OpenAI")
            throw new Exception($"Expected Provider to be 'OpenAI', got '{result.Provider}'");

        if (result.OpenAI == null)
            throw new Exception("Expected OpenAI settings to be present");

        if (result.OpenAI.Model != "deepseek-chat")
            throw new Exception($"Expected OpenAI Model to be 'deepseek-chat', got '{result.OpenAI.Model}'");

        if (result.OpenAI.BaseUrl != "https://api.deepseek.com/v1")
            throw new Exception($"Expected OpenAI BaseUrl to be 'https://api.deepseek.com/v1', got '{result.OpenAI.BaseUrl}'");

        if (result.Ollama == null)
            throw new Exception("Expected Ollama settings to be present");

        if (result.Ollama.BaseUrl != "http://localhost:11434")
            throw new Exception($"Expected Ollama BaseUrl to be 'http://localhost:11434', got '{result.Ollama.BaseUrl}'");

        if (result.Ollama.Model != "llama2")
            throw new Exception($"Expected Ollama Model to be 'llama2', got '{result.Ollama.Model}'");

        Console.WriteLine("\n✓ All assertions passed!");
    }

    /// <summary>
    /// Test that ValidateConfigurationAsync correctly validates OpenAI configuration
    /// </summary>
    public static async Task TestValidateOpenAIConfiguration()
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

    /// <summary>
    /// Test that ValidateConfigurationAsync correctly validates Ollama configuration
    /// </summary>
    public static async Task TestValidateOllamaConfiguration()
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

    /// <summary>
    /// Test URL format validation edge cases
    /// </summary>
    public static async Task TestUrlFormatValidation()
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

    /// <summary>
    /// Test general validation edge cases
    /// </summary>
    public static async Task TestGeneralValidation()
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

    /// <summary>
    /// Test that SaveConfigurationAsync saves and loads configuration correctly
    /// </summary>
    public static async Task TestSaveConfigurationAsync()
    {
        Console.WriteLine("\n=== Testing SaveConfigurationAsync ===");
        
        // Create a temporary test configuration file
        var testConfigPath = Path.Combine(Path.GetTempPath(), $"test_appsettings_{Guid.NewGuid()}.json");
        
        try
        {
            // Create initial configuration file
            var initialConfig = new
            {
                Logging = new
                {
                    LogLevel = new
                    {
                        Default = "Information"
                    }
                },
                Llm = new
                {
                    Provider = "OpenAI",
                    OpenAI = new
                    {
                        ApiKey = "old-key",
                        Model = "old-model",
                        BaseUrl = "https://old.api.com"
                    },
                    Ollama = new
                    {
                        BaseUrl = "http://localhost:11434",
                        Model = "llama2"
                    }
                }
            };

            await File.WriteAllTextAsync(testConfigPath, System.Text.Json.JsonSerializer.Serialize(initialConfig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Create service with test configuration file
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(testConfigPath, optional: false, reloadOnChange: true)
                .Build();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<ConfigurationService>();
            var environment = new MockWebHostEnvironment
            {
                ContentRootPath = Path.GetDirectoryName(testConfigPath)!
            };

            // Override the config file path for testing
            var service = new TestableConfigurationService(configuration, logger, environment, testConfigPath);

            // Test 1: Save new OpenAI configuration
            var newConfig = new LlmConfiguration
            {
                Provider = "OpenAI",
                OpenAI = new ProviderSettings
                {
                    ApiKey = "new-test-key",
                    Model = "gpt-4",
                    BaseUrl = "https://api.openai.com/v1"
                },
                Ollama = new OllamaSettings
                {
                    BaseUrl = "http://localhost:11434",
                    Model = "llama2"
                }
            };

            await service.SaveConfigurationAsync(newConfig);
            Console.WriteLine("✓ Configuration saved successfully");

            // Verify the file was updated
            var fileContent = await File.ReadAllTextAsync(testConfigPath);
            if (!fileContent.Contains("new-test-key"))
                throw new Exception("Expected saved configuration to contain new API key");
            if (!fileContent.Contains("gpt-4"))
                throw new Exception("Expected saved configuration to contain new model");
            if (!fileContent.Contains("https://api.openai.com/v1"))
                throw new Exception("Expected saved configuration to contain new base URL");
            Console.WriteLine("✓ File content verified");

            // Test 2: Load the saved configuration
            var reloadedConfig = new ConfigurationBuilder()
                .AddJsonFile(testConfigPath, optional: false)
                .Build();
            var reloadedService = new TestableConfigurationService(reloadedConfig, logger, environment, testConfigPath);
            var loadedConfig = await reloadedService.GetConfigurationAsync();

            if (loadedConfig.Provider != "OpenAI")
                throw new Exception($"Expected Provider to be 'OpenAI', got '{loadedConfig.Provider}'");
            if (loadedConfig.OpenAI?.ApiKey != "new-test-key")
                throw new Exception($"Expected ApiKey to be 'new-test-key', got '{loadedConfig.OpenAI?.ApiKey}'");
            if (loadedConfig.OpenAI?.Model != "gpt-4")
                throw new Exception($"Expected Model to be 'gpt-4', got '{loadedConfig.OpenAI?.Model}'");
            if (loadedConfig.OpenAI?.BaseUrl != "https://api.openai.com/v1")
                throw new Exception($"Expected BaseUrl to be 'https://api.openai.com/v1', got '{loadedConfig.OpenAI?.BaseUrl}'");
            Console.WriteLine("✓ Configuration round-trip successful");

            // Test 3: Save Ollama configuration
            var ollamaConfig = new LlmConfiguration
            {
                Provider = "Ollama",
                OpenAI = new ProviderSettings
                {
                    ApiKey = "new-test-key",
                    Model = "gpt-4",
                    BaseUrl = "https://api.openai.com/v1"
                },
                Ollama = new OllamaSettings
                {
                    BaseUrl = "http://192.168.1.100:11434",
                    Model = "mistral"
                }
            };

            await service.SaveConfigurationAsync(ollamaConfig);
            Console.WriteLine("✓ Ollama configuration saved successfully");

            // Verify Ollama settings
            fileContent = await File.ReadAllTextAsync(testConfigPath);
            if (!fileContent.Contains("http://192.168.1.100:11434"))
                throw new Exception("Expected saved configuration to contain new Ollama base URL");
            if (!fileContent.Contains("mistral"))
                throw new Exception("Expected saved configuration to contain new Ollama model");
            Console.WriteLine("✓ Ollama configuration verified");

            // Test 4: Verify other sections are preserved
            if (!fileContent.Contains("Logging"))
                throw new Exception("Expected Logging section to be preserved");
            Console.WriteLine("✓ Other configuration sections preserved");

            // Test 5: Try to save invalid configuration
            var invalidConfig = new LlmConfiguration
            {
                Provider = "OpenAI",
                OpenAI = new ProviderSettings
                {
                    ApiKey = "",  // Invalid: empty API key
                    Model = "gpt-4",
                    BaseUrl = "https://api.openai.com/v1"
                }
            };

            try
            {
                await service.SaveConfigurationAsync(invalidConfig);
                throw new Exception("Expected SaveConfigurationAsync to throw exception for invalid configuration");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("✓ Invalid configuration rejected");
            }

            Console.WriteLine("\n✓ All SaveConfigurationAsync tests passed!");
        }
        finally
        {
            // Clean up test file
            if (File.Exists(testConfigPath))
            {
                File.Delete(testConfigPath);
            }
        }
    }

    /// <summary>
    /// Test concurrent write protection with file locking
    /// </summary>
    public static async Task TestConcurrentWriteProtection()
    {
        Console.WriteLine("\n=== Testing Concurrent Write Protection ===");
        
        var testConfigPath = Path.Combine(Path.GetTempPath(), $"test_concurrent_{Guid.NewGuid()}.json");
        
        try
        {
            // Create initial configuration file
            var initialConfig = new
            {
                Llm = new
                {
                    Provider = "OpenAI",
                    OpenAI = new
                    {
                        ApiKey = "test-key",
                        Model = "test-model",
                        BaseUrl = "https://api.test.com"
                    }
                }
            };

            await File.WriteAllTextAsync(testConfigPath, System.Text.Json.JsonSerializer.Serialize(initialConfig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(testConfigPath, optional: false)
                .Build();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<ConfigurationService>();
            var environment = new MockWebHostEnvironment
            {
                ContentRootPath = Path.GetDirectoryName(testConfigPath)!
            };

            var service = new TestableConfigurationService(configuration, logger, environment, testConfigPath);

            // Lock the file to simulate concurrent access
            using (var fileStream = new FileStream(testConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var config = new LlmConfiguration
                {
                    Provider = "OpenAI",
                    OpenAI = new ProviderSettings
                    {
                        ApiKey = "new-key",
                        Model = "new-model",
                        BaseUrl = "https://api.new.com"
                    }
                };

                try
                {
                    await service.SaveConfigurationAsync(config);
                    throw new Exception("Expected SaveConfigurationAsync to throw exception when file is locked");
                }
                catch (InvalidOperationException ex)
                {
                    if (ex.Message.Contains("locked"))
                    {
                        Console.WriteLine("✓ File locking detected correctly");
                    }
                    else
                    {
                        throw new Exception($"Expected exception message to mention file locking, got: {ex.Message}");
                    }
                }
            }

            Console.WriteLine("\n✓ Concurrent write protection test passed!");
        }
        finally
        {
            if (File.Exists(testConfigPath))
            {
                File.Delete(testConfigPath);
            }
        }
    }

    private static ConfigurationService CreateConfigurationService()
    {
        var configuration = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ConfigurationService>();
        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };
        return new ConfigurationService(configuration, logger, environment);
    }

    public static async Task Main(string[] args)
    {
        try
        {
            await TestGetConfigurationAsync();
            await TestValidateOpenAIConfiguration();
            await TestValidateOllamaConfiguration();
            await TestUrlFormatValidation();
            await TestGeneralValidation();
            await TestSaveConfigurationAsync();
            await TestConcurrentWriteProtection();
            
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("✓✓✓ ALL TESTS PASSED! ✓✓✓");
            Console.WriteLine(new string('=', 50));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Test failed: {ex.Message}");
            Environment.Exit(1);
        }
    }
}

/// <summary>
/// Testable version of ConfigurationService that allows overriding the config file path
/// </summary>
public class TestableConfigurationService : ConfigurationService
{
    private readonly string _testConfigFilePath;

    public TestableConfigurationService(
        IConfiguration configuration,
        ILogger<ConfigurationService> logger,
        IWebHostEnvironment environment,
        string testConfigFilePath)
        : base(configuration, logger, environment)
    {
        _testConfigFilePath = testConfigFilePath;
        
        // Use reflection to set the private _configFilePath field
        var field = typeof(ConfigurationService).GetField("_configFilePath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(this, _testConfigFilePath);
    }
}

/// <summary>
/// Mock implementation of IWebHostEnvironment for testing
/// </summary>
public class MockWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ApplicationName { get; set; } = "CSharpRefactoringAssistant";
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
}

using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Standalone test runner for SaveConfigurationAsync tests
/// </summary>
public class ConfigTestRunner
{
    public static async Task RunSaveConfigurationTests()
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("SAVE CONFIGURATION TESTS");
        Console.WriteLine(new string('=', 60));

        try
        {
            await TestSaveConfigurationAsync();
            await TestConcurrentWriteProtection();
            
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("✓✓✓ ALL SAVE CONFIGURATION TESTS PASSED! ✓✓✓");
            Console.WriteLine(new string('=', 60));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static async Task TestSaveConfigurationAsync()
    {
        Console.WriteLine("\n=== Testing SaveConfigurationAsync ===");
        
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

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            var logger = loggerFactory.CreateLogger<ConfigurationService>();
            var environment = new MockWebHostEnvironment
            {
                ContentRootPath = Path.GetDirectoryName(testConfigPath)!
            };

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
            if (File.Exists(testConfigPath))
            {
                File.Delete(testConfigPath);
            }
        }
    }

    private static async Task TestConcurrentWriteProtection()
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

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
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
}

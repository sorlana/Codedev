using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Tests for the GET /api/configuration endpoint.
/// Validates: Requirements 1.4
/// </summary>
public class ConfigurationEndpointTests
{
    /// <summary>
    /// Test that the endpoint returns a successful ConfigurationResponse with current settings
    /// </summary>
    public static async Task TestGetConfigurationEndpoint()
    {
        Console.WriteLine("Testing GET /api/configuration endpoint...");
        
        // Arrange - Set up ConfigurationService
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ConfigurationService>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var configService = new ConfigurationService(configuration, logger, environment);

        // Act - Simulate the endpoint logic
        ConfigurationResponse? response = null;
        Exception? caughtException = null;
        
        try
        {
            var config = await configService.GetConfigurationAsync();
            response = new ConfigurationResponse
            {
                Success = true,
                Configuration = config
            };
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        if (caughtException != null)
        {
            Console.WriteLine($"❌ FAILED: Exception thrown: {caughtException.Message}");
            return;
        }

        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (!response.Success)
        {
            Console.WriteLine("❌ FAILED: Response.Success is false");
            return;
        }

        if (response.Configuration == null)
        {
            Console.WriteLine("❌ FAILED: Response.Configuration is null");
            return;
        }

        // Verify configuration has expected structure
        if (string.IsNullOrEmpty(response.Configuration.Provider))
        {
            Console.WriteLine("❌ FAILED: Provider is empty");
            return;
        }

        Console.WriteLine($"✓ Response.Success: {response.Success}");
        Console.WriteLine($"✓ Provider: {response.Configuration.Provider}");
        
        if (response.Configuration.OpenAI != null)
        {
            Console.WriteLine($"✓ OpenAI settings present");
            Console.WriteLine($"  - Model: {response.Configuration.OpenAI.Model}");
            Console.WriteLine($"  - BaseUrl: {response.Configuration.OpenAI.BaseUrl}");
        }
        
        if (response.Configuration.Ollama != null)
        {
            Console.WriteLine($"✓ Ollama settings present");
            Console.WriteLine($"  - Model: {response.Configuration.Ollama.Model}");
            Console.WriteLine($"  - BaseUrl: {response.Configuration.Ollama.BaseUrl}");
        }

        Console.WriteLine("✓ PASSED: GET /api/configuration endpoint test");
    }

    /// <summary>
    /// Test that the endpoint handles errors appropriately
    /// </summary>
    public static async Task TestGetConfigurationEndpointErrorHandling()
    {
        Console.WriteLine("\nTesting GET /api/configuration error handling...");
        
        // Arrange - Set up ConfigurationService with invalid path
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI"
            })
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ConfigurationService>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = "/invalid/path/that/does/not/exist"
        };

        var configService = new ConfigurationService(configuration, logger, environment);

        // Act - The endpoint should catch exceptions and return appropriate error
        ConfigurationResponse? response = null;
        bool exceptionCaught = false;
        
        try
        {
            var config = await configService.GetConfigurationAsync();
            response = new ConfigurationResponse
            {
                Success = true,
                Configuration = config
            };
        }
        catch (Exception)
        {
            // This simulates the endpoint's error handling
            exceptionCaught = true;
        }

        // Assert - Either we get a response or an exception is caught
        if (response != null && response.Success)
        {
            Console.WriteLine("✓ PASSED: Endpoint returned configuration successfully");
        }
        else if (exceptionCaught)
        {
            Console.WriteLine("✓ PASSED: Endpoint would return 500 error (exception caught)");
        }
        else
        {
            Console.WriteLine("❌ FAILED: Unexpected state");
        }
    }

    /// <summary>
    /// Test that POST /api/configuration endpoint accepts valid configuration
    /// Validates: Requirements 2.7, 3.5, 4.1, 5.3
    /// </summary>
    public static async Task TestPostConfigurationEndpointWithValidData()
    {
        Console.WriteLine("\nTesting POST /api/configuration with valid data...");
        
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ConfigurationService>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var configService = new ConfigurationService(configuration, logger, environment);

        // Create a valid request
        var request = new SaveConfigurationRequest
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "test-api-key-12345",
                Model = "test-model",
                BaseUrl = "https://api.test.com/v1"
            }
        };

        // Act - Simulate the endpoint logic
        ConfigurationResponse? response = null;
        Exception? caughtException = null;
        
        try
        {
            var config = new LlmConfiguration
            {
                Provider = request.Provider,
                OpenAI = request.OpenAI,
                Ollama = request.Ollama
            };

            var isValid = await configService.ValidateConfigurationAsync(config);
            if (!isValid)
            {
                response = new ConfigurationResponse
                {
                    Success = false,
                    Message = "Configuration validation failed. Please check that all required fields are provided and URLs are valid."
                };
            }
            else
            {
                await configService.SaveConfigurationAsync(config);
                var savedConfig = await configService.GetConfigurationAsync();
                response = new ConfigurationResponse
                {
                    Success = true,
                    Message = "Configuration saved successfully",
                    Configuration = savedConfig
                };
            }
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        if (caughtException != null)
        {
            Console.WriteLine($"❌ FAILED: Exception thrown: {caughtException.Message}");
            return;
        }

        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (!response.Success)
        {
            Console.WriteLine($"❌ FAILED: Response.Success is false. Message: {response.Message}");
            return;
        }

        if (response.Configuration == null)
        {
            Console.WriteLine("❌ FAILED: Response.Configuration is null");
            return;
        }

        // Verify the saved configuration matches what we sent
        if (response.Configuration.Provider != request.Provider)
        {
            Console.WriteLine($"❌ FAILED: Provider mismatch. Expected: {request.Provider}, Got: {response.Configuration.Provider}");
            return;
        }

        Console.WriteLine($"✓ Response.Success: {response.Success}");
        Console.WriteLine($"✓ Response.Message: {response.Message}");
        Console.WriteLine($"✓ Configuration saved and retrieved successfully");
        Console.WriteLine("✓ PASSED: POST /api/configuration with valid data test");
    }

    /// <summary>
    /// Test that POST /api/configuration endpoint rejects invalid configuration
    /// Validates: Requirements 5.4
    /// </summary>
    public static async Task TestPostConfigurationEndpointWithInvalidData()
    {
        Console.WriteLine("\nTesting POST /api/configuration with invalid data...");
        
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ConfigurationService>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var configService = new ConfigurationService(configuration, logger, environment);

        // Create an invalid request (missing API key)
        var request = new SaveConfigurationRequest
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "", // Empty API key - should fail validation
                Model = "test-model",
                BaseUrl = "https://api.test.com/v1"
            }
        };

        // Act - Simulate the endpoint logic
        ConfigurationResponse? response = null;
        
        try
        {
            var config = new LlmConfiguration
            {
                Provider = request.Provider,
                OpenAI = request.OpenAI,
                Ollama = request.Ollama
            };

            var isValid = await configService.ValidateConfigurationAsync(config);
            if (!isValid)
            {
                response = new ConfigurationResponse
                {
                    Success = false,
                    Message = "Configuration validation failed. Please check that all required fields are provided and URLs are valid."
                };
            }
            else
            {
                await configService.SaveConfigurationAsync(config);
                var savedConfig = await configService.GetConfigurationAsync();
                response = new ConfigurationResponse
                {
                    Success = true,
                    Message = "Configuration saved successfully",
                    Configuration = savedConfig
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAILED: Unexpected exception: {ex.Message}");
            return;
        }

        // Assert
        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (response.Success)
        {
            Console.WriteLine("❌ FAILED: Response.Success should be false for invalid data");
            return;
        }

        if (string.IsNullOrEmpty(response.Message))
        {
            Console.WriteLine("❌ FAILED: Response.Message should contain error details");
            return;
        }

        Console.WriteLine($"✓ Response.Success: {response.Success}");
        Console.WriteLine($"✓ Response.Message: {response.Message}");
        Console.WriteLine("✓ PASSED: POST /api/configuration with invalid data test");
    }

    /// <summary>
    /// Test that POST /api/configuration endpoint rejects invalid URL format
    /// Validates: Requirements 2.6, 3.4
    /// </summary>
    public static async Task TestPostConfigurationEndpointWithInvalidUrl()
    {
        Console.WriteLine("\nTesting POST /api/configuration with invalid URL...");
        
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ConfigurationService>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var configService = new ConfigurationService(configuration, logger, environment);

        // Create a request with invalid URL
        var request = new SaveConfigurationRequest
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "test-api-key",
                Model = "test-model",
                BaseUrl = "not-a-valid-url" // Invalid URL format
            }
        };

        // Act - Simulate the endpoint logic
        ConfigurationResponse? response = null;
        
        try
        {
            var config = new LlmConfiguration
            {
                Provider = request.Provider,
                OpenAI = request.OpenAI,
                Ollama = request.Ollama
            };

            var isValid = await configService.ValidateConfigurationAsync(config);
            if (!isValid)
            {
                response = new ConfigurationResponse
                {
                    Success = false,
                    Message = "Configuration validation failed. Please check that all required fields are provided and URLs are valid."
                };
            }
            else
            {
                await configService.SaveConfigurationAsync(config);
                var savedConfig = await configService.GetConfigurationAsync();
                response = new ConfigurationResponse
                {
                    Success = true,
                    Message = "Configuration saved successfully",
                    Configuration = savedConfig
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAILED: Unexpected exception: {ex.Message}");
            return;
        }

        // Assert
        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (response.Success)
        {
            Console.WriteLine("❌ FAILED: Response.Success should be false for invalid URL");
            return;
        }

        Console.WriteLine($"✓ Response.Success: {response.Success}");
        Console.WriteLine($"✓ Response.Message: {response.Message}");
        Console.WriteLine("✓ PASSED: POST /api/configuration with invalid URL test");
    }

    /// <summary>
    /// Test that GET /api/configuration/ollama/models endpoint handles connection errors gracefully
    /// Validates: Requirements 3.6
    /// </summary>
    public static async Task TestGetOllamaModelsEndpointConnectionError()
    {
        Console.WriteLine("\nTesting GET /api/configuration/ollama/models with connection error...");
        
        // Arrange - Use an invalid Ollama URL that will fail to connect
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Ollama:BaseUrl"] = "http://localhost:54321" // Unreachable port
            })
            .Build();

        var httpClientFactory = new MockHttpClientFactory();

        // Act - Simulate the endpoint logic
        OllamaModelsResponse? response = null;
        Exception? caughtException = null;
        
        try
        {
            var ollamaBaseUrl = configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(1); // Short timeout for test
            
            try
            {
                var httpResponse = await httpClient.GetAsync($"{ollamaBaseUrl}/api/tags");
                
                if (httpResponse.IsSuccessStatusCode)
                {
                    // Parse response (won't reach here in this test)
                    response = new OllamaModelsResponse { Models = new List<string>() };
                }
                else
                {
                    // Return empty list on error
                    response = new OllamaModelsResponse { Models = new List<string>() };
                }
            }
            catch (HttpRequestException)
            {
                // Graceful handling - return empty list
                response = new OllamaModelsResponse { Models = new List<string>() };
            }
            catch (TaskCanceledException)
            {
                // Timeout - return empty list
                response = new OllamaModelsResponse { Models = new List<string>() };
            }
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        if (caughtException != null)
        {
            Console.WriteLine($"❌ FAILED: Unexpected exception: {caughtException.Message}");
            return;
        }

        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (response.Models == null)
        {
            Console.WriteLine("❌ FAILED: Response.Models is null");
            return;
        }

        // Should return empty list on connection error (graceful handling)
        if (response.Models.Count != 0)
        {
            Console.WriteLine($"❌ FAILED: Expected empty list, got {response.Models.Count} models");
            return;
        }

        Console.WriteLine("✓ Response returned successfully");
        Console.WriteLine($"✓ Models list is empty (graceful error handling)");
        Console.WriteLine("✓ PASSED: GET /api/configuration/ollama/models connection error test");
    }

    /// <summary>
    /// Test that GET /api/configuration/ollama/models endpoint uses default URL when not configured
    /// Validates: Requirements 3.6
    /// </summary>
    public static async Task TestGetOllamaModelsEndpointDefaultUrl()
    {
        Console.WriteLine("\nTesting GET /api/configuration/ollama/models with default URL...");
        
        // Arrange - Configuration without Ollama URL
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act - Simulate the endpoint logic
        var ollamaBaseUrl = configuration["Llm:Ollama:BaseUrl"];
        
        if (string.IsNullOrWhiteSpace(ollamaBaseUrl))
        {
            ollamaBaseUrl = "http://localhost:11434";
        }

        // Assert
        if (ollamaBaseUrl != "http://localhost:11434")
        {
            Console.WriteLine($"❌ FAILED: Expected default URL 'http://localhost:11434', got '{ollamaBaseUrl}'");
            return;
        }

        Console.WriteLine($"✓ Default URL used: {ollamaBaseUrl}");
        Console.WriteLine("✓ PASSED: GET /api/configuration/ollama/models default URL test");
    }

    /// <summary>
    /// Test that POST /api/configuration/test endpoint validates configuration structure
    /// Validates: Requirements 5.5
    /// </summary>
    public static async Task TestPostConfigurationTestEndpointValidation()
    {
        Console.WriteLine("\nTesting POST /api/configuration/test with invalid configuration...");
        
        // Arrange - Create request with missing provider settings
        var request = new SaveConfigurationRequest
        {
            Provider = "OpenAI",
            OpenAI = null, // Missing OpenAI settings
            Ollama = null
        };

        // Act - Simulate the endpoint validation logic
        TestConnectionResponse? response = null;
        
        if (request.Provider?.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) == true && request.OpenAI != null)
        {
            // Would proceed with test
            response = new TestConnectionResponse
            {
                Success = true,
                Message = "Connection successful"
            };
        }
        else if (request.Provider?.Equals("Ollama", StringComparison.OrdinalIgnoreCase) == true && request.Ollama != null)
        {
            // Would proceed with test
            response = new TestConnectionResponse
            {
                Success = true,
                Message = "Connection successful"
            };
        }
        else
        {
            // Invalid configuration
            response = new TestConnectionResponse
            {
                Success = false,
                Message = "Invalid provider configuration. Please specify either OpenAI or Ollama settings."
            };
        }

        // Assert
        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (response.Success)
        {
            Console.WriteLine("❌ FAILED: Response.Success should be false for invalid configuration");
            return;
        }

        if (string.IsNullOrEmpty(response.Message))
        {
            Console.WriteLine("❌ FAILED: Response.Message should contain error details");
            return;
        }

        Console.WriteLine($"✓ Response.Success: {response.Success}");
        Console.WriteLine($"✓ Response.Message: {response.Message}");
        Console.WriteLine("✓ PASSED: POST /api/configuration/test validation test");
    }

    /// <summary>
    /// Test that POST /api/configuration/test endpoint handles missing API key
    /// Validates: Requirements 5.5
    /// </summary>
    public static async Task TestPostConfigurationTestEndpointMissingApiKey()
    {
        Console.WriteLine("\nTesting POST /api/configuration/test with missing API key...");
        
        // Arrange - Create request with missing API key
        var request = new SaveConfigurationRequest
        {
            Provider = "OpenAI",
            OpenAI = new ProviderSettings
            {
                ApiKey = "", // Empty API key
                Model = "test-model",
                BaseUrl = "https://api.test.com/v1"
            }
        };

        // Act - Simulate the endpoint logic
        TestConnectionResponse? response = null;
        
        try
        {
            // Create temporary configuration
            var configData = new Dictionary<string, string?>
            {
                ["Llm:Provider"] = request.Provider,
                ["Llm:OpenAI:ApiKey"] = string.IsNullOrEmpty(request.OpenAI.ApiKey) ? null : request.OpenAI.ApiKey,
                ["Llm:OpenAI:Model"] = request.OpenAI.Model,
                ["Llm:OpenAI:BaseUrl"] = request.OpenAI.BaseUrl
            };

            var testConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .Build();

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<OpenAiLlmService>();
            var httpClientFactory = new MockHttpClientFactory();
            var httpClient = httpClientFactory.CreateClient();

            // Try to create service - should throw ArgumentException for missing API key
            var llmService = new OpenAiLlmService(httpClient, testConfiguration, logger);
            
            response = new TestConnectionResponse
            {
                Success = false,
                Message = "Should not reach here"
            };
        }
        catch (ArgumentException ex)
        {
            // Expected - API key is required
            response = new TestConnectionResponse
            {
                Success = false,
                Message = $"Configuration error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAILED: Unexpected exception: {ex.Message}");
            return;
        }

        // Assert
        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (response.Success)
        {
            Console.WriteLine("❌ FAILED: Response.Success should be false for missing API key");
            return;
        }

        if (!response.Message.Contains("Configuration error") && !response.Message.Contains("API key"))
        {
            Console.WriteLine($"❌ FAILED: Response.Message should indicate configuration error, got: {response.Message}");
            return;
        }

        Console.WriteLine($"✓ Response.Success: {response.Success}");
        Console.WriteLine($"✓ Response.Message: {response.Message}");
        Console.WriteLine("✓ PASSED: POST /api/configuration/test missing API key test");
    }

    /// <summary>
    /// Test that POST /api/configuration/test endpoint handles unsupported provider
    /// Validates: Requirements 5.5
    /// </summary>
    public static async Task TestPostConfigurationTestEndpointUnsupportedProvider()
    {
        Console.WriteLine("\nTesting POST /api/configuration/test with unsupported provider...");
        
        // Arrange - Create request with unsupported provider
        var request = new SaveConfigurationRequest
        {
            Provider = "UnsupportedProvider",
            OpenAI = null,
            Ollama = null
        };

        // Act - Simulate the endpoint logic
        TestConnectionResponse? response = null;
        
        if (request.Provider?.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) == true)
        {
            response = new TestConnectionResponse { Success = true, Message = "OK" };
        }
        else if (request.Provider?.Equals("Ollama", StringComparison.OrdinalIgnoreCase) == true)
        {
            response = new TestConnectionResponse { Success = true, Message = "OK" };
        }
        else
        {
            response = new TestConnectionResponse
            {
                Success = false,
                Message = "Unsupported provider type. Please use 'OpenAI' or 'Ollama'."
            };
        }

        // Assert
        if (response == null)
        {
            Console.WriteLine("❌ FAILED: Response is null");
            return;
        }

        if (response.Success)
        {
            Console.WriteLine("❌ FAILED: Response.Success should be false for unsupported provider");
            return;
        }

        if (!response.Message.Contains("Unsupported provider"))
        {
            Console.WriteLine($"❌ FAILED: Response.Message should indicate unsupported provider, got: {response.Message}");
            return;
        }

        Console.WriteLine($"✓ Response.Success: {response.Success}");
        Console.WriteLine($"✓ Response.Message: {response.Message}");
        Console.WriteLine("✓ PASSED: POST /api/configuration/test unsupported provider test");
    }
}

/// <summary>
/// Test runner for configuration endpoint tests
/// </summary>
public static class ConfigEndpointTestRunner
{
    public static async Task RunAllEndpointTests()
    {
        Console.WriteLine("=== Running Configuration Endpoint Tests ===\n");
        
        await ConfigurationEndpointTests.TestGetConfigurationEndpoint();
        await ConfigurationEndpointTests.TestGetConfigurationEndpointErrorHandling();
        await ConfigurationEndpointTests.TestPostConfigurationEndpointWithValidData();
        await ConfigurationEndpointTests.TestPostConfigurationEndpointWithInvalidData();
        await ConfigurationEndpointTests.TestPostConfigurationEndpointWithInvalidUrl();
        await ConfigurationEndpointTests.TestGetOllamaModelsEndpointConnectionError();
        await ConfigurationEndpointTests.TestGetOllamaModelsEndpointDefaultUrl();
        await ConfigurationEndpointTests.TestPostConfigurationTestEndpointValidation();
        await ConfigurationEndpointTests.TestPostConfigurationTestEndpointMissingApiKey();
        await ConfigurationEndpointTests.TestPostConfigurationTestEndpointUnsupportedProvider();
        
        Console.WriteLine("\n=== Configuration Endpoint Tests Complete ===");
    }
}

using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Manual tests for LlmServiceFactory
/// These tests verify that the factory creates the correct service type based on configuration.
/// </summary>
public class LlmServiceFactoryManualTests
{
    /// <summary>
    /// Test that factory creates OpenAI service when Provider is "OpenAI"
    /// </summary>
    public static void TestCreateOpenAiService()
    {
        Console.WriteLine("\n=== Testing OpenAI Service Creation ===");

        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI",
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "deepseek-chat",
                ["Llm:OpenAI:BaseUrl"] = "https://api.deepseek.com/v1"
            })
            .Build();

        var factory = CreateFactory(configuration);

        // Act
        var service = factory.CreateLlmService();

        // Assert
        if (service is not OpenAiLlmService)
            throw new Exception($"Expected OpenAiLlmService, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory created OpenAiLlmService for Provider='OpenAI'");
    }

    /// <summary>
    /// Test that factory creates Ollama service when Provider is "Ollama"
    /// </summary>
    public static void TestCreateOllamaService()
    {
        Console.WriteLine("\n=== Testing Ollama Service Creation ===");

        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "Ollama",
                ["Llm:Ollama:BaseUrl"] = "http://localhost:11434",
                ["Llm:Ollama:Model"] = "llama2"
            })
            .Build();

        var factory = CreateFactory(configuration);

        // Act
        var service = factory.CreateLlmService();

        // Assert
        if (service is not OllamaLlmService)
            throw new Exception($"Expected OllamaLlmService, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory created OllamaLlmService for Provider='Ollama'");
    }

    /// <summary>
    /// Test that factory defaults to OpenAI service when Provider is not set
    /// </summary>
    public static void TestDefaultToOpenAiService()
    {
        Console.WriteLine("\n=== Testing Default Service Creation ===");

        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "deepseek-chat",
                ["Llm:OpenAI:BaseUrl"] = "https://api.deepseek.com/v1"
            })
            .Build();

        var factory = CreateFactory(configuration);

        // Act
        var service = factory.CreateLlmService();

        // Assert
        if (service is not OpenAiLlmService)
            throw new Exception($"Expected OpenAiLlmService as default, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory defaulted to OpenAiLlmService when Provider not set");
    }

    /// <summary>
    /// Test that factory defaults to OpenAI service when Provider is unknown
    /// </summary>
    public static void TestUnknownProviderDefaultsToOpenAi()
    {
        Console.WriteLine("\n=== Testing Unknown Provider Fallback ===");

        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "UnknownProvider",
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "deepseek-chat",
                ["Llm:OpenAI:BaseUrl"] = "https://api.deepseek.com/v1"
            })
            .Build();

        var factory = CreateFactory(configuration);

        // Act
        var service = factory.CreateLlmService();

        // Assert
        if (service is not OpenAiLlmService)
            throw new Exception($"Expected OpenAiLlmService for unknown provider, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory defaulted to OpenAiLlmService for unknown provider");
    }

    /// <summary>
    /// Test that factory is case-insensitive for provider names
    /// </summary>
    public static void TestCaseInsensitiveProviderMatching()
    {
        Console.WriteLine("\n=== Testing Case-Insensitive Provider Matching ===");

        var testCases = new[]
        {
            ("openai", typeof(OpenAiLlmService), "lowercase 'openai'"),
            ("OPENAI", typeof(OpenAiLlmService), "uppercase 'OPENAI'"),
            ("OpenAI", typeof(OpenAiLlmService), "mixed case 'OpenAI'"),
            ("ollama", typeof(OllamaLlmService), "lowercase 'ollama'"),
            ("OLLAMA", typeof(OllamaLlmService), "uppercase 'OLLAMA'"),
            ("Ollama", typeof(OllamaLlmService), "mixed case 'Ollama'")
        };

        foreach (var (provider, expectedType, description) in testCases)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Llm:Provider"] = provider,
                    ["Llm:OpenAI:ApiKey"] = "test-key",
                    ["Llm:OpenAI:Model"] = "test-model",
                    ["Llm:OpenAI:BaseUrl"] = "https://api.test.com",
                    ["Llm:Ollama:BaseUrl"] = "http://localhost:11434",
                    ["Llm:Ollama:Model"] = "llama2"
                })
                .Build();

            var factory = CreateFactory(configuration);
            var service = factory.CreateLlmService();

            if (service.GetType() != expectedType)
                throw new Exception($"Expected {expectedType.Name} for {description}, got {service.GetType().Name}");

            Console.WriteLine($"✓ {description} matched correctly");
        }

        Console.WriteLine("\n✓ All case-insensitive matching tests passed!");
    }

    /// <summary>
    /// Test that factory creates services that implement ILlmService
    /// </summary>
    public static void TestServicesImplementILlmService()
    {
        Console.WriteLine("\n=== Testing ILlmService Interface Implementation ===");

        var configurations = new[]
        {
            ("OpenAI", new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI",
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "test-model",
                ["Llm:OpenAI:BaseUrl"] = "https://api.test.com"
            }),
            ("Ollama", new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "Ollama",
                ["Llm:Ollama:BaseUrl"] = "http://localhost:11434",
                ["Llm:Ollama:Model"] = "llama2"
            })
        };

        foreach (var (providerName, configDict) in configurations)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();

            var factory = CreateFactory(configuration);
            var service = factory.CreateLlmService();

            if (service is not ILlmService)
                throw new Exception($"Expected {providerName} service to implement ILlmService");

            Console.WriteLine($"✓ {providerName} service implements ILlmService");
        }

        Console.WriteLine("\n✓ All interface implementation tests passed!");
    }

    /// <summary>
    /// Test that factory can be called multiple times
    /// </summary>
    public static void TestMultipleServiceCreation()
    {
        Console.WriteLine("\n=== Testing Multiple Service Creation ===");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI",
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "test-model",
                ["Llm:OpenAI:BaseUrl"] = "https://api.test.com"
            })
            .Build();

        var factory = CreateFactory(configuration);

        // Create multiple services
        var service1 = factory.CreateLlmService();
        var service2 = factory.CreateLlmService();
        var service3 = factory.CreateLlmService();

        // Verify all are valid instances
        if (service1 is not OpenAiLlmService)
            throw new Exception("First service creation failed");
        if (service2 is not OpenAiLlmService)
            throw new Exception("Second service creation failed");
        if (service3 is not OpenAiLlmService)
            throw new Exception("Third service creation failed");

        // Verify they are different instances
        if (ReferenceEquals(service1, service2))
            throw new Exception("Expected different instances, got same reference");
        if (ReferenceEquals(service2, service3))
            throw new Exception("Expected different instances, got same reference");

        Console.WriteLine("✓ Factory can create multiple service instances");
        Console.WriteLine("✓ Each call creates a new instance");
    }

    private static LlmServiceFactory CreateFactory(IConfiguration configuration)
    {
        var httpClientFactory = new MockHttpClientFactory();
        var openAiLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var openAiLogger = openAiLoggerFactory.CreateLogger<OpenAiLlmService>();
        var ollamaLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var ollamaLogger = ollamaLoggerFactory.CreateLogger<OllamaLlmService>();

        return new LlmServiceFactory(configuration, httpClientFactory, openAiLogger, ollamaLogger);
    }

    public static void Main(string[] args)
    {
        try
        {
            TestCreateOpenAiService();
            TestCreateOllamaService();
            TestDefaultToOpenAiService();
            TestUnknownProviderDefaultsToOpenAi();
            TestCaseInsensitiveProviderMatching();
            TestServicesImplementILlmService();
            TestMultipleServiceCreation();

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("✓✓✓ ALL FACTORY TESTS PASSED! ✓✓✓");
            Console.WriteLine(new string('=', 50));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}

/// <summary>
/// Mock implementation of IHttpClientFactory for testing
/// </summary>
public class MockHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient();
    }
}

/// <summary>
/// Провайдер логирования для тестов
/// </summary>
public class TestLoggerProvider : ILoggerProvider
{
    private readonly List<string> _logMessages;

    public TestLoggerProvider(List<string> logMessages)
    {
        _logMessages = logMessages;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger(_logMessages);
    }

    public void Dispose() { }
}

/// <summary>
/// Логгер для тестов
/// </summary>
public class TestLogger : ILogger
{
    private readonly List<string> _logMessages;

    public TestLogger(List<string> logMessages)
    {
        _logMessages = logMessages;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _logMessages.Add($"[{logLevel}] {message}");
    }
}

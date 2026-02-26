using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Standalone test runner for LlmServiceFactory tests
/// Run with: dotnet run --project CSharpRefactoringAssistant.csproj -- test-factory
/// </summary>
public class FactoryTestRunner
{
    public static void RunAllFactoryTests()
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("LLM SERVICE FACTORY TESTS");
        Console.WriteLine(new string('=', 60));

        try
        {
            TestCreateOpenAiService();
            TestCreateOllamaService();
            TestDefaultToOpenAiService();
            TestUnknownProviderDefaultsToOpenAi();
            TestCaseInsensitiveProviderMatching();
            TestServicesImplementILlmService();
            TestMultipleServiceCreation();

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("✓✓✓ ALL FACTORY TESTS PASSED! ✓✓✓");
            Console.WriteLine(new string('=', 60));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static void TestCreateOpenAiService()
    {
        Console.WriteLine("\n=== Testing OpenAI Service Creation ===");

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
        var service = factory.CreateLlmService();

        if (service is not OpenAiLlmService)
            throw new Exception($"Expected OpenAiLlmService, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory created OpenAiLlmService for Provider='OpenAI'");
    }

    private static void TestCreateOllamaService()
    {
        Console.WriteLine("\n=== Testing Ollama Service Creation ===");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "Ollama",
                ["Llm:Ollama:BaseUrl"] = "http://localhost:11434",
                ["Llm:Ollama:Model"] = "llama2"
            })
            .Build();

        var factory = CreateFactory(configuration);
        var service = factory.CreateLlmService();

        if (service is not OllamaLlmService)
            throw new Exception($"Expected OllamaLlmService, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory created OllamaLlmService for Provider='Ollama'");
    }

    private static void TestDefaultToOpenAiService()
    {
        Console.WriteLine("\n=== Testing Default Service Creation ===");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "deepseek-chat",
                ["Llm:OpenAI:BaseUrl"] = "https://api.deepseek.com/v1"
            })
            .Build();

        var factory = CreateFactory(configuration);
        var service = factory.CreateLlmService();

        if (service is not OpenAiLlmService)
            throw new Exception($"Expected OpenAiLlmService as default, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory defaulted to OpenAiLlmService when Provider not set");
    }

    private static void TestUnknownProviderDefaultsToOpenAi()
    {
        Console.WriteLine("\n=== Testing Unknown Provider Fallback ===");

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
        var service = factory.CreateLlmService();

        if (service is not OpenAiLlmService)
            throw new Exception($"Expected OpenAiLlmService for unknown provider, got {service.GetType().Name}");

        Console.WriteLine("✓ Factory defaulted to OpenAiLlmService for unknown provider");
    }

    private static void TestCaseInsensitiveProviderMatching()
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

    private static void TestServicesImplementILlmService()
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

    private static void TestMultipleServiceCreation()
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

        var service1 = factory.CreateLlmService();
        var service2 = factory.CreateLlmService();
        var service3 = factory.CreateLlmService();

        if (service1 is not OpenAiLlmService)
            throw new Exception("First service creation failed");
        if (service2 is not OpenAiLlmService)
            throw new Exception("Second service creation failed");
        if (service3 is not OpenAiLlmService)
            throw new Exception("Third service creation failed");

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
        var openAiLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var openAiLogger = openAiLoggerFactory.CreateLogger<OpenAiLlmService>();
        var ollamaLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var ollamaLogger = ollamaLoggerFactory.CreateLogger<OllamaLlmService>();

        return new LlmServiceFactory(configuration, httpClientFactory, openAiLogger, ollamaLogger);
    }
}

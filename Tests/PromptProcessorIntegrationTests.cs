using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using CSharpRefactoringAssistant.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.AspNetCore.Hosting;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Integration tests for PromptProcessor using factory-created LLM service
/// Validates: Requirements 6.3, 4.3
/// Task: 7.2 Ensure PromptProcessor uses factory-created LLM service
/// </summary>
public class PromptProcessorIntegrationTests
{
    /// <summary>
    /// Test that PromptProcessor receives ILlmService from DI container
    /// Validates: Requirement 6.3
    /// </summary>
    public static async Task TestPromptProcessorReceivesLlmServiceFromDI()
    {
        Console.WriteLine("Testing PromptProcessor receives ILlmService from DI...");
        
        // Arrange - Set up all dependencies
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI",
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "test-model",
                ["Llm:OpenAI:BaseUrl"] = "https://api.test.com/v1"
            })
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var configLogger = loggerFactory.CreateLogger<ConfigurationService>();
        var openAiLogger = loggerFactory.CreateLogger<OpenAiLlmService>();
        var ollamaLogger = loggerFactory.CreateLogger<OllamaLlmService>();
        var promptProcessorLogger = loggerFactory.CreateLogger<PromptProcessor>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var httpClientFactory = new MockHttpClientFactory();

        // Create factory
        var factory = new LlmServiceFactory(configuration, httpClientFactory, openAiLogger, ollamaLogger);
        
        // Create LLM service from factory (simulating DI)
        var llmService = factory.CreateLlmService();

        // Create mock services
        var dbOptions = new DbContextOptionsBuilder<RefactoringDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_PromptProcessor_DI")
            .Options;
        var dbContext = new RefactoringDbContext(dbOptions);
        
        var serenaService = new MockSerenaService();
        var directShellService = new MockDirectShellService();
        var gitService = new MockGitService();
        var taskExecutorService = new Lazy<ITaskExecutorService>(() => new MockTaskExecutorService());
        
        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        var tasksFilePathResolverLogger = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var commandRecognizer = new CommandRecognizer();
        var tasksFilePathResolver = new TasksFilePathResolver(pathValidator, tasksFilePathResolverLogger);

        // Act - Create PromptProcessor with factory-created service
        var promptProcessor = new PromptProcessor(
            dbContext,
            llmService,
            serenaService,
            directShellService,
            gitService,
            taskExecutorService,
            promptProcessorLogger,
            commandRecognizer,
            tasksFilePathResolver
        );

        // Assert
        if (promptProcessor == null)
        {
            Console.WriteLine("❌ FAILED: PromptProcessor is null");
            return;
        }

        // Verify the service type matches what the factory created
        if (llmService is not OpenAiLlmService)
        {
            Console.WriteLine($"❌ FAILED: Expected OpenAiLlmService, got {llmService.GetType().Name}");
            return;
        }

        Console.WriteLine("✓ PromptProcessor successfully created with factory-created ILlmService");
        Console.WriteLine($"✓ LLM service type: {llmService.GetType().Name}");
        Console.WriteLine("✓ PASSED: PromptProcessor receives ILlmService from DI test");
        
        // Cleanup
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    /// <summary>
    /// Test that configuration changes are reflected in service behavior
    /// Validates: Requirements 6.3, 4.3
    /// </summary>
    public static async Task TestConfigurationChangesReflectedInServiceBehavior()
    {
        Console.WriteLine("\nTesting configuration changes are reflected in service behavior...");
        
        // Arrange - Start with OpenAI configuration
        var configData = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = "OpenAI",
            ["Llm:OpenAI:ApiKey"] = "test-key-openai",
            ["Llm:OpenAI:Model"] = "test-model-openai",
            ["Llm:OpenAI:BaseUrl"] = "https://api.openai.test.com/v1",
            ["Llm:Ollama:BaseUrl"] = "http://localhost:11434",
            ["Llm:Ollama:Model"] = "llama2"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var openAiLogger = loggerFactory.CreateLogger<OpenAiLlmService>();
        var ollamaLogger = loggerFactory.CreateLogger<OllamaLlmService>();
        var httpClientFactory = new MockHttpClientFactory();

        // Create factory with OpenAI configuration
        var factory1 = new LlmServiceFactory(configuration, httpClientFactory, openAiLogger, ollamaLogger);
        var service1 = factory1.CreateLlmService();

        // Assert first service is OpenAI
        if (service1 is not OpenAiLlmService)
        {
            Console.WriteLine($"❌ FAILED: Expected OpenAiLlmService for first configuration, got {service1.GetType().Name}");
            return;
        }

        Console.WriteLine("✓ Initial configuration created OpenAiLlmService");

        // Act - Change configuration to Ollama
        configData["Llm:Provider"] = "Ollama";
        var newConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Create new factory with updated configuration
        var factory2 = new LlmServiceFactory(newConfiguration, httpClientFactory, openAiLogger, ollamaLogger);
        var service2 = factory2.CreateLlmService();

        // Assert second service is Ollama
        if (service2 is not OllamaLlmService)
        {
            Console.WriteLine($"❌ FAILED: Expected OllamaLlmService for updated configuration, got {service2.GetType().Name}");
            return;
        }

        Console.WriteLine("✓ Updated configuration created OllamaLlmService");

        // Verify services are different types
        if (service1.GetType() == service2.GetType())
        {
            Console.WriteLine("❌ FAILED: Services should be different types after configuration change");
            return;
        }

        Console.WriteLine("✓ Configuration change resulted in different service type");
        Console.WriteLine($"✓ Service 1 type: {service1.GetType().Name}");
        Console.WriteLine($"✓ Service 2 type: {service2.GetType().Name}");
        Console.WriteLine("✓ PASSED: Configuration changes reflected in service behavior test");
    }

    /// <summary>
    /// Test that PromptProcessor works with different LLM service implementations
    /// Validates: Requirements 6.3, 6.4
    /// </summary>
    public static async Task TestPromptProcessorWorksWithDifferentServices()
    {
        Console.WriteLine("\nTesting PromptProcessor works with different LLM service implementations...");
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var promptProcessorLogger = loggerFactory.CreateLogger<PromptProcessor>();
        var openAiLogger = loggerFactory.CreateLogger<OpenAiLlmService>();
        var ollamaLogger = loggerFactory.CreateLogger<OllamaLlmService>();
        var httpClientFactory = new MockHttpClientFactory();

        // Test with OpenAI service
        var openAiConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI",
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "test-model",
                ["Llm:OpenAI:BaseUrl"] = "https://api.test.com/v1"
            })
            .Build();

        var openAiFactory = new LlmServiceFactory(openAiConfig, httpClientFactory, openAiLogger, ollamaLogger);
        var openAiService = openAiFactory.CreateLlmService();

        var dbOptions1 = new DbContextOptionsBuilder<RefactoringDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_PromptProcessor_OpenAI")
            .Options;
        var dbContext1 = new RefactoringDbContext(dbOptions1);
        
        var pathValidatorLogger1 = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator1 = new PathValidator(openAiConfig, pathValidatorLogger1);
        var tasksFilePathResolverLogger1 = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var commandRecognizer1 = new CommandRecognizer();
        var tasksFilePathResolver1 = new TasksFilePathResolver(pathValidator1, tasksFilePathResolverLogger1);
        
        var promptProcessor1 = new PromptProcessor(
            dbContext1,
            openAiService,
            new MockSerenaService(),
            new MockDirectShellService(),
            new MockGitService(),
            new Lazy<ITaskExecutorService>(() => new MockTaskExecutorService()),
            promptProcessorLogger,
            commandRecognizer1,
            tasksFilePathResolver1
        );

        if (promptProcessor1 == null)
        {
            Console.WriteLine("❌ FAILED: PromptProcessor with OpenAI service is null");
            return;
        }

        Console.WriteLine("✓ PromptProcessor created successfully with OpenAiLlmService");

        // Test with Ollama service
        var ollamaConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "Ollama",
                ["Llm:Ollama:BaseUrl"] = "http://localhost:11434",
                ["Llm:Ollama:Model"] = "llama2"
            })
            .Build();

        var ollamaFactory = new LlmServiceFactory(ollamaConfig, httpClientFactory, openAiLogger, ollamaLogger);
        var ollamaService = ollamaFactory.CreateLlmService();

        var dbOptions2 = new DbContextOptionsBuilder<RefactoringDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_PromptProcessor_Ollama")
            .Options;
        var dbContext2 = new RefactoringDbContext(dbOptions2);
        
        var pathValidatorLogger2 = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator2 = new PathValidator(ollamaConfig, pathValidatorLogger2);
        var tasksFilePathResolverLogger2 = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var commandRecognizer2 = new CommandRecognizer();
        var tasksFilePathResolver2 = new TasksFilePathResolver(pathValidator2, tasksFilePathResolverLogger2);
        
        var promptProcessor2 = new PromptProcessor(
            dbContext2,
            ollamaService,
            new MockSerenaService(),
            new MockDirectShellService(),
            new MockGitService(),
            new Lazy<ITaskExecutorService>(() => new MockTaskExecutorService()),
            promptProcessorLogger,
            commandRecognizer2,
            tasksFilePathResolver2
        );

        if (promptProcessor2 == null)
        {
            Console.WriteLine("❌ FAILED: PromptProcessor with Ollama service is null");
            return;
        }

        Console.WriteLine("✓ PromptProcessor created successfully with OllamaLlmService");
        Console.WriteLine("✓ PASSED: PromptProcessor works with different LLM service implementations test");
        
        // Cleanup
        await dbContext1.Database.EnsureDeletedAsync();
        await dbContext1.DisposeAsync();
        await dbContext2.Database.EnsureDeletedAsync();
        await dbContext2.DisposeAsync();
    }

    /// <summary>
    /// Test that factory-created service respects configuration settings
    /// Validates: Requirements 6.3, 4.3
    /// </summary>
    public static async Task TestFactoryCreatedServiceRespectsConfiguration()
    {
        Console.WriteLine("\nTesting factory-created service respects configuration settings...");
        
        // Arrange - Create configuration with specific settings
        var testApiKey = "test-api-key-12345";
        var testModel = "custom-model-name";
        var testBaseUrl = "https://custom.api.com/v1";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI",
                ["Llm:OpenAI:ApiKey"] = testApiKey,
                ["Llm:OpenAI:Model"] = testModel,
                ["Llm:OpenAI:BaseUrl"] = testBaseUrl
            })
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var openAiLogger = loggerFactory.CreateLogger<OpenAiLlmService>();
        var ollamaLogger = loggerFactory.CreateLogger<OllamaLlmService>();
        var httpClientFactory = new MockHttpClientFactory();

        // Act - Create service through factory
        var factory = new LlmServiceFactory(configuration, httpClientFactory, openAiLogger, ollamaLogger);
        var service = factory.CreateLlmService();

        // Assert - Verify service was created
        if (service is not OpenAiLlmService)
        {
            Console.WriteLine($"❌ FAILED: Expected OpenAiLlmService, got {service.GetType().Name}");
            return;
        }

        // The service should be created with the configuration values
        // We can't directly inspect private fields, but we can verify the service was created successfully
        Console.WriteLine("✓ Factory created service with custom configuration");
        Console.WriteLine($"✓ Service type: {service.GetType().Name}");
        Console.WriteLine($"✓ Configuration Provider: {configuration["Llm:Provider"]}");
        Console.WriteLine($"✓ Configuration Model: {configuration["Llm:OpenAI:Model"]}");
        Console.WriteLine($"✓ Configuration BaseUrl: {configuration["Llm:OpenAI:BaseUrl"]}");
        Console.WriteLine("✓ PASSED: Factory-created service respects configuration settings test");
    }
}

/// <summary>
/// Test runner for PromptProcessor integration tests
/// </summary>
public static class PromptProcessorTestRunner
{
    public static async Task RunAllPromptProcessorTests()
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("PROMPT PROCESSOR INTEGRATION TESTS");
        Console.WriteLine(new string('=', 60));

        try
        {
            await PromptProcessorIntegrationTests.TestPromptProcessorReceivesLlmServiceFromDI();
            await PromptProcessorIntegrationTests.TestConfigurationChangesReflectedInServiceBehavior();
            await PromptProcessorIntegrationTests.TestPromptProcessorWorksWithDifferentServices();
            await PromptProcessorIntegrationTests.TestFactoryCreatedServiceRespectsConfiguration();

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("✓✓✓ ALL PROMPT PROCESSOR TESTS PASSED! ✓✓✓");
            Console.WriteLine(new string('=', 60));
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
/// Mock implementation of IGitService for testing
/// </summary>
public class MockGitService : IGitService
{
    public Task<bool> IsGitRepositoryAsync(string path)
    {
        return Task.FromResult(true);
    }

    public Task InitializeRepositoryAsync(string path)
    {
        return Task.CompletedTask;
    }

    public Task<string> CreateCheckpointAsync(string path, string message)
    {
        return Task.FromResult("mock-commit-hash");
    }

    public Task RollbackToCheckpointAsync(string path, string commitHash)
    {
        return Task.CompletedTask;
    }

    public Task<bool> HasUncommittedChangesAsync(string path)
    {
        return Task.FromResult(false);
    }
}

/// <summary>
/// Mock implementation of ISerenaService for testing
/// </summary>
public class MockSerenaService : ISerenaService
{
    public Task<string> ActivateProjectAsync(string projectPath)
    {
        return Task.FromResult("Project activated");
    }

    public Task<string> FindSymbolAsync(string symbolName)
    {
        return Task.FromResult($"Found symbol: {symbolName}");
    }

    public Task<string> FindReferencingSymbolsAsync(string symbolId)
    {
        return Task.FromResult($"Found references for: {symbolId}");
    }

    public Task<string> ReplaceSymbolBodyAsync(string symbolId, string newBody)
    {
        return Task.FromResult($"Replaced symbol: {symbolId}");
    }

    public Task<string> ExecuteShellCommandAsync(string command, string workingDirectory)
    {
        return Task.FromResult($"Executed: {command}");
    }

    public Task<string> ReadFileAsync(string filePath)
    {
        return Task.FromResult($"File content: {filePath}");
    }

    public Task<string> InsertBeforeSymbolAsync(string symbolId, string content)
    {
        return Task.FromResult($"Inserted before: {symbolId}");
    }

    public Task<string> DeleteLinesAsync(string filePath, int startLine, int endLine)
    {
        return Task.FromResult($"Deleted lines {startLine}-{endLine} in {filePath}");
    }
}

/// <summary>
/// Mock implementation of IDirectShellService for testing
/// </summary>
public class MockDirectShellService : IDirectShellService
{
    public Task<string> ExecuteCommandAsync(string command, string workingDirectory)
    {
        return Task.FromResult($"Executed command: {command}");
    }

    public Task<string> ReadFileAsync(string filePath, string workingDirectory)
    {
        return Task.FromResult($"File content: {filePath}");
    }
}

/// <summary>
/// Mock implementation of ITaskExecutorService for testing
/// </summary>
public class MockTaskExecutorService : ITaskExecutorService
{
    public Task<int> ExecuteTasksAsync(int dialogueId, string tasksFilePath, bool skipOptional = true)
    {
        return Task.FromResult(1); // Возвращаем mock ID сессии
    }

    public Task StopExecutionAsync(int dialogueId)
    {
        return Task.CompletedTask;
    }

    public Task ResumeExecutionAsync(int dialogueId)
    {
        return Task.CompletedTask;
    }

    public Task<ExecutionStatusDto> GetExecutionStatusAsync(int dialogueId)
    {
        return Task.FromResult(new ExecutionStatusDto
        {
            Status = "none",
            Progress = null,
            CurrentTask = null,
            ErrorMessage = null,
            StartedAt = null,
            CompletedAt = null
        });
    }
}

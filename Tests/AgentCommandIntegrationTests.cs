using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using CSharpRefactoringAssistant.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Интеграционные тесты для команд управления агентским режимом
/// </summary>
public static class AgentCommandIntegrationTests
{
    public static async Task RunAllTests()
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("AGENT COMMAND INTEGRATION TESTS");
        Console.WriteLine(new string('=', 60));

        await TestCommandRecognitionInPromptProcessor();
        await TestStartExecutionCommand();
        await TestStopExecutionCommand();
        await TestResumeExecutionCommand();
        await TestShowStatusCommand();
        await TestNonCommandPassesToLlm();

        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("✓✓✓ ALL AGENT COMMAND INTEGRATION TESTS PASSED! ✓✓✓");
        Console.WriteLine(new string('=', 60));
    }

    private static async Task TestCommandRecognitionInPromptProcessor()
    {
        Console.WriteLine("\nТест: Распознавание команд в PromptProcessor");
        
        var (promptProcessor, dbContext) = await CreatePromptProcessor();

        try
        {
            // Создаем диалог
            var dialogue = new Dialogue
            {
                ProjectPath = Directory.GetCurrentDirectory(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Dialogues.Add(dialogue);
            await dbContext.SaveChangesAsync();

            // Создаем тестовый файл tasks.md
            var tasksPath = Path.Combine(Directory.GetCurrentDirectory(), "test-tasks.md");
            if (!File.Exists(tasksPath))
            {
                await File.WriteAllTextAsync(tasksPath, "# Test tasks\n- [ ] Task 1");
            }

            // Отправляем команду запуска
            var response = await promptProcessor.ProcessPromptAsync(
                dialogue.Id,
                "начни выполнение задач из test-tasks.md");

            if (!response.Contains("Запущено выполнение задач"))
            {
                throw new Exception($"❌ Неверный ответ на команду запуска: {response}");
            }

            Console.WriteLine("✓ Команда запуска распознана и обработана корректно");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.DisposeAsync();
        }
    }

    private static async Task TestStartExecutionCommand()
    {
        Console.WriteLine("\nТест: Команда запуска выполнения");
        
        var (promptProcessor, dbContext) = await CreatePromptProcessor();

        try
        {
            var dialogue = new Dialogue
            {
                ProjectPath = Directory.GetCurrentDirectory(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Dialogues.Add(dialogue);
            await dbContext.SaveChangesAsync();

            var tasksPath = Path.Combine(Directory.GetCurrentDirectory(), "test-tasks.md");
            if (!File.Exists(tasksPath))
            {
                await File.WriteAllTextAsync(tasksPath, "# Test tasks\n- [ ] Task 1");
            }

            var response = await promptProcessor.ProcessPromptAsync(
                dialogue.Id,
                "execute tasks from test-tasks.md");

            if (!response.Contains("Запущено выполнение задач") && !response.Contains("выполнение задач"))
            {
                throw new Exception($"❌ Команда запуска не обработана: {response}");
            }

            Console.WriteLine("✓ Команда запуска выполнена успешно");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.DisposeAsync();
        }
    }

    private static async Task TestStopExecutionCommand()
    {
        Console.WriteLine("\nТест: Команда остановки выполнения");
        
        var (promptProcessor, dbContext) = await CreatePromptProcessor();

        try
        {
            var dialogue = new Dialogue
            {
                ProjectPath = Directory.GetCurrentDirectory(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Dialogues.Add(dialogue);
            await dbContext.SaveChangesAsync();

            var response = await promptProcessor.ProcessPromptAsync(
                dialogue.Id,
                "останови выполнение");

            if (!response.Contains("остановлено") && !response.Contains("Выполнение"))
            {
                throw new Exception($"❌ Команда остановки не обработана: {response}");
            }

            Console.WriteLine("✓ Команда остановки выполнена успешно");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.DisposeAsync();
        }
    }

    private static async Task TestResumeExecutionCommand()
    {
        Console.WriteLine("\nТест: Команда возобновления выполнения");
        
        var (promptProcessor, dbContext) = await CreatePromptProcessor();

        try
        {
            var dialogue = new Dialogue
            {
                ProjectPath = Directory.GetCurrentDirectory(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Dialogues.Add(dialogue);
            await dbContext.SaveChangesAsync();

            var response = await promptProcessor.ProcessPromptAsync(
                dialogue.Id,
                "продолжи выполнение");

            // MockTaskExecutorService не выбрасывает исключение, поэтому ожидаем успешный ответ
            if (!response.Contains("Продолжаю выполнение") && 
                !response.Contains("возобнов") &&
                !response.Contains("Нет остановленной сессии"))
            {
                throw new Exception($"❌ Команда возобновления не обработана: {response}");
            }

            Console.WriteLine("✓ Команда возобновления выполнена успешно");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.DisposeAsync();
        }
    }

    private static async Task TestShowStatusCommand()
    {
        Console.WriteLine("\nТест: Команда показа статуса");
        
        var (promptProcessor, dbContext) = await CreatePromptProcessor();

        try
        {
            var dialogue = new Dialogue
            {
                ProjectPath = Directory.GetCurrentDirectory(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Dialogues.Add(dialogue);
            await dbContext.SaveChangesAsync();

            var response = await promptProcessor.ProcessPromptAsync(
                dialogue.Id,
                "покажи статус выполнения");

            if (!response.Contains("Статус выполнения") && !response.Contains("статус"))
            {
                throw new Exception($"❌ Команда статуса не обработана: {response}");
            }

            Console.WriteLine("✓ Команда статуса выполнена успешно");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.DisposeAsync();
        }
    }

    private static async Task TestNonCommandPassesToLlm()
    {
        Console.WriteLine("\nТест: Обычные сообщения передаются в LLM");
        
        var (promptProcessor, dbContext) = await CreatePromptProcessor();

        try
        {
            var dialogue = new Dialogue
            {
                ProjectPath = Directory.GetCurrentDirectory(),
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Dialogues.Add(dialogue);
            await dbContext.SaveChangesAsync();

            // Отправляем обычное сообщение (не команду)
            // Ожидаем исключение, так как используется mock LLM с недоступным URL
            try
            {
                var response = await promptProcessor.ProcessPromptAsync(
                    dialogue.Id,
                    "Привет! Как дела?");

                // Если дошли сюда, проверяем, что это не команда
                if (response.Contains("Запущено выполнение") || 
                    response.Contains("остановлено") ||
                    response.Contains("Статус выполнения"))
                {
                    throw new Exception($"❌ Обычное сообщение было обработано как команда: {response}");
                }
            }
            catch (CSharpRefactoringAssistant.Services.LlmException)
            {
                // Ожидаемое исключение - LLM недоступен, но это нормально для теста
                // Главное, что сообщение не было обработано как команда
            }

            Console.WriteLine("✓ Обычные сообщения корректно передаются в LLM");
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.DisposeAsync();
        }
    }

    private static async Task<(PromptProcessor, RefactoringDbContext)> CreatePromptProcessor()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Provider"] = "OpenAI",
                ["Llm:OpenAI:ApiKey"] = "test-key",
                ["Llm:OpenAI:Model"] = "test-model",
                ["Llm:OpenAI:BaseUrl"] = "https://api.test.com/v1"
            })
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var dbOptions = new DbContextOptionsBuilder<RefactoringDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_AgentCommands_{Guid.NewGuid()}")
            .Options;
        var dbContext = new RefactoringDbContext(dbOptions);

        var httpClientFactory = new MockHttpClientFactory();
        var openAiLogger = loggerFactory.CreateLogger<OpenAiLlmService>();
        var ollamaLogger = loggerFactory.CreateLogger<OllamaLlmService>();
        var factory = new LlmServiceFactory(configuration, httpClientFactory, openAiLogger, ollamaLogger);
        var llmService = factory.CreateLlmService();

        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        
        var tasksFilePathResolverLogger = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var tasksFilePathResolver = new TasksFilePathResolver(pathValidator, tasksFilePathResolverLogger);
        
        var commandRecognizerLogger = loggerFactory.CreateLogger<CommandRecognizer>();
        var commandRecognizer = new CommandRecognizer(commandRecognizerLogger);
        
        var promptProcessorLogger = loggerFactory.CreateLogger<PromptProcessor>();
        
        var taskExecutorService = new Lazy<ITaskExecutorService>(() => new MockTaskExecutorService());
        
        var mockProjectService = new MockProjectManagementService();

        var promptProcessor = new PromptProcessor(
            dbContext,
            llmService,
            new MockSerenaService(),
            new MockDirectShellService(),
            new MockGitService(),
            taskExecutorService,
            mockProjectService,
            promptProcessorLogger,
            commandRecognizer,
            tasksFilePathResolver,
            new MockDeepSeekOrchestrator(),
            new MockConfigurationService()
        );

        return (promptProcessor, dbContext);
    }
    
    // Mock для IProjectManagementService
    private class MockProjectManagementService : IProjectManagementService
    {
        public Task<List<Project>> GetAllProjectsAsync() => Task.FromResult(new List<Project>());
        public Task<Project?> GetSelectedProjectAsync() => Task.FromResult<Project?>(null);
        public Task<Project> AddProjectAsync(string projectPath) => throw new NotImplementedException();
        public Task DeleteProjectAsync(int projectId) => throw new NotImplementedException();
        public Task SelectProjectAsync(int projectId) => throw new NotImplementedException();
    }
}

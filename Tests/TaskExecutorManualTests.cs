using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Ручные тесты для проверки функциональности TaskExecutorService
/// </summary>
public class TaskExecutorManualTests
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Тесты TaskExecutorService ===\n");

        await TestStopExecutionAsync();
        await TestResumeExecutionAsync();
        await TestGetExecutionStatusAsync();

        Console.WriteLine("\n=== Все тесты завершены ===");
    }

    private static async Task TestStopExecutionAsync()
    {
        Console.WriteLine("Тест 1: StopExecutionAsync");
        Console.WriteLine("Описание: Проверка остановки выполнения задач");

        try
        {
            var options = new DbContextOptionsBuilder<RefactoringDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDb_Stop")
                .Options;

            using var context = new RefactoringDbContext(options);
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<TaskExecutorService>();
            
            // Создаем мок-конфигурацию
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();
            var pathValidator = new PathValidator(configuration, loggerFactory.CreateLogger<PathValidator>());

            // Создаем мок-сервисы
            var promptProcessor = new MockPromptProcessor();
            var gitService = new MockGitService();
            var reasoningService = new MockReasoningService();

            var service = new TaskExecutorService(
                context,
                promptProcessor,
                gitService,
                logger,
                pathValidator,
                reasoningService,
                loggerFactory
            );

            // Тест: вызов StopExecutionAsync для несуществующего диалога
            await service.StopExecutionAsync(999);
            Console.WriteLine("✓ StopExecutionAsync не выбрасывает исключение для несуществующего диалога");

            Console.WriteLine("✓ Тест пройден\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Тест провален: {ex.Message}\n");
        }
    }

    private static async Task TestResumeExecutionAsync()
    {
        Console.WriteLine("Тест 2: ResumeExecutionAsync");
        Console.WriteLine("Описание: Проверка продолжения выполнения задач");

        try
        {
            var options = new DbContextOptionsBuilder<RefactoringDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDb_Resume")
                .Options;

            using var context = new RefactoringDbContext(options);
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<TaskExecutorService>();
            
            // Создаем мок-конфигурацию
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();
            var pathValidator = new PathValidator(configuration, loggerFactory.CreateLogger<PathValidator>());

            var promptProcessor = new MockPromptProcessor();
            var gitService = new MockGitService();
            var reasoningService = new MockReasoningService();

            var service = new TaskExecutorService(
                context,
                promptProcessor,
                gitService,
                logger,
                pathValidator,
                reasoningService,
                loggerFactory
            );

            // Тест: попытка продолжить несуществующую сессию
            try
            {
                await service.ResumeExecutionAsync(999);
                Console.WriteLine("✗ Должно было выброситься исключение для несуществующей сессии");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("✓ ResumeExecutionAsync выбрасывает InvalidOperationException для несуществующей сессии");
            }

            Console.WriteLine("✓ Тест пройден\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Тест провален: {ex.Message}\n");
        }
    }

    private static async Task TestGetExecutionStatusAsync()
    {
        Console.WriteLine("Тест 3: GetExecutionStatusAsync");
        Console.WriteLine("Описание: Проверка получения статуса выполнения");

        try
        {
            var options = new DbContextOptionsBuilder<RefactoringDbContext>()
                .UseInMemoryDatabase(databaseName: "TestDb_Status")
                .Options;

            using var context = new RefactoringDbContext(options);
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<TaskExecutorService>();
            
            // Создаем мок-конфигурацию
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();
            var pathValidator = new PathValidator(configuration, loggerFactory.CreateLogger<PathValidator>());

            var promptProcessor = new MockPromptProcessor();
            var gitService = new MockGitService();
            var reasoningService = new MockReasoningService();

            var service = new TaskExecutorService(
                context,
                promptProcessor,
                gitService,
                logger,
                pathValidator,
                reasoningService,
                loggerFactory
            );

            // Тест: получение статуса для несуществующего диалога
            var status = await service.GetExecutionStatusAsync(999);
            if (status.Status == "none")
            {
                Console.WriteLine("✓ GetExecutionStatusAsync возвращает status='none' для несуществующего диалога");
            }
            else
            {
                Console.WriteLine($"✗ Ожидался status='none', получен '{status.Status}'");
            }

            // Создаем диалог и сессию
            var dialogue = new Dialogue
            {
                ProjectPath = "C:\\test",
                CreatedAt = DateTime.UtcNow
            };
            context.Dialogues.Add(dialogue);
            await context.SaveChangesAsync();

            var session = new ExecutionSession
            {
                DialogueId = dialogue.Id,
                TasksFilePath = "tasks.md",
                Status = "running",
                Progress = "1/5",
                CurrentTask = "Test task",
                StartedAt = DateTime.UtcNow,
                SkipOptional = true
            };
            context.ExecutionSessions.Add(session);
            await context.SaveChangesAsync();

            // Тест: получение статуса для существующей сессии
            status = await service.GetExecutionStatusAsync(dialogue.Id);
            if (status.Status == "running" && status.Progress == "1/5" && status.CurrentTask == "Test task")
            {
                Console.WriteLine("✓ GetExecutionStatusAsync возвращает корректный статус для существующей сессии");
            }
            else
            {
                Console.WriteLine($"✗ Некорректный статус: {status.Status}, {status.Progress}, {status.CurrentTask}");
            }

            Console.WriteLine("✓ Тест пройден\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Тест провален: {ex.Message}\n");
        }
    }

    // Мок-классы для тестирования
    private class MockPromptProcessor : IPromptProcessor
    {
        public Task<string> ProcessPromptAsync(int dialogueId, string prompt)
        {
            return Task.FromResult("Mock response");
        }
        
        public List<CSharpRefactoringAssistant.Models.FunctionDefinition> GetAvailableTools()
        {
            return new List<CSharpRefactoringAssistant.Models.FunctionDefinition>();
        }
        
        public Task<string> ExecuteFunctionAsync(string functionName, Dictionary<string, object> arguments, string projectPath)
        {
            return Task.FromResult("Mock function result");
        }
    }

    private class MockGitService : IGitService
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

    private class MockReasoningService : IReasoningService
    {
        public Task<string> CreateTaskPlanAsync(string taskDescription, string projectPath)
        {
            return Task.FromResult($"Mock plan for: {taskDescription}");
        }
    }
}

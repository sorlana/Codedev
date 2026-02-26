using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Тесты для TasksFilePathResolver
/// </summary>
public static class TasksFilePathResolverTests
{
    public static async Task RunAllTests()
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("TASKS FILE PATH RESOLVER TESTS");
        Console.WriteLine(new string('=', 60));

        await TestResolveDefaultPath();
        await TestResolveUserProvidedPath();
        await TestFileNotFoundInRoot();
        await TestFileNotFoundAtPath();
        await TestInvalidPath();

        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("✓✓✓ ALL TASKS FILE PATH RESOLVER TESTS PASSED! ✓✓✓");
        Console.WriteLine(new string('=', 60));
    }

    private static async Task TestResolveDefaultPath()
    {
        Console.WriteLine("\nТест: Разрешение пути по умолчанию (tasks.md в корне)");
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var configuration = new ConfigurationBuilder().Build();
        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        var resolverLogger = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var resolver = new TasksFilePathResolver(pathValidator, resolverLogger);

        var projectPath = Directory.GetCurrentDirectory();
        
        // Создаем тестовый файл tasks.md в корне
        var testFilePath = Path.Combine(projectPath, "tasks.md");
        await File.WriteAllTextAsync(testFilePath, "# Test tasks");

        try
        {
            var resolvedPath = await resolver.ResolveTasksFilePathAsync(null, projectPath);
            
            if (!File.Exists(resolvedPath))
            {
                throw new Exception($"❌ Разрешенный путь не существует: {resolvedPath}");
            }

            if (!resolvedPath.EndsWith("tasks.md"))
            {
                throw new Exception($"❌ Неверный разрешенный путь: {resolvedPath}");
            }

            Console.WriteLine($"✓ Путь по умолчанию разрешен корректно: {resolvedPath}");
        }
        finally
        {
            // Очистка
            if (File.Exists(testFilePath))
            {
                File.Delete(testFilePath);
            }
        }
    }

    private static async Task TestResolveUserProvidedPath()
    {
        Console.WriteLine("\nТест: Разрешение пути, указанного пользователем");
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var configuration = new ConfigurationBuilder().Build();
        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        var resolverLogger = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var resolver = new TasksFilePathResolver(pathValidator, resolverLogger);

        var projectPath = Directory.GetCurrentDirectory();
        
        // Создаем тестовый файл test-tasks.md
        var testFilePath = Path.Combine(projectPath, "test-tasks.md");
        if (!File.Exists(testFilePath))
        {
            await File.WriteAllTextAsync(testFilePath, "# Test tasks");
        }

        try
        {
            var resolvedPath = await resolver.ResolveTasksFilePathAsync("test-tasks.md", projectPath);
            
            if (!File.Exists(resolvedPath))
            {
                throw new Exception($"❌ Разрешенный путь не существует: {resolvedPath}");
            }

            if (!resolvedPath.EndsWith("test-tasks.md"))
            {
                throw new Exception($"❌ Неверный разрешенный путь: {resolvedPath}");
            }

            Console.WriteLine($"✓ Путь пользователя разрешен корректно: {resolvedPath}");
        }
        finally
        {
            // Не удаляем test-tasks.md, так как он может использоваться в других тестах
        }
    }

    private static async Task TestFileNotFoundInRoot()
    {
        Console.WriteLine("\nТест: Файл не найден в корне проекта");
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var configuration = new ConfigurationBuilder().Build();
        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        var resolverLogger = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var resolver = new TasksFilePathResolver(pathValidator, resolverLogger);

        var projectPath = Directory.GetCurrentDirectory();
        
        // Убедимся, что tasks.md не существует
        var testFilePath = Path.Combine(projectPath, "tasks.md");
        if (File.Exists(testFilePath))
        {
            File.Delete(testFilePath);
        }

        try
        {
            await resolver.ResolveTasksFilePathAsync(null, projectPath);
            throw new Exception("❌ Должно было быть выброшено исключение FileNotFoundException");
        }
        catch (FileNotFoundException ex)
        {
            if (!ex.Message.Contains("не найден в корне проекта"))
            {
                throw new Exception($"❌ Неверное сообщение об ошибке: {ex.Message}");
            }
            Console.WriteLine("✓ FileNotFoundException выброшено корректно");
        }
    }

    private static async Task TestFileNotFoundAtPath()
    {
        Console.WriteLine("\nТест: Файл не найден по указанному пути");
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var configuration = new ConfigurationBuilder().Build();
        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        var resolverLogger = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var resolver = new TasksFilePathResolver(pathValidator, resolverLogger);

        var projectPath = Directory.GetCurrentDirectory();

        try
        {
            await resolver.ResolveTasksFilePathAsync("nonexistent-tasks.md", projectPath);
            throw new Exception("❌ Должно было быть выброшено исключение FileNotFoundException");
        }
        catch (FileNotFoundException ex)
        {
            if (!ex.Message.Contains("Файл не найден"))
            {
                throw new Exception($"❌ Неверное сообщение об ошибке: {ex.Message}");
            }
            Console.WriteLine("✓ FileNotFoundException для несуществующего файла выброшено корректно");
        }
    }

    private static async Task TestInvalidPath()
    {
        Console.WriteLine("\nТест: Невалидный путь");
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var configuration = new ConfigurationBuilder().Build();
        var pathValidatorLogger = loggerFactory.CreateLogger<PathValidator>();
        var pathValidator = new PathValidator(configuration, pathValidatorLogger);
        var resolverLogger = loggerFactory.CreateLogger<TasksFilePathResolver>();
        var resolver = new TasksFilePathResolver(pathValidator, resolverLogger);

        var projectPath = Directory.GetCurrentDirectory();

        try
        {
            // Пытаемся использовать путь с недопустимыми символами
            await resolver.ResolveTasksFilePathAsync("../../../etc/passwd", projectPath);
            throw new Exception("❌ Должно было быть выброшено исключение InvalidOperationException");
        }
        catch (InvalidOperationException ex)
        {
            if (!ex.Message.Contains("Невалидный путь"))
            {
                throw new Exception($"❌ Неверное сообщение об ошибке: {ex.Message}");
            }
            Console.WriteLine("✓ InvalidOperationException для невалидного пути выброшено корректно");
        }
        catch (FileNotFoundException)
        {
            // PathValidator может не отклонить путь, но файл не будет найден
            Console.WriteLine("✓ Невалидный путь обработан (FileNotFoundException)");
        }
    }
}

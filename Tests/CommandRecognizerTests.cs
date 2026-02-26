using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Тесты для CommandRecognizer
/// </summary>
public static class CommandRecognizerTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("COMMAND RECOGNIZER TESTS");
        Console.WriteLine(new string('=', 60));

        TestStartExecutionRussian();
        TestStartExecutionEnglish();
        TestStopExecutionRussian();
        TestStopExecutionEnglish();
        TestResumeExecutionRussian();
        TestResumeExecutionEnglish();
        TestShowStatusRussian();
        TestShowStatusEnglish();
        TestExtractFilePath();
        TestCaseInsensitive();
        TestNonCommand();

        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("✓✓✓ ALL COMMAND RECOGNIZER TESTS PASSED! ✓✓✓");
        Console.WriteLine(new string('=', 60));
    }

    private static void TestStartExecutionRussian()
    {
        Console.WriteLine("\nТест: Распознавание команды запуска (русский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "начни выполнение задач из tasks.md",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.StartExecution)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        if (filePath != "tasks.md")
        {
            throw new Exception($"❌ Неверный путь: {filePath}");
        }

        Console.WriteLine("✓ Команда запуска (русский) распознана корректно");
    }

    private static void TestStartExecutionEnglish()
    {
        Console.WriteLine("\nТест: Распознавание команды запуска (английский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "execute tasks from file tasks.md",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.StartExecution)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        if (filePath != "tasks.md")
        {
            throw new Exception($"❌ Неверный путь: {filePath}");
        }

        Console.WriteLine("✓ Команда запуска (английский) распознана корректно");
    }

    private static void TestStopExecutionRussian()
    {
        Console.WriteLine("\nТест: Распознавание команды остановки (русский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "останови выполнение",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.StopExecution)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        Console.WriteLine("✓ Команда остановки (русский) распознана корректно");
    }

    private static void TestStopExecutionEnglish()
    {
        Console.WriteLine("\nТест: Распознавание команды остановки (английский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "stop execution",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.StopExecution)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        Console.WriteLine("✓ Команда остановки (английский) распознана корректно");
    }

    private static void TestResumeExecutionRussian()
    {
        Console.WriteLine("\nТест: Распознавание команды возобновления (русский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "продолжи выполнение",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.ResumeExecution)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        Console.WriteLine("✓ Команда возобновления (русский) распознана корректно");
    }

    private static void TestResumeExecutionEnglish()
    {
        Console.WriteLine("\nТест: Распознавание команды возобновления (английский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "resume execution",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.ResumeExecution)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        Console.WriteLine("✓ Команда возобновления (английский) распознана корректно");
    }

    private static void TestShowStatusRussian()
    {
        Console.WriteLine("\nТест: Распознавание команды статуса (русский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "покажи статус выполнения",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.ShowStatus)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        Console.WriteLine("✓ Команда статуса (русский) распознана корректно");
    }

    private static void TestShowStatusEnglish()
    {
        Console.WriteLine("\nТест: Распознавание команды статуса (английский)");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "show status",
            out var commandType,
            out var filePath);

        if (!result)
        {
            throw new Exception("❌ Команда не распознана");
        }

        if (commandType != AgentCommandType.ShowStatus)
        {
            throw new Exception($"❌ Неверный тип команды: {commandType}");
        }

        Console.WriteLine("✓ Команда статуса (английский) распознана корректно");
    }

    private static void TestExtractFilePath()
    {
        Console.WriteLine("\nТест: Извлечение пути к файлу");
        var recognizer = new CommandRecognizer();
        
        // Тест с относительным путем
        var result1 = recognizer.TryRecognizeCommand(
            "начни выполнение задач из .kiro/specs/feature/tasks.md",
            out var commandType1,
            out var filePath1);

        if (!result1 || filePath1 != ".kiro/specs/feature/tasks.md")
        {
            throw new Exception($"❌ Неверный путь: {filePath1}");
        }

        // Тест с английским вариантом
        var result2 = recognizer.TryRecognizeCommand(
            "execute tasks from file .kiro/specs/test/tasks.md",
            out var commandType2,
            out var filePath2);

        if (!result2 || filePath2 != ".kiro/specs/test/tasks.md")
        {
            throw new Exception($"❌ Неверный путь: {filePath2}");
        }

        Console.WriteLine("✓ Извлечение пути к файлу работает корректно");
    }

    private static void TestCaseInsensitive()
    {
        Console.WriteLine("\nТест: Игнорирование регистра");
        var recognizer = new CommandRecognizer();
        
        var result1 = recognizer.TryRecognizeCommand(
            "НАЧНИ ВЫПОЛНЕНИЕ ЗАДАЧ",
            out var commandType1,
            out var filePath1);

        if (!result1 || commandType1 != AgentCommandType.StartExecution)
        {
            throw new Exception("❌ Команда в верхнем регистре не распознана");
        }

        var result2 = recognizer.TryRecognizeCommand(
            "StOp ExEcUtIoN",
            out var commandType2,
            out var filePath2);

        if (!result2 || commandType2 != AgentCommandType.StopExecution)
        {
            throw new Exception("❌ Команда в смешанном регистре не распознана");
        }

        Console.WriteLine("✓ Игнорирование регистра работает корректно");
    }

    private static void TestNonCommand()
    {
        Console.WriteLine("\nТест: Не-команда");
        var recognizer = new CommandRecognizer();
        
        var result = recognizer.TryRecognizeCommand(
            "Привет, как дела? Расскажи о проекте.",
            out var commandType,
            out var filePath);

        if (result)
        {
            throw new Exception("❌ Обычный текст был распознан как команда");
        }

        Console.WriteLine("✓ Обычный текст не распознается как команда");
    }
}

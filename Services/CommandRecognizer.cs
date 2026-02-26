using System.Text.RegularExpressions;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Компонент для распознавания команд управления агентским режимом выполнения задач.
/// Поддерживает команды на русском и английском языках с игнорированием регистра.
/// </summary>
public class CommandRecognizer
{
    /// <summary>
    /// Словарь паттернов команд для каждого типа команды.
    /// Ключ - тип команды, значение - список паттернов на русском и английском.
    /// </summary>
    private static readonly Dictionary<AgentCommandType, List<string>> CommandPatterns = new()
    {
        [AgentCommandType.StartExecution] = new()
        {
            "начни выполнение",
            "запусти выполнение",
            "выполни задачи",
            "start execution",
            "execute tasks",
            "run tasks"
        },
        [AgentCommandType.StopExecution] = new()
        {
            "останови выполнение",
            "стоп",
            "прекрати выполнение",
            "stop execution",
            "stop",
            "halt execution"
        },
        [AgentCommandType.ResumeExecution] = new()
        {
            "продолжи выполнение",
            "возобнови выполнение",
            "продолжить",
            "resume execution",
            "continue execution",
            "resume"
        },
        [AgentCommandType.ShowStatus] = new()
        {
            "покажи статус",
            "статус выполнения",
            "что происходит",
            "show status",
            "execution status",
            "status"
        }
    };

    /// <summary>
    /// Пытается распознать команду управления агентским режимом в тексте промпта.
    /// </summary>
    /// <param name="prompt">Текст промпта от пользователя</param>
    /// <param name="commandType">Распознанный тип команды (out параметр)</param>
    /// <param name="filePath">Извлеченный путь к файлу, если указан (out параметр)</param>
    /// <returns>true если команда распознана, false в противном случае</returns>
    public bool TryRecognizeCommand(
        string prompt,
        out AgentCommandType commandType,
        out string? filePath)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            commandType = default;
            filePath = null;
            return false;
        }

        // Нормализация промпта (lowercase, trim)
        var normalized = prompt.ToLowerInvariant().Trim();

        // Поиск совпадений с паттернами
        foreach (var (type, patterns) in CommandPatterns)
        {
            if (patterns.Any(p => normalized.Contains(p)))
            {
                commandType = type;
                filePath = ExtractFilePath(prompt);
                return true;
            }
        }

        commandType = default;
        filePath = null;
        return false;
    }

    /// <summary>
    /// Извлекает путь к файлу из текста команды с использованием регулярных выражений.
    /// Поддерживает паттерны: "из файла X", "from file X", "из X.md", "from X.md"
    /// </summary>
    /// <param name="prompt">Текст промпта от пользователя</param>
    /// <returns>Извлеченный путь к файлу или null если путь не найден</returns>
    private string? ExtractFilePath(string prompt)
    {
        // Регулярные выражения для извлечения пути к файлу
        // Паттерны: "из файла X", "из X", "from file X", "from X"
        var patterns = new[]
        {
            @"из\s+файла\s+([^\s]+)",           // "из файла tasks.md"
            @"из\s+([^\s]+\.md)",                // "из tasks.md"
            @"from\s+file\s+([^\s]+)",          // "from file tasks.md"
            @"from\s+([^\s]+\.md)"               // "from tasks.md"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(prompt, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }
}

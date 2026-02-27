using System.Text.RegularExpressions;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Компонент для распознавания команд управления агентским режимом выполнения задач.
/// Поддерживает команды на русском и английском языках с игнорированием регистра.
/// </summary>
public class CommandRecognizer
{
    private readonly ILogger<CommandRecognizer> _logger;

    public CommandRecognizer(ILogger<CommandRecognizer> logger)
    {
        _logger = logger;
    }
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
            "выполни все задачи",
            "выполни задачу",
            "start execution",
            "execute tasks",
            "execute all tasks",
            "execute task",
            "run tasks",
            "run all tasks",
            "run task"
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
    /// Пытается распознать команду управления агентским режимом в тексте промпта с извлечением номера задачи.
    /// </summary>
    /// <param name="prompt">Текст промпта от пользователя</param>
    /// <param name="commandType">Распознанный тип команды (out параметр)</param>
    /// <param name="filePath">Извлеченный путь к файлу, если указан (out параметр)</param>
    /// <param name="taskNumber">Номер задачи, если указан (out параметр)</param>
    /// <returns>true если команда распознана, false в противном случае</returns>
    public bool TryRecognizeCommand(
        string prompt,
        out AgentCommandType commandType,
        out string? filePath,
        out int? taskNumber)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            commandType = default;
            filePath = null;
            taskNumber = null;
            return false;
        }

        // Нормализация промпта (lowercase, trim)
        var normalized = prompt.ToLowerInvariant().Trim();
        
        _logger.LogInformation("[CommandRecognizer] Исходный промпт: '{Prompt}'", prompt);
        _logger.LogInformation("[CommandRecognizer] Нормализованный промпт: '{Normalized}'", normalized);

        // Поиск совпадений с паттернами
        foreach (var (type, patterns) in CommandPatterns)
        {
            foreach (var pattern in patterns)
            {
                if (normalized.Contains(pattern))
                {
                    _logger.LogInformation("[CommandRecognizer] Найдено совпадение с паттерном '{Pattern}' для типа {Type}", pattern, type);
                    commandType = type;
                    filePath = ExtractFilePath(prompt);
                    taskNumber = ExtractTaskNumber(prompt);
                    _logger.LogInformation("[CommandRecognizer] Извлечено: filePath='{FilePath}', taskNumber={TaskNumber}", filePath ?? "(null)", taskNumber?.ToString() ?? "(null)");
                    return true;
                }
            }
        }

        _logger.LogInformation("[CommandRecognizer] Команда не распознана");
        commandType = default;
        filePath = null;
        taskNumber = null;
        return false;
    }

    /// <summary>
    /// Извлекает путь к файлу из текста команды с использованием регулярных выражений.
    /// Поддерживает паттерны: "из файла X", "from file X", "из X.md", "from X.md"
    /// Поддерживает любые имена файлов с расширением .md
    /// </summary>
    /// <param name="prompt">Текст промпта от пользователя</param>
    /// <returns>Извлеченный путь к файлу или null если путь не найден</returns>
    private string? ExtractFilePath(string prompt)
    {
        // Регулярные выражения для извлечения пути к файлу
        // Паттерны поддерживают любые имена файлов с расширением .md
        var patterns = new[]
        {
            @"из\s+файла\s+([^\s]+\.md)",           // "из файла tasks.md", "из файла feature-plan.md"
            @"из\s+([^\s]+\.md)",                    // "из tasks.md", "из my-tasks.md"
            @"from\s+file\s+([^\s]+\.md)",          // "from file tasks.md", "from file plan.md"
            @"from\s+([^\s]+\.md)",                  // "from tasks.md", "from feature.md"
            @"файла\s+([^\s]+\.md)",                 // "задачи из файла plan.md"
            @"file\s+([^\s]+\.md)"                   // "tasks from file plan.md"
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

    /// <summary>
    /// Извлекает номер задачи из текста команды с использованием регулярных выражений.
    /// Поддерживает паттерны: "задачу 1", "задачу номер 1", "task 1", "task number 1"
    /// </summary>
    /// <param name="prompt">Текст промпта от пользователя</param>
    /// <returns>Номер задачи или null если номер не найден</returns>
    private int? ExtractTaskNumber(string prompt)
    {
        // Регулярные выражения для извлечения номера задачи
        var patterns = new[]
        {
            @"задачу\s+(\d+)",                      // "выполни задачу 1"
            @"задачу\s+номер\s+(\d+)",              // "выполни задачу номер 1"
            @"задачи\s+(\d+)",                      // "выполни задачи 1"
            @"task\s+(\d+)",                        // "execute task 1"
            @"task\s+number\s+(\d+)",               // "execute task number 1"
            @"tasks\s+(\d+)"                        // "execute tasks 1"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(prompt, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var taskNumber))
            {
                return taskNumber;
            }
        }

        return null;
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public class PromptProcessor : IPromptProcessor
{
    private readonly RefactoringDbContext _dbContext;
    private readonly ILlmService _llmService;
    private readonly ISerenaService _serenaService;
    private readonly IDirectShellService _directShellService;
    private readonly IGitService _gitService;
    private readonly Lazy<ITaskExecutorService> _taskExecutorService;
    private readonly IProjectManagementService _projectService;
    private readonly ILogger<PromptProcessor> _logger;
    private readonly CommandRecognizer _commandRecognizer;
    private readonly TasksFilePathResolver _tasksFilePathResolver;

    public PromptProcessor(
        RefactoringDbContext dbContext,
        ILlmService llmService,
        ISerenaService serenaService,
        IDirectShellService directShellService,
        IGitService gitService,
        Lazy<ITaskExecutorService> taskExecutorService,
        IProjectManagementService projectService,
        ILogger<PromptProcessor> logger,
        CommandRecognizer commandRecognizer,
        TasksFilePathResolver tasksFilePathResolver)
    {
        _dbContext = dbContext;
        _llmService = llmService;
        _serenaService = serenaService;
        _directShellService = directShellService;
        _gitService = gitService;
        _taskExecutorService = taskExecutorService;
        _projectService = projectService;
        _logger = logger;
        _commandRecognizer = commandRecognizer;
        _tasksFilePathResolver = tasksFilePathResolver;
    }

    public async Task<string> ProcessPromptAsync(int dialogueId, string prompt)
    {
        // 1. Load dialogue and validate
        var dialogue = await _dbContext.Dialogues
            .Include(d => d.Messages)
            .Include(d => d.DialogueGroup) // Загружаем группу с контекстом
            .FirstOrDefaultAsync(d => d.Id == dialogueId);

        if (dialogue == null)
            throw new ArgumentException("Dialogue not found");
        
        // Получаем выбранный проект (приоритет над dialogue.ProjectPath)
        var selectedProject = await _projectService.GetSelectedProjectAsync();
        var projectPath = selectedProject?.Path ?? dialogue.ProjectPath;
        
        _logger.LogInformation(
            "ProcessPromptAsync: используется путь проекта: {ProjectPath} (выбранный проект: {SelectedProject})",
            projectPath,
            selectedProject?.Name ?? "нет");

        // 2. Save user message
        var userMessage = new Message
        {
            DialogueId = dialogueId,
            Role = "user",
            Content = prompt,
            Timestamp = DateTime.UtcNow
        };
        _dbContext.Messages.Add(userMessage);
        await _dbContext.SaveChangesAsync();

        // 3. Проверка на команду агентского режима
        _logger.LogInformation("=== НАЧАЛО ПРОВЕРКИ КОМАНДЫ ===");
        _logger.LogInformation("Проверка команды агентского режима для промпта: '{Prompt}'", prompt);
        _logger.LogInformation("Длина промпта: {Length}, Первые 50 символов: '{Preview}'", 
            prompt.Length, 
            prompt.Length > 50 ? prompt.Substring(0, 50) : prompt);
        
        var isRecognized = _commandRecognizer.TryRecognizeCommand(prompt, out var commandType, out var filePath, out var taskNumber);
        _logger.LogInformation("Результат распознавания: {IsRecognized}, CommandType: {CommandType}, FilePath: {FilePath}, TaskNumber: {TaskNumber}",
            isRecognized, commandType, filePath ?? "(null)", taskNumber?.ToString() ?? "(null)");
        _logger.LogInformation("=== КОНЕЦ ПРОВЕРКИ КОМАНДЫ ===");
        
        if (isRecognized)
        {
            _logger.LogInformation("Распознана команда агентского режима: {CommandType}, путь: {FilePath}, номер задачи: {TaskNumber}", 
                commandType, filePath ?? "(не указан)", taskNumber?.ToString() ?? "(не указан)");
            
            var commandResult = await ExecuteAgentCommandAsync(dialogueId, commandType, filePath, projectPath, taskNumber);
            
            // Сохраняем ответ ассистента
            var assistantMessage = new Message
            {
                DialogueId = dialogueId,
                Role = "assistant",
                Content = commandResult,
                Timestamp = DateTime.UtcNow
            };
            _dbContext.Messages.Add(assistantMessage);
            await _dbContext.SaveChangesAsync();
            
            return commandResult;
        }

        try
        {
            // 3. Get Serena tool definitions
            var toolDefinitions = GetSerenaToolDefinitions();

            // 4. Prepare system message with clear instructions
            var systemMessageContent = @"You are a helpful AI assistant for C# project refactoring on Windows.

CRITICAL: You MUST call the appropriate tools to perform actions. Never just describe what to do.

When the user asks you to do something:
1. Call the tool immediately
2. You can add explanation text along with the tool call

Available tools:
- execute_shell_command: Execute Windows commands
- read_file: Read file contents
- find_symbol: Find C# symbols
- replace_symbol_body: Modify code
- insert_before_symbol: Insert code
- find_referencing_symbols: Find usages

Respond in Russian, but call tools with English parameters.";

            // Добавляем контекст группы диалогов, если он есть
            if (dialogue.DialogueGroup != null)
            {
                var groupContext = new System.Text.StringBuilder();
                groupContext.AppendLine("\n\n=== КОНТЕКСТ ГРУППЫ ДИАЛОГОВ ===");
                
                if (!string.IsNullOrWhiteSpace(dialogue.DialogueGroup.Requirements))
                {
                    groupContext.AppendLine("\n--- ТРЕБОВАНИЯ (Requirements) ---");
                    groupContext.AppendLine(dialogue.DialogueGroup.Requirements);
                }
                
                if (!string.IsNullOrWhiteSpace(dialogue.DialogueGroup.Design))
                {
                    groupContext.AppendLine("\n--- ПРОЕКТИРОВАНИЕ (Design) ---");
                    groupContext.AppendLine(dialogue.DialogueGroup.Design);
                }
                
                if (!string.IsNullOrWhiteSpace(dialogue.DialogueGroup.Tasks))
                {
                    groupContext.AppendLine("\n--- ЗАДАЧИ (Tasks) ---");
                    groupContext.AppendLine(dialogue.DialogueGroup.Tasks);
                }
                
                groupContext.AppendLine("\n=== КОНЕЦ КОНТЕКСТА ГРУППЫ ===");
                groupContext.AppendLine("\nИспользуй этот контекст для понимания требований, архитектуры и задач проекта при выполнении запросов пользователя.");
                
                systemMessageContent += groupContext.ToString();
                
                _logger.LogInformation("Добавлен контекст группы '{GroupName}' в системный промпт", dialogue.DialogueGroup.Name);
            }

            var systemMessage = new Message
            {
                Role = "system",
                Content = systemMessageContent
            };

            // 5. Send prompt to LLM with system message
            var messagesWithSystem = new List<Message> { systemMessage };
            messagesWithSystem.AddRange(dialogue.Messages);

            var llmResponse = await _llmService.SendPromptAsync(
                prompt,
                messagesWithSystem,
                toolDefinitions
            );

            _logger.LogInformation("LLM response received. HasFunctionCalls: {HasFunctionCalls}, HasTextContent: {HasTextContent}, TextContent: {TextContent}", 
                llmResponse.FunctionCalls?.Any() ?? false, 
                !string.IsNullOrEmpty(llmResponse.TextContent),
                llmResponse.TextContent?.Substring(0, Math.Min(200, llmResponse.TextContent?.Length ?? 0)) ?? "(empty)");

            // 6. Execute function calls if present
            var resultBuilder = new StringBuilder();
            var hasExplanation = false;

            // Сначала добавляем рассуждения модели (если есть)
            if (!string.IsNullOrEmpty(llmResponse.TextContent))
            {
                _logger.LogInformation("LLM reasoning/explanation, length: {Length}", llmResponse.TextContent.Length);
                
                // Проверяем, не вернула ли модель текст вместо tool call
                if (llmResponse.FunctionCalls == null || !llmResponse.FunctionCalls.Any())
                {
                    // Пытаемся извлечь команду из текста
                    var extractedCall = TryExtractToolCallFromText(llmResponse.TextContent);
                    if (extractedCall != null)
                    {
                        _logger.LogWarning("Model returned text instead of tool call. Extracted: {FunctionName}", extractedCall.Name);
                        llmResponse.FunctionCalls = new List<FunctionCall> { extractedCall };
                    }
                }
                
                // Фильтруем технические сообщения
                var cleanedContent = CleanTechnicalJargon(llmResponse.TextContent);
                if (!string.IsNullOrWhiteSpace(cleanedContent))
                {
                    resultBuilder.AppendLine(cleanedContent);
                    resultBuilder.AppendLine();
                    hasExplanation = true;
                }
            }

            // Затем выполняем инструменты
            if (llmResponse.FunctionCalls?.Any() == true)
            {
                _logger.LogInformation("Processing {Count} function calls", llmResponse.FunctionCalls.Count);
                
                // Если модель не дала объяснения, генерируем его сами
                if (!hasExplanation)
                {
                    var contextMessage = GenerateContextMessage(llmResponse.FunctionCalls, prompt);
                    if (!string.IsNullOrEmpty(contextMessage))
                    {
                        resultBuilder.AppendLine(contextMessage);
                        resultBuilder.AppendLine();
                    }
                }
                
                foreach (var functionCall in llmResponse.FunctionCalls)
                {
                    _logger.LogInformation("Executing function: {FunctionName} with arguments: {Arguments}", 
                        functionCall.Name, 
                        JsonSerializer.Serialize(functionCall.Arguments));

                    // Convert Linux commands to Windows if needed
                    if (functionCall.Name == "execute_shell_command" && 
                        functionCall.Arguments.ContainsKey("command"))
                    {
                        var command = functionCall.Arguments["command"]?.ToString() ?? "";
                        var convertedCommand = ConvertLinuxCommandToWindows(command);
                        _logger.LogInformation("Command conversion: '{Original}' -> '{Converted}'", command, convertedCommand);
                        functionCall.Arguments["command"] = convertedCommand;
                    }

                    try
                    {
                        var result = await ExecuteFunctionCallAsync(functionCall, projectPath);
                        _logger.LogInformation("Function {FunctionName} executed successfully. Result length: {Length}", 
                            functionCall.Name, result?.Length ?? 0);
                        
                        // Показываем результат в понятном формате
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            // Не показываем технические детали, если результат короткий
                            if (result.Length < 100 && !result.Contains("\n"))
                            {
                                resultBuilder.AppendLine($"✅ {result}");
                            }
                            else if (result.Length > 500)
                            {
                                resultBuilder.AppendLine($"✅ Результат:");
                                resultBuilder.AppendLine($"   {result.Substring(0, 500)}...");
                                resultBuilder.AppendLine($"   (показано первые 500 символов из {result.Length})");
                            }
                            else
                            {
                                resultBuilder.AppendLine($"✅ {result}");
                            }
                        }
                        resultBuilder.AppendLine();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing function {FunctionName}", functionCall.Name);
                        resultBuilder.AppendLine($"❌ Ошибка: {ex.Message}");
                        resultBuilder.AppendLine();
                        hasExplanation = true; // Помечаем, что есть сообщение об ошибке
                    }
                }
                
                // Добавляем итоговое сообщение только если не было ошибок и модель не дала объяснения
                var hasErrors = resultBuilder.ToString().Contains("❌");
                if (!hasExplanation && !hasErrors)
                {
                    resultBuilder.AppendLine("Готово! Операция выполнена успешно.");
                }
            }
            else if (string.IsNullOrEmpty(llmResponse.TextContent))
            {
                // Fallback если нет ни function calls, ни text content
                _logger.LogWarning("LLM returned empty response for dialogue {DialogueId}", dialogueId);
                resultBuilder.AppendLine("Модель не вернула ответ. Попробуйте переформулировать запрос.");
            }

            var responseContent = resultBuilder.ToString().Trim();
            _logger.LogInformation("Saving assistant message, content length: {Length}", responseContent.Length);

            // 7. Save assistant response
            var assistantMessage = new Message
            {
                DialogueId = dialogueId,
                Role = "assistant",
                Content = responseContent,
                Timestamp = DateTime.UtcNow
            };
            _dbContext.Messages.Add(assistantMessage);
            await _dbContext.SaveChangesAsync();

            return assistantMessage.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing prompt for dialogue {DialogueId}", dialogueId);

            // Save error as assistant message
            var errorMessage = new Message
            {
                DialogueId = dialogueId,
                Role = "assistant",
                Content = $"Error: {ex.Message}",
                Timestamp = DateTime.UtcNow
            };
            _dbContext.Messages.Add(errorMessage);
            await _dbContext.SaveChangesAsync();

            throw;
        }
    }

    /// <summary>
    /// Выполняет команду управления агентским режимом выполнения задач
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="commandType">Тип команды</param>
    /// <param name="filePath">Путь к файлу tasks.md (может быть null)</param>
    /// <param name="projectPath">Путь к проекту</param>
    /// <param name="taskNumber">Номер конкретной задачи для выполнения (может быть null)</param>
    /// <returns>Сообщение с результатом выполнения команды</returns>
    private async Task<string> ExecuteAgentCommandAsync(
        int dialogueId,
        AgentCommandType commandType,
        string? filePath,
        string projectPath,
        int? taskNumber = null)
    {
        try
        {
            var taskExecutor = _taskExecutorService.Value;
            
            switch (commandType)
            {
                case AgentCommandType.StartExecution:
                    // Логируем параметры перед разрешением пути
                    _logger.LogInformation(
                        "Разрешение пути к файлу tasks.md: filePath={FilePath}, projectPath={ProjectPath}", 
                        filePath ?? "(null)", 
                        projectPath);
                    
                    // Разрешаем путь к файлу tasks.md
                    var resolvedPath = await _tasksFilePathResolver.ResolveTasksFilePathAsync(filePath, projectPath);
                    
                    if (taskNumber.HasValue)
                    {
                        _logger.LogInformation("Запуск выполнения задачи {TaskNumber} из файла: {FilePath}", taskNumber.Value, resolvedPath);
                        
                        // Запускаем выполнение конкретной задачи
                        var sessionId = await taskExecutor.ExecuteSpecificTaskAsync(dialogueId, resolvedPath, taskNumber.Value);
                        
                        return $"✅ Запущено выполнение задачи {taskNumber.Value} из файла {Path.GetFileName(resolvedPath)}\n\n" +
                               $"Следите за прогрессом в сообщениях ниже.";
                    }
                    else
                    {
                        _logger.LogInformation("Запуск выполнения всех задач из файла: {FilePath}", resolvedPath);
                        
                        // Запускаем выполнение всех задач
                        var sessionId = await taskExecutor.ExecuteTasksAsync(dialogueId, resolvedPath);
                        
                        return $"✅ Запущено выполнение всех задач из файла {Path.GetFileName(resolvedPath)}\n\n" +
                               $"Следите за прогрессом в сообщениях ниже. Вы можете остановить выполнение в любой момент.";
                    }

                case AgentCommandType.StopExecution:
                    _logger.LogInformation("Остановка выполнения задач для диалога {DialogueId}", dialogueId);
                    
                    await taskExecutor.StopExecutionAsync(dialogueId);
                    
                    return "⏸️ Выполнение задач остановлено.\n\n" +
                           "Вы можете возобновить выполнение командой \"продолжи выполнение\".";

                case AgentCommandType.ResumeExecution:
                    _logger.LogInformation("Возобновление выполнения задач для диалога {DialogueId}", dialogueId);
                    
                    await taskExecutor.ResumeExecutionAsync(dialogueId);
                    
                    return "▶️ Продолжаю выполнение задач...\n\n" +
                           "Следите за прогрессом в сообщениях ниже.";

                case AgentCommandType.ShowStatus:
                    _logger.LogInformation("Запрос статуса выполнения для диалога {DialogueId}", dialogueId);
                    
                    var status = await taskExecutor.GetExecutionStatusAsync(dialogueId);
                    
                    return FormatExecutionStatus(status);

                default:
                    return $"❌ Неизвестная команда: {commandType}";
            }
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Файл не найден при выполнении команды {CommandType}", commandType);
            return $"❌ {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Ошибка валидации при выполнении команды {CommandType}", commandType);
            return $"❌ {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выполнении команды {CommandType}", commandType);
            return $"❌ Ошибка выполнения команды: {ex.Message}\n\n" +
                   "Попробуйте позже или обратитесь к администратору.";
        }
    }

    /// <summary>
    /// Форматирует статус выполнения задач в читаемый вид
    /// </summary>
    /// <param name="status">Статус выполнения</param>
    /// <returns>Отформатированное сообщение со статусом</returns>
    private string FormatExecutionStatus(ExecutionStatusDto status)
    {
        var statusEmoji = status.Status switch
        {
            "running" => "🔄",
            "stopped" => "⏸️",
            "completed" => "✅",
            "failed" => "❌",
            _ => "ℹ️"
        };

        var statusText = status.Status switch
        {
            "running" => "Выполняется",
            "stopped" => "Приостановлено",
            "completed" => "Завершено",
            "failed" => "Ошибка",
            "none" => "Нет активного выполнения",
            _ => status.Status
        };

        var result = new StringBuilder();
        result.AppendLine($"{statusEmoji} **Статус выполнения:** {statusText}");
        result.AppendLine();

        if (!string.IsNullOrEmpty(status.Progress))
        {
            result.AppendLine($"📊 **Прогресс:** {status.Progress}");
        }

        if (!string.IsNullOrEmpty(status.CurrentTask))
        {
            result.AppendLine($"📝 **Текущая задача:** {status.CurrentTask}");
        }

        if (status.StartedAt.HasValue)
        {
            result.AppendLine($"🕐 **Начало:** {status.StartedAt.Value:HH:mm:ss}");
        }

        if (status.CompletedAt.HasValue)
        {
            result.AppendLine($"🏁 **Завершение:** {status.CompletedAt.Value:HH:mm:ss}");
            
            if (status.StartedAt.HasValue)
            {
                var duration = status.CompletedAt.Value - status.StartedAt.Value;
                result.AppendLine($"⏱️ **Длительность:** {duration:hh\\:mm\\:ss}");
            }
        }

        if (!string.IsNullOrEmpty(status.ErrorMessage))
        {
            result.AppendLine();
            result.AppendLine($"❌ **Ошибка:** {status.ErrorMessage}");
        }

        return result.ToString().Trim();
    }

    private async Task<string> ExecuteFunctionCallAsync(FunctionCall functionCall, string projectPath)
    {
        return functionCall.Name switch
        {
            "find_symbol" => await ExecuteFindSymbolAsync(functionCall.Arguments),
            "find_referencing_symbols" => await ExecuteFindReferencingSymbolsAsync(functionCall.Arguments),
            "replace_symbol_body" => await ExecuteReplaceSymbolBodyAsync(functionCall.Arguments),
            "execute_shell_command" => await ExecuteShellCommandAsync(functionCall.Arguments, projectPath),
            "read_file" => await ExecuteReadFileAsync(functionCall.Arguments),
            "insert_before_symbol" => await ExecuteInsertBeforeSymbolAsync(functionCall.Arguments),
            _ => throw new NotSupportedException($"Unknown function: {functionCall.Name}")
        };
    }

    private async Task<string> ExecuteFindSymbolAsync(Dictionary<string, object> arguments)
    {
        var symbolName = arguments.GetValueOrDefault("symbolName")?.ToString() ?? string.Empty;
        return await _serenaService.FindSymbolAsync(symbolName);
    }

    private async Task<string> ExecuteFindReferencingSymbolsAsync(Dictionary<string, object> arguments)
    {
        var symbolId = arguments.GetValueOrDefault("symbolId")?.ToString() ?? string.Empty;
        return await _serenaService.FindReferencingSymbolsAsync(symbolId);
    }

    private async Task<string> ExecuteReplaceSymbolBodyAsync(Dictionary<string, object> arguments)
    {
        var symbolId = arguments.GetValueOrDefault("symbolId")?.ToString() ?? string.Empty;
        var newBody = arguments.GetValueOrDefault("newBody")?.ToString() ?? string.Empty;
        return await _serenaService.ReplaceSymbolBodyAsync(symbolId, newBody);
    }

    private async Task<string> ExecuteShellCommandAsync(Dictionary<string, object> arguments, string projectPath)
    {
        var command = arguments.GetValueOrDefault("command")?.ToString() ?? string.Empty;
        return await _directShellService.ExecuteCommandAsync(command, projectPath);
    }

    private async Task<string> ExecuteReadFileAsync(Dictionary<string, object> arguments)
    {
        var filePath = arguments.GetValueOrDefault("filePath")?.ToString() ?? string.Empty;
        
        // Получаем текущий диалог для определения projectPath
        var dialogue = await _dbContext.Dialogues.FirstOrDefaultAsync();
        var projectPath = dialogue?.ProjectPath ?? Directory.GetCurrentDirectory();
        
        return await _directShellService.ReadFileAsync(filePath, projectPath);
    }

    private async Task<string> ExecuteInsertBeforeSymbolAsync(Dictionary<string, object> arguments)
    {
        var symbolId = arguments.GetValueOrDefault("symbolId")?.ToString() ?? string.Empty;
        var content = arguments.GetValueOrDefault("content")?.ToString() ?? string.Empty;
        return await _serenaService.InsertBeforeSymbolAsync(symbolId, content);
    }

    private List<FunctionDefinition> GetSerenaToolDefinitions()
    {
        return new List<FunctionDefinition>
        {
            new FunctionDefinition
            {
                Name = "find_symbol",
                Description = "Find a symbol (class, method, property) by name in the C# project",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["symbolName"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The name of the symbol to find"
                        }
                    },
                    ["required"] = new[] { "symbolName" }
                }
            },
            new FunctionDefinition
            {
                Name = "find_referencing_symbols",
                Description = "Find all references to a specific symbol",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["symbolId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the symbol to find references for"
                        }
                    },
                    ["required"] = new[] { "symbolId" }
                }
            },
            new FunctionDefinition
            {
                Name = "replace_symbol_body",
                Description = "Replace the body of a method or class",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["symbolId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the symbol to replace"
                        },
                        ["newBody"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The new body content"
                        }
                    },
                    ["required"] = new[] { "symbolId", "newBody" }
                }
            },
            new FunctionDefinition
            {
                Name = "execute_shell_command",
                Description = "Execute a Windows shell command in the project directory. Use this for file operations (create, delete, move files), running build commands, or any other shell operations. Examples: 'echo text > file.txt' to create a file, 'del file.txt' to delete a file, 'dotnet build' to build the project.",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["command"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The Windows shell command to execute (e.g., 'echo Hello > file.txt', 'del file.txt', 'dotnet build')"
                        }
                    },
                    ["required"] = new[] { "command" }
                }
            },
            new FunctionDefinition
            {
                Name = "read_file",
                Description = "Read the contents of an existing file from the project directory. Use this ONLY to read files that already exist. Provide just the filename (e.g., 'file.txt') or relative path (e.g., 'src/file.txt'), NOT absolute paths.",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["filePath"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The relative path to the file (e.g., 'file.txt' or 'src/file.txt'). Do NOT use absolute paths or placeholders like '/path/to/directory'."
                        }
                    },
                    ["required"] = new[] { "filePath" }
                }
            },
            new FunctionDefinition
            {
                Name = "insert_before_symbol",
                Description = "Insert content before a symbol",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["symbolId"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The ID of the symbol"
                        },
                        ["content"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The content to insert"
                        }
                    },
                    ["required"] = new[] { "symbolId", "content" }
                }
            }
        };
    }

    private string TruncatePrompt(string prompt, int maxLength)
    {
        if (prompt.Length <= maxLength)
            return prompt;

        return prompt.Substring(0, maxLength) + "...";
    }

    private string ConvertLinuxCommandToWindows(string command)
    {
        // Simple command conversion for common cases
        var conversions = new Dictionary<string, string>
        {
            { "ls", "dir" },
            { "ls -a", "dir /a" },
            { "ls -la", "dir /a" },
            { "ls -l", "dir" },
            { "cat ", "type " },
            { "cp ", "copy " },
            { "rm -rf ", "rmdir /s /q " },
            { "rm -f ", "del /f " },  // Добавлено: rm -f -> del /f
            { "rm ", "del " },
            { "mv ", "move " },
            { "pwd", "cd" },
            { "touch ", "type nul > " },
            { "clear", "cls" },
            { "grep ", "findstr " }
        };

        var lowerCommand = command.ToLower().TrimStart();

        foreach (var conversion in conversions)
        {
            if (lowerCommand.StartsWith(conversion.Key))
            {
                var converted = conversion.Value + command.Substring(conversion.Key.Length);
                _logger.LogInformation("Converted Linux command '{Original}' to Windows command '{Converted}'", 
                    command, converted);
                return converted;
            }
        }

        // If no conversion found, return original command
        return command;
    }

    private string GetFunctionDisplayName(string functionName)
    {
        return functionName switch
        {
            "execute_shell_command" => "Выполнение команды",
            "read_file" => "Чтение файла",
            "find_symbol" => "Поиск символа",
            "replace_symbol_body" => "Изменение кода",
            "insert_before_symbol" => "Вставка кода",
            "find_referencing_symbols" => "Поиск использований",
            _ => functionName
        };
    }

    private string CleanTechnicalJargon(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        // Удаляем технические фразы, которые модель может добавить
        var technicalPhrases = new[]
        {
            "Выполнено execute_shell_command:",
            "Выполнено read_file:",
            "Выполнено find_symbol:",
            "[вызываешь execute_shell_command",
            "[Call: execute_shell_command",
            "[Вызов: execute_shell_command",
            "execute_shell_command с {",
            "read_file с {",
            "find_symbol с {"
        };

        var cleaned = content;
        foreach (var phrase in technicalPhrases)
        {
            if (cleaned.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                // Если весь ответ - это техническая фраза, возвращаем пустую строку
                if (cleaned.Trim().StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }
                
                // Иначе удаляем фразу
                cleaned = cleaned.Replace(phrase, "", StringComparison.OrdinalIgnoreCase);
            }
        }

        return cleaned.Trim();
    }

    private string GenerateContextMessage(List<FunctionCall> functionCalls, string userPrompt)
    {
        if (functionCalls == null || !functionCalls.Any())
            return string.Empty;

        // Анализируем, что делает первый вызов функции
        var firstCall = functionCalls.First();

        return firstCall.Name switch
        {
            "execute_shell_command" => GenerateShellCommandMessage(firstCall, userPrompt),
            "read_file" => GenerateReadFileMessage(firstCall),
            "find_symbol" => GenerateFindSymbolMessage(firstCall),
            "replace_symbol_body" => "Изменяю код в указанном методе или классе...",
            "insert_before_symbol" => "Вставляю новый код в указанное место...",
            "find_referencing_symbols" => "Ищу все места использования указанного символа...",
            _ => "Выполняю запрошенную операцию..."
        };
    }

    private string ExtractFileNameFromCommand(string command)
    {
        try
        {
            // Извлекаем имя файла из команды типа "echo text > file.txt"
            var parts = command.Split('>');
            if (parts.Length >= 2)
            {
                return parts[1].Trim();
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга
        }
        return string.Empty;
    }

    private string GenerateShellCommandMessage(FunctionCall functionCall, string userPrompt)
    {
        if (!functionCall.Arguments.TryGetValue("command", out var commandObj))
            return "Выполняю команду...";

        var command = commandObj?.ToString() ?? "";
        
        // Определяем тип операции по команде
        if (command.Contains("del ", StringComparison.OrdinalIgnoreCase) || 
            command.Contains("rm ", StringComparison.OrdinalIgnoreCase))
        {
            // Извлекаем имена файлов из команды
            var files = ExtractFileNamesFromDeleteCommand(command);
            if (files.Any())
            {
                return $"Удаляю файл{(files.Count > 1 ? "ы" : "")}: {string.Join(", ", files)}...";
            }
            return "Удаляю указанные файлы...";
        }
        
        if (command.Contains("echo") && command.Contains(">"))
        {
            var fileName = ExtractFileNameFromCommand(command);
            if (!string.IsNullOrEmpty(fileName))
            {
                return $"Создаю файл {fileName}...";
            }
            return "Создаю новый файл...";
        }
        
        if (command.StartsWith("dir", StringComparison.OrdinalIgnoreCase) || 
            command.StartsWith("ls", StringComparison.OrdinalIgnoreCase))
        {
            return "Получаю список файлов в директории...";
        }
        
        if (command.StartsWith("type", StringComparison.OrdinalIgnoreCase) || 
            command.StartsWith("cat", StringComparison.OrdinalIgnoreCase))
        {
            return "Читаю содержимое файла...";
        }

        return "Выполняю команду...";
    }

    private List<string> ExtractFileNamesFromDeleteCommand(string command)
    {
        var files = new List<string>();
        try
        {
            // Удаляем команду del/rm и флаги
            var cleaned = command
                .Replace("del ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("rm ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("/q", "")
                .Replace("-f", "")
                .Trim();

            // Разделяем по пробелам и запятым
            var parts = cleaned.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            files.AddRange(parts.Where(p => p.Contains(".")));
        }
        catch
        {
            // Игнорируем ошибки парсинга
        }
        return files;
    }

    private string GenerateReadFileMessage(FunctionCall functionCall)
    {
        if (functionCall.Arguments.TryGetValue("filePath", out var filePathObj))
        {
            var filePath = filePathObj?.ToString() ?? "";
            if (!string.IsNullOrEmpty(filePath))
            {
                return $"Читаю содержимое файла {filePath}...";
            }
        }
        return "Читаю содержимое файла...";
    }

    private string GenerateFindSymbolMessage(FunctionCall functionCall)
    {
        if (functionCall.Arguments.TryGetValue("symbolName", out var symbolNameObj))
        {
            var symbolName = symbolNameObj?.ToString() ?? "";
            if (!string.IsNullOrEmpty(symbolName))
            {
                return $"Ищу символ {symbolName} в проекте...";
            }
        }
        return "Ищу указанный символ в проекте...";
    }

    private FunctionCall? TryExtractToolCallFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            // Ищем паттерны типа "Выполнено execute_shell_command: ..."
            if (text.Contains("execute_shell_command", StringComparison.OrdinalIgnoreCase))
            {
                // Пытаемся найти команду в тексте
                // Паттерны: "echo ...", "del ...", "dir", etc.
                var lowerText = text.ToLower();
                
                if (lowerText.Contains("echo") && lowerText.Contains(">"))
                {
                    // Извлекаем команду echo
                    var match = System.Text.RegularExpressions.Regex.Match(text, @"echo\s+(.+?)\s*>\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var content = match.Groups[1].Value.Trim();
                        var fileName = match.Groups[2].Value.Trim();
                        return new FunctionCall
                        {
                            Name = "execute_shell_command",
                            Arguments = new Dictionary<string, object>
                            {
                                ["command"] = $"echo {content} > {fileName}"
                            }
                        };
                    }
                }
                
                if (lowerText.Contains("del ") || lowerText.Contains("rm "))
                {
                    // Извлекаем команду удаления
                    var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:del|rm)\s+(.+?)(?:\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var files = match.Groups[1].Value.Trim();
                        return new FunctionCall
                        {
                            Name = "execute_shell_command",
                            Arguments = new Dictionary<string, object>
                            {
                                ["command"] = $"del {files}"
                            }
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract tool call from text");
        }

        return null;
    }
    
    /// <summary>
    /// Получает список доступных инструментов для LLM (публичный метод для StreamingService)
    /// </summary>
    public List<FunctionDefinition> GetAvailableTools()
    {
        return GetSerenaToolDefinitions();
    }
    
    /// <summary>
    /// Выполняет функцию по имени с заданными аргументами (публичный метод для StreamingService)
    /// </summary>
    public async Task<string> ExecuteFunctionAsync(string functionName, Dictionary<string, object> arguments, string projectPath)
    {
        // Конвертируем Linux команды в Windows если нужно
        if (functionName == "execute_shell_command" && arguments.ContainsKey("command"))
        {
            var command = arguments["command"]?.ToString() ?? "";
            var convertedCommand = ConvertLinuxCommandToWindows(command);
            arguments["command"] = convertedCommand;
        }
        
        var functionCall = new FunctionCall
        {
            Name = functionName,
            Arguments = arguments
        };
        
        return await ExecuteFunctionCallAsync(functionCall, projectPath);
    }
}

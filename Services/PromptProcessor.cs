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
    private readonly ILogger<PromptProcessor> _logger;

    public PromptProcessor(
        RefactoringDbContext dbContext,
        ILlmService llmService,
        ISerenaService serenaService,
        IDirectShellService directShellService,
        IGitService gitService,
        ILogger<PromptProcessor> logger)
    {
        _dbContext = dbContext;
        _llmService = llmService;
        _serenaService = serenaService;
        _directShellService = directShellService;
        _gitService = gitService;
        _logger = logger;
    }

    public async Task<string> ProcessPromptAsync(int dialogueId, string prompt)
    {
        // 1. Load dialogue and validate
        var dialogue = await _dbContext.Dialogues
            .Include(d => d.Messages)
            .FirstOrDefaultAsync(d => d.Id == dialogueId);

        if (dialogue == null)
            throw new ArgumentException("Dialogue not found");

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

        try
        {
            // 3. Get Serena tool definitions
            var toolDefinitions = GetSerenaToolDefinitions();

            // 4. Prepare system message with clear instructions
            var systemMessage = new Message
            {
                Role = "system",
                Content = @"You are a helpful AI assistant for C# project refactoring on Windows.

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

Respond in Russian, but call tools with English parameters."
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
                        var result = await ExecuteFunctionCallAsync(functionCall, dialogue.ProjectPath);
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
                    }
                }
                
                // Добавляем итоговое сообщение, если модель не дала объяснения
                if (!hasExplanation)
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
                Description = "Execute a shell command (e.g., dotnet build)",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["command"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The command to execute"
                        }
                    },
                    ["required"] = new[] { "command" }
                }
            },
            new FunctionDefinition
            {
                Name = "read_file",
                Description = "Read the contents of a file",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["filePath"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The path to the file to read"
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
            { "rm ", "del " },
            { "rm -rf ", "rmdir /s /q " },
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
}

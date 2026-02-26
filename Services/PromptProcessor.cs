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
    private readonly IGitService _gitService;
    private readonly ILogger<PromptProcessor> _logger;

    public PromptProcessor(
        RefactoringDbContext dbContext,
        ILlmService llmService,
        ISerenaService serenaService,
        IGitService gitService,
        ILogger<PromptProcessor> logger)
    {
        _dbContext = dbContext;
        _llmService = llmService;
        _serenaService = serenaService;
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
            // 3. Create checkpoint before LLM call
            var checkpointMessage = $"Checkpoint: {TruncatePrompt(prompt, 100)}";
            var commitHash = await _gitService.CreateCheckpointAsync(
                dialogue.ProjectPath,
                checkpointMessage
            );

            var checkpoint = new Checkpoint
            {
                DialogueId = dialogueId,
                CommitHash = commitHash,
                Description = checkpointMessage,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Checkpoints.Add(checkpoint);
            await _dbContext.SaveChangesAsync();

            // 4. Get Serena tool definitions
            var toolDefinitions = GetSerenaToolDefinitions();

            // 5. Prepare system message for Windows environment
            var systemMessage = new Message
            {
                Role = "system",
                Content = @"You are working on a Windows system. When executing shell commands:
- Use Windows commands (dir, type, copy, del, etc.) instead of Linux commands (ls, cat, cp, rm, etc.)
- Use backslashes (\) for paths, not forward slashes (/)
- Use PowerShell or CMD syntax
- Common command mappings:
  * ls → dir
  * cat → type
  * cp → copy
  * rm → del
  * mv → move
  * pwd → cd
  * touch → type nul >
  * mkdir → mkdir (same)
  * rm -rf → rmdir /s /q"
            };

            // 6. Send prompt to LLM with system message
            var messagesWithSystem = new List<Message> { systemMessage };
            messagesWithSystem.AddRange(dialogue.Messages);

            var llmResponse = await _llmService.SendPromptAsync(
                prompt,
                messagesWithSystem,
                toolDefinitions
            );

            // 6. Execute function calls if present
            var resultBuilder = new StringBuilder();

            if (llmResponse.FunctionCalls?.Any() == true)
            {
                foreach (var functionCall in llmResponse.FunctionCalls)
                {
                    // Convert Linux commands to Windows if needed
                    if (functionCall.Name == "mcp__serena__execute_shell_command" && 
                        functionCall.Arguments.ContainsKey("command"))
                    {
                        var command = functionCall.Arguments["command"]?.ToString() ?? "";
                        functionCall.Arguments["command"] = ConvertLinuxCommandToWindows(command);
                    }

                    var result = await ExecuteFunctionCallAsync(functionCall, dialogue.ProjectPath);
                    resultBuilder.AppendLine($"Executed {functionCall.Name}:");
                    resultBuilder.AppendLine(result);
                    resultBuilder.AppendLine();
                }
            }
            else if (!string.IsNullOrEmpty(llmResponse.TextContent))
            {
                resultBuilder.AppendLine(llmResponse.TextContent);
            }

            // 7. Save assistant response
            var assistantMessage = new Message
            {
                DialogueId = dialogueId,
                Role = "assistant",
                Content = resultBuilder.ToString(),
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
        return await _serenaService.ExecuteShellCommandAsync(command, projectPath);
    }

    private async Task<string> ExecuteReadFileAsync(Dictionary<string, object> arguments)
    {
        var filePath = arguments.GetValueOrDefault("filePath")?.ToString() ?? string.Empty;
        return await _serenaService.ReadFileAsync(filePath);
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
}

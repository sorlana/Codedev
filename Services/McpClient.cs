using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public class McpClient : IMcpClient
{
    private readonly string _command;
    private readonly string[] _args;
    private readonly ILogger<McpClient> _logger;

    public bool IsConnected => true; // Всегда "подключен" так как создаем процесс по требованию

    public McpClient(IConfiguration configuration, ILogger<McpClient> logger)
    {
        _logger = logger;
        _command = configuration["Serena:StdioCommand"] ?? "docker";
        var argsString = configuration["Serena:StdioArgs"];
        _args = argsString != null 
            ? JsonSerializer.Deserialize<string[]>(argsString) ?? Array.Empty<string>()
            : Array.Empty<string>();
    }

    public Task InitializeAsync()
    {
        // Инициализация не требуется - создаем процесс для каждого вызова
        _logger.LogInformation("MCP Client ready (process-per-call mode)");
        return Task.CompletedTask;
    }

    public async Task<McpResponse> CallToolAsync(string toolName, Dictionary<string, object> parameters)
    {
        try
        {
            _logger.LogInformation("Calling MCP tool: {ToolName}", toolName);

            // Создаем два JSON-RPC запроса: initialize и tool call
            var initRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "CSharpRefactoringAssistant",
                        version = "1.0.0"
                    }
                }
            };

            var toolRequest = new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments = parameters
                }
            };

            // Объединяем оба запроса в один ввод (каждый на отдельной строке)
            var input = JsonSerializer.Serialize(initRequest) + "\n" + JsonSerializer.Serialize(toolRequest);

            // Создаем процесс для этого вызова
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _command,
                Arguments = string.Join(" ", _args),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
                throw new McpException("Failed to start Serena process");

            // Отправляем оба запроса сразу
            await process.StandardInput.WriteAsync(input);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            // Читаем весь вывод с таймаутом
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            
            try
            {
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("MCP tool call timed out after 30 seconds: {ToolName}", toolName);
                process.Kill(true);
                throw new McpException($"MCP tool call timed out: {toolName}");
            }

            var output = await outputTask;
            var errorOutput = await errorTask;

            // Ждем завершения процесса с таймаутом
            if (!process.WaitForExit(5000))
            {
                _logger.LogWarning("Process did not exit in time, killing it");
                process.Kill(true);
            }

            _logger.LogDebug("Process exited with code: {ExitCode}", process.ExitCode);

            // Ищем JSON ответы в выводе (пропускаем INFO логи)
            var lines = output.Split('\n');
            string? toolResponse = null;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("{") && trimmed.Contains("\"id\":2"))
                {
                    toolResponse = trimmed;
                    break;
                }
            }

            if (toolResponse == null)
            {
                _logger.LogError("No tool response found. Output: {Output}", output.Length > 500 ? output.Substring(0, 500) : output);
                throw new McpException($"No response from tool: {toolName}");
            }

            _logger.LogDebug("Tool response received");

            // Парсим ответ
            var response = JsonSerializer.Deserialize<JsonElement>(toolResponse);
            
            if (response.TryGetProperty("error", out var error))
            {
                return new McpResponse
                {
                    IsSuccess = false,
                    Error = error.GetProperty("message").GetString()
                };
            }

            if (response.TryGetProperty("result", out var result))
            {
                return new McpResponse
                {
                    IsSuccess = true,
                    Result = result
                };
            }

            throw new McpException("Invalid response format from Serena");
        }
        catch (Exception ex) when (ex is not McpException)
        {
            _logger.LogError(ex, "Error calling MCP tool: {ToolName}", toolName);
            throw new McpException($"Error calling MCP tool: {toolName}", ex);
        }
    }

    public Task ShutdownAsync()
    {
        // Ничего не нужно делать - процессы создаются и завершаются для каждого вызова
        _logger.LogInformation("MCP Client shutdown (no-op in process-per-call mode)");
        return Task.CompletedTask;
    }
}

public class McpException : Exception
{
    public McpException(string message) : base(message) { }
    public McpException(string message, Exception innerException) : base(message, innerException) { }
}

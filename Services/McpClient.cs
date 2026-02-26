using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public class McpClient : IMcpClient
{
    private Process? _serenaProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private int _requestId = 0;
    private readonly string _command;
    private readonly string[] _args;
    private readonly ILogger<McpClient> _logger;

    public bool IsConnected => _serenaProcess != null && !_serenaProcess.HasExited;

    public McpClient(IConfiguration configuration, ILogger<McpClient> logger)
    {
        _logger = logger;
        _command = configuration["Serena:StdioCommand"] ?? "docker";
        var argsString = configuration["Serena:StdioArgs"];
        _args = argsString != null 
            ? JsonSerializer.Deserialize<string[]>(argsString) ?? Array.Empty<string>()
            : Array.Empty<string>();
    }

    public async Task InitializeAsync()
    {
        try
        {
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

            _serenaProcess = Process.Start(processStartInfo);
            if (_serenaProcess == null)
                throw new McpException("Failed to start Serena process");

            _stdin = _serenaProcess.StandardInput;
            _stdout = _serenaProcess.StandardOutput;

            _logger.LogInformation("MCP Client initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MCP Client");
            throw new McpException("Failed to initialize MCP Client", ex);
        }
    }

    public async Task<McpResponse> CallToolAsync(string toolName, Dictionary<string, object> parameters)
    {
        if (!IsConnected)
            throw new McpException("MCP Client is not connected");

        try
        {
            var requestId = Interlocked.Increment(ref _requestId);
            var request = new
            {
                jsonrpc = "2.0",
                id = requestId,
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments = parameters
                }
            };

            var requestJson = JsonSerializer.Serialize(request);
            await _stdin!.WriteLineAsync(requestJson);
            await _stdin.FlushAsync();

            var responseJson = await _stdout!.ReadLineAsync();
            if (string.IsNullOrEmpty(responseJson))
                throw new McpException("Empty response from Serena");

            var response = JsonSerializer.Deserialize<JsonElement>(responseJson);
            
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

    public async Task ShutdownAsync()
    {
        try
        {
            if (_stdin != null)
            {
                await _stdin.DisposeAsync();
                _stdin = null;
            }

            if (_stdout != null)
            {
                _stdout.Dispose();
                _stdout = null;
            }

            if (_serenaProcess != null && !_serenaProcess.HasExited)
            {
                _serenaProcess.Kill();
                await _serenaProcess.WaitForExitAsync();
                _serenaProcess.Dispose();
                _serenaProcess = null;
            }

            _logger.LogInformation("MCP Client shut down successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error shutting down MCP Client");
        }
    }
}

public class McpException : Exception
{
    public McpException(string message) : base(message) { }
    public McpException(string message, Exception innerException) : base(message, innerException) { }
}

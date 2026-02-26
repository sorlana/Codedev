using System.Text.Json;

namespace CSharpRefactoringAssistant.Services;

public class SerenaService : ISerenaService
{
    private readonly IMcpClient _mcpClient;
    private readonly ILogger<SerenaService> _logger;

    public SerenaService(IMcpClient mcpClient, ILogger<SerenaService> logger)
    {
        _mcpClient = mcpClient;
        _logger = logger;
    }

    public async Task<string> ActivateProjectAsync(string projectPath)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__activate_project",
                new Dictionary<string, object> { { "project", projectPath } }
            );

            if (!response.IsSuccess)
                throw new SerenaException($"Failed to activate project: {response.Error}");

            return response.Result?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error activating project: {ProjectPath}", projectPath);
            throw new SerenaException($"Error activating project: {projectPath}", ex);
        }
    }

    public async Task<string> FindSymbolAsync(string symbolName)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__find_symbol",
                new Dictionary<string, object> { { "name_path_pattern", symbolName } }
            );

            if (!response.IsSuccess)
                throw new SerenaException($"Failed to find symbol: {response.Error}");

            return JsonSerializer.Serialize(response.Result);
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error finding symbol: {SymbolName}", symbolName);
            throw new SerenaException($"Error finding symbol: {symbolName}", ex);
        }
    }

    public async Task<string> FindReferencingSymbolsAsync(string symbolId)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__find_referencing_symbols",
                new Dictionary<string, object> 
                { 
                    { "name_path", symbolId },
                    { "relative_path", "." }
                }
            );

            if (!response.IsSuccess)
                throw new SerenaException($"Failed to find referencing symbols: {response.Error}");

            return JsonSerializer.Serialize(response.Result);
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error finding referencing symbols: {SymbolId}", symbolId);
            throw new SerenaException($"Error finding referencing symbols: {symbolId}", ex);
        }
    }

    public async Task<string> ReplaceSymbolBodyAsync(string symbolId, string newBody)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__replace_symbol_body",
                new Dictionary<string, object> 
                { 
                    { "name_path", symbolId },
                    { "relative_path", "." },
                    { "body", newBody }
                }
            );

            if (!response.IsSuccess)
                throw new SerenaException($"Failed to replace symbol body: {response.Error}");

            return response.Result?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error replacing symbol body: {SymbolId}", symbolId);
            throw new SerenaException($"Error replacing symbol body: {symbolId}", ex);
        }
    }

    public async Task<string> ExecuteShellCommandAsync(string command, string workingDirectory)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__execute_shell_command",
                new Dictionary<string, object> 
                { 
                    { "command", command },
                    { "cwd", workingDirectory }
                }
            );

            if (!response.IsSuccess)
                throw new SerenaException($"Failed to execute shell command: {response.Error}");

            return JsonSerializer.Serialize(response.Result);
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error executing shell command: {Command}", command);
            throw new SerenaException($"Error executing shell command: {command}", ex);
        }
    }

    public async Task<string> ReadFileAsync(string filePath)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__read_file",
                new Dictionary<string, object> { { "relative_path", filePath } }
            );

            if (!response.IsSuccess)
            {
                var errorMsg = response.Error ?? "Unknown error";
                
                // Provide helpful error message
                if (errorMsg.Contains("not found") || errorMsg.Contains("does not exist"))
                {
                    throw new SerenaException($"Файл '{filePath}' не найден в проекте. Проверьте путь к файлу.");
                }
                
                throw new SerenaException($"Не удалось прочитать файл '{filePath}': {errorMsg}");
            }

            return response.Result?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error reading file: {FilePath}", filePath);
            throw new SerenaException($"Ошибка чтения файла '{filePath}': {ex.Message}", ex);
        }
    }

    public async Task<string> InsertBeforeSymbolAsync(string symbolId, string content)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__insert_before_symbol",
                new Dictionary<string, object> 
                { 
                    { "name_path", symbolId },
                    { "relative_path", "." },
                    { "body", content }
                }
            );

            if (!response.IsSuccess)
                throw new SerenaException($"Failed to insert before symbol: {response.Error}");

            return response.Result?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error inserting before symbol: {SymbolId}", symbolId);
            throw new SerenaException($"Error inserting before symbol: {symbolId}", ex);
        }
    }

    public async Task<string> DeleteLinesAsync(string filePath, int startLine, int endLine)
    {
        try
        {
            var response = await _mcpClient.CallToolAsync(
                "mcp__serena__replace_content",
                new Dictionary<string, object> 
                { 
                    { "relative_path", filePath },
                    { "needle", $"^.*$" },
                    { "repl", "" },
                    { "mode", "regex" }
                }
            );

            if (!response.IsSuccess)
                throw new SerenaException($"Failed to delete lines: {response.Error}");

            return response.Result?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not SerenaException)
        {
            _logger.LogError(ex, "Error deleting lines: {FilePath}", filePath);
            throw new SerenaException($"Error deleting lines: {filePath}", ex);
        }
    }
}

public class SerenaException : Exception
{
    public SerenaException(string message) : base(message) { }
    public SerenaException(string message, Exception innerException) : base(message, innerException) { }
}

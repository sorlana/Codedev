using System.Diagnostics;
using System.Text;

namespace CSharpRefactoringAssistant.Services;

public class DirectShellService : IDirectShellService
{
    private readonly ILogger<DirectShellService> _logger;

    public DirectShellService(ILogger<DirectShellService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExecuteCommandAsync(string command, string workingDirectory)
    {
        try
        {
            _logger.LogInformation("=== Shell Command Execution Start ===");
            _logger.LogInformation("Command: {Command}", command);
            _logger.LogInformation("Working Directory: {WorkingDirectory}", workingDirectory);
            
            // Проверяем, существует ли рабочая директория
            if (!Directory.Exists(workingDirectory))
            {
                _logger.LogWarning("Working directory does not exist: {WorkingDirectory}", workingDirectory);
                return $"Ошибка: рабочая директория не существует: {workingDirectory}";
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogDebug("STDOUT: {Data}", e.Data);
                    outputBuilder.AppendLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger.LogDebug("STDERR: {Data}", e.Data);
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString().Trim();

            _logger.LogInformation("Exit Code: {ExitCode}", process.ExitCode);
            _logger.LogInformation("Output: {Output}", string.IsNullOrEmpty(output) ? "(empty)" : output);
            _logger.LogInformation("Error: {Error}", string.IsNullOrEmpty(error) ? "(empty)" : error);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Command failed with exit code {ExitCode}", process.ExitCode);
                var result = $"Команда выполнена с кодом {process.ExitCode}";
                if (!string.IsNullOrEmpty(output))
                    result += $"\nВывод: {output}";
                if (!string.IsNullOrEmpty(error))
                    result += $"\nОшибка: {error}";
                
                _logger.LogInformation("=== Shell Command Execution End ===");
                return result;
            }

            _logger.LogInformation("Command executed successfully");
            _logger.LogInformation("=== Shell Command Execution End ===");
            
            // Для команд удаления файлов проверяем, удалился ли файл
            if ((command.Contains("del ", StringComparison.OrdinalIgnoreCase) || 
                 command.Contains("rm ", StringComparison.OrdinalIgnoreCase)) && 
                command.Contains("."))
            {
                var fileName = ExtractFileNameFromDeleteCommand(command);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var filePath = Path.Combine(workingDirectory, fileName);
                    _logger.LogInformation("Проверка удаления файла: {FilePath}", filePath);
                    
                    if (!File.Exists(filePath))
                    {
                        return $"Файл '{fileName}' успешно удален из директории {workingDirectory}";
                    }
                    else
                    {
                        return $"Команда выполнена, но файл '{fileName}' все еще существует в директории {workingDirectory}";
                    }
                }
            }
            
            // Для команд создания файлов проверяем, создался ли файл
            if (command.Contains(">") && command.Contains("."))
            {
                var fileName = ExtractFileNameFromCommand(command);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var filePath = Path.Combine(workingDirectory, fileName);
                    if (File.Exists(filePath))
                    {
                        return $"Файл '{fileName}' успешно создан в директории {workingDirectory}";
                    }
                    else
                    {
                        return $"Команда выполнена, но файл '{fileName}' не найден в директории {workingDirectory}";
                    }
                }
            }
            
            return string.IsNullOrEmpty(output) ? "Команда выполнена успешно" : output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Command}", command);
            throw new Exception($"Ошибка выполнения команды: {ex.Message}", ex);
        }
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
    
    private string ExtractFileNameFromDeleteCommand(string command)
    {
        try
        {
            // Удаляем команду del/rm и флаги
            var cleaned = command
                .Replace("del ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("rm ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("/f", "", StringComparison.OrdinalIgnoreCase)
                .Replace("/q", "", StringComparison.OrdinalIgnoreCase)
                .Replace("-f", "", StringComparison.OrdinalIgnoreCase)
                .Replace("-rf", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            // Берем первое слово (имя файла)
            var parts = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0].Contains("."))
            {
                return parts[0];
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга
        }
        return string.Empty;
    }

    public async Task<string> ReadFileAsync(string filePath, string workingDirectory)
    {
        try
        {
            var fullPath = Path.IsPathRooted(filePath) 
                ? filePath 
                : Path.Combine(workingDirectory, filePath);

            _logger.LogInformation("Reading file: {FilePath}", fullPath);

            if (!File.Exists(fullPath))
            {
                return $"Файл не найден: {filePath}";
            }

            var content = await File.ReadAllTextAsync(fullPath);
            _logger.LogInformation("File read successfully. Length: {Length}", content.Length);
            
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {FilePath}", filePath);
            return $"Ошибка чтения файла: {ex.Message}";
        }
    }
}

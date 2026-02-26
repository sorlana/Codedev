namespace CSharpRefactoringAssistant.Services;

public class PathValidator
{
    private readonly string? _allowedRootDirectory;
    private readonly ILogger<PathValidator> _logger;

    public PathValidator(IConfiguration configuration, ILogger<PathValidator> logger)
    {
        _allowedRootDirectory = configuration["Security:AllowedRootDirectory"];
        _logger = logger;
    }

    public bool ValidatePath(string path, out string? errorMessage)
    {
        errorMessage = null;

        // Check if path is absolute
        if (!Path.IsPathFullyQualified(path))
        {
            errorMessage = "Path must be absolute";
            _logger.LogWarning("Path validation failed: {Path} is not absolute", path);
            return false;
        }

        // Normalize path (resolve symbolic links and relative components)
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid path format: {ex.Message}";
            _logger.LogWarning(ex, "Path normalization failed for: {Path}", path);
            return false;
        }

        // Check if path exists
        if (!Directory.Exists(normalizedPath))
        {
            errorMessage = "Path does not exist or is not a directory";
            _logger.LogWarning("Path validation failed: {Path} does not exist", normalizedPath);
            return false;
        }

        // Check root directory restriction if configured
        if (!string.IsNullOrEmpty(_allowedRootDirectory))
        {
            var normalizedRoot = Path.GetFullPath(_allowedRootDirectory);
            if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Path is outside the allowed root directory";
                _logger.LogWarning("Path validation failed: {Path} is outside allowed root {Root}", 
                    normalizedPath, normalizedRoot);
                return false;
            }
        }

        return true;
    }

    public string SanitizePath(string path)
    {
        // Remove potentially dangerous characters
        var sanitized = path
            .Replace("../", "")
            .Replace("..\\", "")
            .Replace(";", "")
            .Replace("|", "")
            .Replace("&", "")
            .Replace(">", "")
            .Replace("<", "");

        return sanitized;
    }

    public string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to normalize path: {Path}", path);
            throw new PathValidationException($"Failed to normalize path: {path}", ex);
        }
    }
}

public class PathValidationException : Exception
{
    public PathValidationException(string message) : base(message) { }
    public PathValidationException(string message, Exception innerException) : base(message, innerException) { }
}

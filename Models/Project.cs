namespace CSharpRefactoringAssistant.Models;

/// <summary>
/// Represents a C# project managed by the application
/// </summary>
public class Project
{
    public int Id { get; set; }
    
    /// <summary>
    /// Display name for the project (derived from folder name)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full path to the project directory
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// When the project was added to the list
    /// </summary>
    public DateTime AddedAt { get; set; }
    
    /// <summary>
    /// Whether this project is currently selected
    /// </summary>
    public bool IsSelected { get; set; }
}

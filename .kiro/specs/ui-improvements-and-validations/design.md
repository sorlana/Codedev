# Design Document: UI Improvements and Validations

## Overview

This design document describes the implementation of three key UI improvements for the CSharpRefactoringAssistant application:

1. **Model Connection Validation at Startup**: Verify Ollama model connectivity when the application starts and display user-friendly error messages
2. **Project Management Interface**: Replace text input with a dropdown selector and modal window for managing multiple projects
3. **Manual Checkpoint Control**: Remove automatic checkpoint creation and provide user-controlled checkpoint management

The implementation will enhance user experience by providing better feedback, simplifying project management, and giving users control over checkpoint creation.

## Architecture

### System Components

The feature spans both backend (C# ASP.NET) and frontend (JavaScript) layers:

**Backend Components:**
- `IStartupValidationService`: New service interface for startup validation logic
- `StartupValidationService`: Implementation of model connection validation
- `IProjectManagementService`: New service interface for project list management
- `ProjectManagementService`: Implementation of project CRUD operations
- API endpoints for project management operations

**Frontend Components:**
- Startup validation UI notification system
- Project selector dropdown component
- Project management modal window
- Manual checkpoint button and controls
- Updated checkpoint creation logic

**Data Layer:**
- New `Project` entity for storing project information
- Database migration to add Projects table
- Configuration storage for selected project

### Technology Stack

- **Backend**: C# ASP.NET Core 10.0, Entity Framework Core, SQLite
- **Frontend**: Vanilla JavaScript, HTML5, CSS3
- **Database**: SQLite with Entity Framework Core
- **Configuration**: JSON-based configuration (appsettings.json)

## Components and Interfaces

### 1. Model Connection Validation

#### Backend Service Interface

```csharp
namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Service for validating system state at application startup
/// </summary>
public interface IStartupValidationService
{
    /// <summary>
    /// Validates connection to the configured LLM model
    /// </summary>
    /// <returns>Validation result with connection status and error message if applicable</returns>
    Task<ModelConnectionResult> ValidateModelConnectionAsync();
}

public class ModelConnectionResult
{
    public bool IsConnected { get; set; }
    public string? ModelName { get; set; }
    public string? ErrorMessage { get; set; }
}
```

#### Backend Service Implementation

```csharp
namespace CSharpRefactoringAssistant.Services;

public class StartupValidationService : IStartupValidationService
{
    private readonly IConfigurationService _configService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StartupValidationService> _logger;

    public StartupValidationService(
        IConfigurationService configService,
        IHttpClientFactory httpClientFactory,
        ILogger<StartupValidationService> logger)
    {
        _configService = configService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ModelConnectionResult> ValidateModelConnectionAsync()
    {
        var config = await _configService.GetConfigurationAsync();
        
        // Only validate Ollama connections (OpenAI is cloud-based)
        if (!config.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelConnectionResult { IsConnected = true };
        }

        if (config.Ollama == null || string.IsNullOrWhiteSpace(config.Ollama.Model))
        {
            return new ModelConnectionResult
            {
                IsConnected = false,
                ErrorMessage = "Модель не настроена"
            };
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            var response = await httpClient.GetAsync($"{config.Ollama.BaseUrl}/api/tags");
            
            if (!response.IsSuccessStatusCode)
            {
                return new ModelConnectionResult
                {
                    IsConnected = false,
                    ModelName = config.Ollama.Model,
                    ErrorMessage = $"Нет подключения к модели {config.Ollama.Model}, запустите модель в Ollama"
                };
            }

            return new ModelConnectionResult
            {
                IsConnected = true,
                ModelName = config.Ollama.Model
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Ollama");
            return new ModelConnectionResult
            {
                IsConnected = false,
                ModelName = config.Ollama.Model,
                ErrorMessage = $"Нет подключения к модели {config.Ollama.Model}, запустите модель в Ollama"
            };
        }
    }
}
```

#### API Endpoint

```csharp
// Add to Program.cs
app.MapGet("/api/startup/validate", async (IStartupValidationService validationService) =>
{
    var result = await validationService.ValidateModelConnectionAsync();
    return Results.Ok(result);
});
```

#### Frontend Implementation

```javascript
// Add to app.js
async function validateModelConnection() {
    try {
        const response = await fetch(`${API_BASE}/api/startup/validate`);
        const result = await response.json();
        
        if (!result.isConnected && result.errorMessage) {
            showStartupWarning(result.errorMessage);
        }
    } catch (error) {
        console.error('Error validating model connection:', error);
    }
}

function showStartupWarning(message) {
    const warningDiv = document.createElement('div');
    warningDiv.className = 'startup-warning';
    warningDiv.innerHTML = `
        <div class="warning-content">
            <span class="warning-icon">⚠️</span>
            <span class="warning-message">${escapeHtml(message)}</span>
            <button class="warning-close" onclick="this.parentElement.parentElement.remove()">✕</button>
        </div>
    `;
    document.body.insertBefore(warningDiv, document.body.firstChild);
}

// Call on page load
document.addEventListener('DOMContentLoaded', () => {
    validateModelConnection();
    loadDialogues();
    setupEventListeners();
    loadSavedProjectPath();
});
```

### 2. Project Management Interface

#### Data Model

```csharp
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
```

#### Database Context Update

```csharp
// Add to RefactoringDbContext.cs
public DbSet<Project> Projects { get; set; }

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    
    // Existing configurations...
    
    modelBuilder.Entity<Project>(entity =>
    {
        entity.HasKey(p => p.Id);
        entity.Property(p => p.Name).IsRequired().HasMaxLength(255);
        entity.Property(p => p.Path).IsRequired().HasMaxLength(1000);
        entity.HasIndex(p => p.Path).IsUnique();
    });
}
```

#### Backend Service Interface

```csharp
namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Service for managing the list of C# projects
/// </summary>
public interface IProjectManagementService
{
    /// <summary>
    /// Gets all projects in the list
    /// </summary>
    Task<List<Project>> GetAllProjectsAsync();
    
    /// <summary>
    /// Gets the currently selected project
    /// </summary>
    Task<Project?> GetSelectedProjectAsync();
    
    /// <summary>
    /// Adds a new project to the list
    /// </summary>
    Task<Project> AddProjectAsync(string projectPath);
    
    /// <summary>
    /// Removes a project from the list
    /// </summary>
    Task DeleteProjectAsync(int projectId);
    
    /// <summary>
    /// Sets a project as the selected one
    /// </summary>
    Task SelectProjectAsync(int projectId);
}
```

#### Backend Service Implementation

```csharp
namespace CSharpRefactoringAssistant.Services;

public class ProjectManagementService : IProjectManagementService
{
    private readonly RefactoringDbContext _dbContext;
    private readonly PathValidator _pathValidator;
    private readonly ILogger<ProjectManagementService> _logger;

    public ProjectManagementService(
        RefactoringDbContext dbContext,
        PathValidator pathValidator,
        ILogger<ProjectManagementService> logger)
    {
        _dbContext = dbContext;
        _pathValidator = pathValidator;
        _logger = logger;
    }

    public async Task<List<Project>> GetAllProjectsAsync()
    {
        return await _dbContext.Projects
            .OrderByDescending(p => p.IsSelected)
            .ThenByDescending(p => p.AddedAt)
            .ToListAsync();
    }

    public async Task<Project?> GetSelectedProjectAsync()
    {
        return await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.IsSelected);
    }

    public async Task<Project> AddProjectAsync(string projectPath)
    {
        // Validate path
        if (!_pathValidator.ValidatePath(projectPath, out var errorMessage))
        {
            throw new ArgumentException(errorMessage);
        }

        // Check if project already exists
        var existing = await _dbContext.Projects
            .FirstOrDefaultAsync(p => p.Path == projectPath);
        
        if (existing != null)
        {
            throw new InvalidOperationException("Проект уже добавлен в список");
        }

        // Extract project name from path
        var projectName = System.IO.Path.GetFileName(projectPath.TrimEnd('\\', '/'));

        var project = new Project
        {
            Name = projectName,
            Path = projectPath,
            AddedAt = DateTime.UtcNow,
            IsSelected = false
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Added project: {ProjectName} at {ProjectPath}", projectName, projectPath);

        return project;
    }

    public async Task DeleteProjectAsync(int projectId)
    {
        var project = await _dbContext.Projects.FindAsync(projectId);
        
        if (project == null)
        {
            throw new ArgumentException("Проект не найден");
        }

        _dbContext.Projects.Remove(project);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Deleted project: {ProjectName}", project.Name);
    }

    public async Task SelectProjectAsync(int projectId)
    {
        // Deselect all projects
        var allProjects = await _dbContext.Projects.ToListAsync();
        foreach (var p in allProjects)
        {
            p.IsSelected = false;
        }

        // Select the specified project
        var project = allProjects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
        {
            throw new ArgumentException("Проект не найден");
        }

        project.IsSelected = true;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Selected project: {ProjectName}", project.Name);
    }
}
```

#### API Endpoints

```csharp
// Add to Program.cs

// Get all projects
app.MapGet("/api/projects", async (IProjectManagementService projectService) =>
{
    var projects = await projectService.GetAllProjectsAsync();
    return Results.Ok(projects);
});

// Get selected project
app.MapGet("/api/projects/selected", async (IProjectManagementService projectService) =>
{
    var project = await projectService.GetSelectedProjectAsync();
    return project != null ? Results.Ok(project) : Results.NotFound();
});

// Add project
app.MapPost("/api/projects", async (
    AddProjectRequest request,
    IProjectManagementService projectService) =>
{
    try
    {
        var project = await projectService.AddProjectAsync(request.ProjectPath);
        return Results.Ok(project);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

// Delete project
app.MapDelete("/api/projects/{id}", async (
    int id,
    IProjectManagementService projectService) =>
{
    try
    {
        await projectService.DeleteProjectAsync(id);
        return Results.Ok(new { message = "Проект удален" });
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(ex.Message);
    }
});

// Select project
app.MapPost("/api/projects/{id}/select", async (
    int id,
    IProjectManagementService projectService) =>
{
    try
    {
        await projectService.SelectProjectAsync(id);
        return Results.Ok(new { message = "Проект выбран" });
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(ex.Message);
    }
});

// Add request model to RequestModels.cs
public record AddProjectRequest(string ProjectPath);
```

#### Frontend Implementation

```javascript
// Add to app.js

let projects = [];

async function loadProjects() {
    try {
        const response = await fetch(`${API_BASE}/api/projects`);
        projects = await response.json();
        
        updateProjectSelector();
    } catch (error) {
        console.error('Error loading projects:', error);
    }
}

function updateProjectSelector() {
    const selector = document.getElementById('project-selector');
    
    if (projects.length === 0) {
        selector.innerHTML = '<option value="">Нет проектов</option>';
        return;
    }
    
    selector.innerHTML = projects.map(p => `
        <option value="${p.id}" ${p.isSelected ? 'selected' : ''}>
            ${escapeHtml(p.name)}
        </option>
    `).join('');
}

async function selectProject(projectId) {
    try {
        await fetch(`${API_BASE}/api/projects/${projectId}/select`, {
            method: 'POST'
        });
        
        await loadProjects();
    } catch (error) {
        console.error('Error selecting project:', error);
    }
}

function openProjectModal() {
    const modal = document.getElementById('project-modal');
    modal.classList.add('active');
    renderProjectList();
}

function closeProjectModal() {
    const modal = document.getElementById('project-modal');
    modal.classList.remove('active');
}

function renderProjectList() {
    const listElement = document.getElementById('modal-project-list');
    
    if (projects.length === 0) {
        listElement.innerHTML = '<div class="empty-state">Нет проектов</div>';
        return;
    }
    
    listElement.innerHTML = projects.map(p => `
        <div class="project-list-item">
            <span class="project-name">${escapeHtml(p.name)}</span>
            <button class="delete-project-btn" onclick="deleteProject(${p.id})">
                🗑️
            </button>
        </div>
    `).join('');
}

async function addProject() {
    // This will be implemented using native folder picker
    // For now, show input dialog
    const path = prompt('Введите путь к проекту:');
    
    if (!path) return;
    
    try {
        const response = await fetch(`${API_BASE}/api/projects`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ projectPath: path })
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        await loadProjects();
        renderProjectList();
    } catch (error) {
        alert('Ошибка добавления проекта: ' + error.message);
    }
}

async function deleteProject(projectId) {
    if (!confirm('Удалить проект из списка?')) {
        return;
    }
    
    try {
        await fetch(`${API_BASE}/api/projects/${projectId}`, {
            method: 'DELETE'
        });
        
        await loadProjects();
        renderProjectList();
    } catch (error) {
        alert('Ошибка удаления проекта: ' + error.message);
    }
}
```

#### HTML Updates

```html
<!-- Replace project path input in index.html -->
<div id="sidebar-header">
    <h1>
        Диалоги
        <button class="help-button" onclick="showHelp()" title="Помощь">❓</button>
    </h1>
    <div id="new-dialogue-form">
        <div class="project-selector-container">
            <select id="project-selector" onchange="selectProject(this.value)">
                <option value="">Выберите проект</option>
            </select>
            <button id="open-project-modal-btn" onclick="openProjectModal()" title="Управление проектами">
                📁
            </button>
        </div>
        <div class="button-row">
            <button id="create-dialogue-button">Создать диалог</button>
            <button id="open-config-button">⚙️</button>
        </div>
    </div>
</div>

<!-- Add project management modal -->
<div id="project-modal-overlay">
    <div id="project-modal">
        <div class="modal-header">
            <h2>Управление проектами</h2>
            <button class="close-modal" onclick="closeProjectModal()">✕</button>
        </div>
        <div class="modal-body">
            <div id="modal-project-list"></div>
            <button id="add-project-btn" onclick="addProject()">Добавить</button>
        </div>
    </div>
</div>
```

### 3. Manual Checkpoint Management

#### Backend Changes

Remove automatic checkpoint creation from `PromptProcessor.cs`:

```csharp
// In PromptProcessor.cs - Remove automatic checkpoint creation
public async Task<string> ProcessPromptAsync(int dialogueId, string userPrompt)
{
    // ... existing code ...
    
    // REMOVE: Automatic checkpoint creation
    // await CreateCheckpointAsync(dialogue, "Auto checkpoint");
    
    // ... rest of the code ...
}
```

Add manual checkpoint creation endpoint:

```csharp
// Add to Program.cs
app.MapPost("/api/dialogues/{id}/checkpoints", async (
    int id,
    CreateCheckpointRequest request,
    RefactoringDbContext dbContext,
    IGitService gitService) =>
{
    var dialogue = await dbContext.Dialogues.FindAsync(id);
    if (dialogue == null)
        return Results.NotFound("Dialogue not found");

    try
    {
        // Create checkpoint
        var commitHash = await gitService.CreateCheckpointAsync(
            dialogue.ProjectPath,
            request.Description ?? "Manual checkpoint");

        var checkpoint = new Checkpoint
        {
            DialogueId = id,
            Description = request.Description ?? "Manual checkpoint",
            CommitHash = commitHash,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Checkpoints.Add(checkpoint);
        await dbContext.SaveChangesAsync();

        return Results.Ok(checkpoint);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error creating checkpoint");
        return Results.Problem(ex.Message);
    }
});

// Add request model to RequestModels.cs
public record CreateCheckpointRequest(string? Description);
```

#### Frontend Implementation

```javascript
// Add to app.js

async function createManualCheckpoint() {
    if (!currentDialogueId) {
        alert('Выберите диалог');
        return;
    }
    
    const description = prompt('Описание чекпойнта (необязательно):');
    
    // User cancelled
    if (description === null) {
        return;
    }
    
    try {
        const response = await fetch(
            `${API_BASE}/api/dialogues/${currentDialogueId}/checkpoints`,
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ 
                    description: description || 'Manual checkpoint' 
                })
            }
        );
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        await loadCheckpoints(currentDialogueId);
        showStatusMessage('Чекпойнт создан', 'success');
    } catch (error) {
        console.error('Error creating checkpoint:', error);
        alert('Ошибка создания чекпойнта: ' + error.message);
    }
}

function showStatusMessage(message, type) {
    // Simple toast notification
    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;
    toast.textContent = message;
    document.body.appendChild(toast);
    
    setTimeout(() => {
        toast.remove();
    }, 3000);
}
```

#### HTML Updates

```html
<!-- Update checkpoint panel in index.html -->
<div id="checkpoint-panel">
    <div class="checkpoint-header">
        <h2>Чекпоинты</h2>
        <button id="create-checkpoint-btn" onclick="createManualCheckpoint()" title="Добавить чекпойнт">
            ➕
        </button>
    </div>
    <div id="checkpoint-list"></div>
</div>
```

## Data Models

### Project Entity

```csharp
public class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public bool IsSelected { get; set; }
}
```

### Request/Response Models

```csharp
// Add to RequestModels.cs
public record AddProjectRequest(string ProjectPath);
public record CreateCheckpointRequest(string? Description);

// Add to ConfigurationModels.cs or create new file
public class ModelConnectionResult
{
    public bool IsConnected { get; set; }
    public string? ModelName { get; set; }
    public string? ErrorMessage { get; set; }
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system—essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*


### Property 1: Model Connection Validation Invocation
*For any* application startup with a configured Ollama model, the model connection validation service should be invoked to check connectivity.
**Validates: Requirements 1.1**

### Property 2: Unreachable Model Error Message Format
*For any* unreachable Ollama model configuration, the validation result should contain an error message in the format "Нет подключения к модели {model_name}, запустите модель в Ollama".
**Validates: Requirements 1.2**

### Property 3: Connection Status Logging
*For any* model connection check completion, a log entry should be created containing the connection status (success or failure).
**Validates: Requirements 1.3**

### Property 4: Modal Display on Button Click
*For any* application state where the folder icon button is clicked, the project management modal window should become visible.
**Validates: Requirements 2.3**

### Property 5: Project List Rendering
*For any* list of projects displayed in the modal window, each project should be rendered with its name and a delete button.
**Validates: Requirements 2.4**

### Property 6: Folder Dialog Trigger
*For any* state where the "Добавить" button in the project modal is clicked, a folder selection dialog should be triggered.
**Validates: Requirements 2.6**

### Property 7: Project Addition
*For any* valid folder path selected by the user, adding it should result in a new project appearing in the project list.
**Validates: Requirements 2.7**

### Property 8: Project Display Consistency
*For any* newly added project, it should appear in both the modal window project list and the project selector dropdown.
**Validates: Requirements 2.8**

### Property 9: Project Deletion
*For any* project in the project list, clicking its delete button should remove it from the list.
**Validates: Requirements 2.9**

### Property 10: Project Selection Persistence Round-Trip
*For any* project selected from the project selector, after saving and restarting the application, that same project should be displayed as the default selection.
**Validates: Requirements 2.10, 2.11**

### Property 11: No Automatic Checkpoints
*For any* file change or prompt submission in a dialogue, no automatic checkpoint should be created.
**Validates: Requirements 3.1, 3.2**

### Property 12: Manual Checkpoint Creation
*For any* dialogue state where the "Добавить чекпойнт" button is clicked, a new checkpoint should be created for the current project state.
**Validates: Requirements 3.4**

### Property 13: Checkpoint Creation Confirmation
*For any* successful checkpoint creation, a confirmation message should be displayed to the user.
**Validates: Requirements 3.6**

## Error Handling

### Model Connection Validation Errors

**Scenario**: Ollama service is not running
- **Handling**: Display warning message to user, log the error, allow application to continue
- **User Action**: Start Ollama service and refresh

**Scenario**: Invalid model configuration
- **Handling**: Display warning message, log the error, allow application to continue
- **User Action**: Update configuration in settings modal

### Project Management Errors

**Scenario**: Invalid project path
- **Handling**: Return 400 Bad Request with validation error message
- **User Action**: Provide valid project path

**Scenario**: Duplicate project path
- **Handling**: Return 400 Bad Request with "Проект уже добавлен в список" message
- **User Action**: Select existing project or provide different path

**Scenario**: Project not found for deletion/selection
- **Handling**: Return 404 Not Found with error message
- **User Action**: Refresh project list

**Scenario**: Database error during project operations
- **Handling**: Return 500 Internal Server Error, log detailed error
- **User Action**: Retry operation, check database connectivity

### Checkpoint Creation Errors

**Scenario**: Git repository not initialized
- **Handling**: Return 400 Bad Request with error message
- **User Action**: Ensure dialogue was created properly (Git should be initialized automatically)

**Scenario**: Uncommitted changes during checkpoint creation
- **Handling**: Allow checkpoint creation (Git will commit the changes)
- **User Action**: None required

**Scenario**: Git operation failure
- **Handling**: Return 500 Internal Server Error with error details, log error
- **User Action**: Check Git installation and repository state

## Testing Strategy

### Dual Testing Approach

This feature requires both unit tests and property-based tests for comprehensive coverage:

**Unit Tests** focus on:
- Specific examples of model connection validation responses
- Edge cases like empty project lists, invalid paths
- Error conditions and exception handling
- Integration between frontend and backend components
- Database operations for project management

**Property-Based Tests** focus on:
- Universal properties that hold for all inputs
- Model connection validation across various configurations
- Project CRUD operations with random project data
- Checkpoint creation behavior across different dialogue states
- UI consistency properties

### Property-Based Testing Configuration

- **Library**: For C# backend, use FsCheck or CsCheck
- **Library**: For JavaScript frontend, use fast-check
- **Iterations**: Minimum 100 iterations per property test
- **Tagging**: Each test must reference its design property

Example tag format:
```csharp
// Feature: ui-improvements-and-validations, Property 2: Unreachable Model Error Message Format
[Property]
public Property UnreachableModelReturnsCorrectErrorMessage() { ... }
```

### Unit Testing Focus Areas

**Model Connection Validation:**
- Test with Ollama running and model available
- Test with Ollama not running
- Test with invalid model name
- Test with OpenAI provider (should skip validation)

**Project Management:**
- Test adding valid project path
- Test adding duplicate project path
- Test deleting existing project
- Test deleting non-existent project
- Test selecting project
- Test loading projects on startup

**Manual Checkpoint Creation:**
- Test creating checkpoint with description
- Test creating checkpoint without description
- Test creating checkpoint when no dialogue is selected
- Test checkpoint appears in list after creation

**Frontend UI:**
- Test modal opens and closes correctly
- Test project selector updates when projects change
- Test checkpoint button is visible
- Test startup warning displays correctly

### Integration Testing

**End-to-End Scenarios:**
1. Start application → Verify model connection warning if Ollama is down
2. Add project → Select project → Create dialogue → Verify project path is used
3. Create checkpoint manually → Verify it appears in list → Rollback → Verify state restored
4. Add multiple projects → Delete one → Verify it's removed from both selector and modal
5. Select project → Restart application → Verify selection persists

### Test Data Generation

For property-based tests, generate:
- Random project paths (valid and invalid)
- Random project names
- Random checkpoint descriptions
- Various model configurations (Ollama, OpenAI, invalid)
- Different application states (with/without selected project, with/without dialogues)

## CSS Styling Requirements

### Startup Warning Styles

```css
.startup-warning {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    background: #fff3cd;
    border-bottom: 2px solid #ffc107;
    padding: 12px 20px;
    z-index: 2000;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.warning-content {
    display: flex;
    align-items: center;
    gap: 12px;
    max-width: 1200px;
    margin: 0 auto;
}

.warning-icon {
    font-size: 20px;
}

.warning-message {
    flex: 1;
    color: #856404;
    font-weight: 500;
}

.warning-close {
    background: none;
    border: none;
    color: #856404;
    cursor: pointer;
    font-size: 20px;
    padding: 4px;
}

.warning-close:hover {
    color: #533f03;
}
```

### Project Selector Styles

```css
.project-selector-container {
    display: flex;
    gap: 8px;
    align-items: center;
}

#project-selector {
    flex: 1;
    padding: 8px;
    border: 1px solid #ddd;
    border-radius: 4px;
    font-size: 14px;
}

#open-project-modal-btn {
    padding: 8px 12px;
    background: #6c757d;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 16px;
}

#open-project-modal-btn:hover {
    background: #5a6268;
}
```

### Project Modal Styles

```css
#project-modal-overlay {
    display: none;
    position: fixed;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    background: rgba(0, 0, 0, 0.5);
    z-index: 1000;
    align-items: center;
    justify-content: center;
}

#project-modal-overlay.active {
    display: flex;
}

#project-modal {
    background: white;
    border-radius: 8px;
    width: 90%;
    max-width: 500px;
    max-height: 70vh;
    display: flex;
    flex-direction: column;
    box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
}

.modal-header {
    padding: 20px;
    border-bottom: 1px solid #ddd;
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.modal-body {
    padding: 20px;
    overflow-y: auto;
    flex: 1;
}

#modal-project-list {
    margin-bottom: 20px;
}

.project-list-item {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 12px;
    background: #f8f9fa;
    border-radius: 4px;
    margin-bottom: 8px;
}

.project-name {
    flex: 1;
    font-size: 14px;
}

.delete-project-btn {
    background: transparent;
    border: none;
    color: #dc3545;
    cursor: pointer;
    font-size: 16px;
    padding: 4px 8px;
}

.delete-project-btn:hover {
    background: #dc3545;
    color: white;
    border-radius: 4px;
}

#add-project-btn {
    width: 100%;
    padding: 10px;
    background: #007bff;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 14px;
}

#add-project-btn:hover {
    background: #0056b3;
}
```

### Checkpoint Button Styles

```css
.checkpoint-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 15px;
}

.checkpoint-header h2 {
    font-size: 16px;
    margin: 0;
}

#create-checkpoint-btn {
    background: #28a745;
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-size: 16px;
    padding: 6px 10px;
}

#create-checkpoint-btn:hover {
    background: #218838;
}

.toast {
    position: fixed;
    bottom: 20px;
    right: 20px;
    padding: 12px 20px;
    border-radius: 4px;
    color: white;
    font-size: 14px;
    z-index: 2000;
    animation: slideIn 0.3s ease-out;
}

.toast-success {
    background: #28a745;
}

.toast-error {
    background: #dc3545;
}

@keyframes slideIn {
    from {
        transform: translateX(100%);
        opacity: 0;
    }
    to {
        transform: translateX(0);
        opacity: 1;
    }
}
```

## Implementation Notes

### Database Migration

A database migration is required to add the Projects table:

```csharp
// Create migration
dotnet ef migrations add AddProjectsTable

// Apply migration
dotnet ef database update
```

### Service Registration

Update `Program.cs` to register new services:

```csharp
builder.Services.AddScoped<IStartupValidationService, StartupValidationService>();
builder.Services.AddScoped<IProjectManagementService, ProjectManagementService>();
```

### Backward Compatibility

**Project Path Input**: The old text input for project path is completely replaced. Existing dialogues will continue to work with their stored project paths.

**Automatic Checkpoints**: Removing automatic checkpoint creation is a breaking change in behavior. Users will need to manually create checkpoints going forward. This should be documented in release notes.

**Configuration**: No changes to existing configuration structure. The model connection validation is additive.

### Performance Considerations

**Model Connection Validation**: 
- Timeout set to 5 seconds to avoid blocking startup
- Runs asynchronously to not block UI rendering
- Only validates Ollama connections (OpenAI is cloud-based and assumed available)

**Project List Loading**:
- Projects are loaded once on page load
- Cached in memory on frontend
- Refreshed only when projects are added/deleted

**Checkpoint Creation**:
- Manual checkpoints use existing Git service
- No performance impact from removing automatic checkpoints
- Users control when expensive Git operations occur

## Security Considerations

**Path Validation**: All project paths must be validated using the existing `PathValidator` service to prevent directory traversal attacks.

**SQL Injection**: Entity Framework Core provides parameterized queries by default, protecting against SQL injection.

**XSS Prevention**: All user-provided content (project names, checkpoint descriptions) must be escaped using `escapeHtml()` function before rendering in the DOM.

**CORS**: Existing CORS configuration allows all origins for development. Production deployment should restrict this.

## Deployment Checklist

1. ✅ Run database migration to create Projects table
2. ✅ Update frontend HTML with new UI components
3. ✅ Update frontend JavaScript with new functions
4. ✅ Add new CSS styles for UI components
5. ✅ Register new services in Program.cs
6. ✅ Add new API endpoints to Program.cs
7. ✅ Remove automatic checkpoint creation from PromptProcessor
8. ✅ Test model connection validation with Ollama running/stopped
9. ✅ Test project management CRUD operations
10. ✅ Test manual checkpoint creation
11. ✅ Update user documentation
12. ✅ Add release notes about breaking changes

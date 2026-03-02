using Microsoft.EntityFrameworkCore;
using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using CSharpRefactoringAssistant.Tests;
using System.Text.Json;

// Check for test command
if (args.Length > 0 && args[0] == "test-validation")
{
    await ValidationTestRunner.RunAllValidationTests();
    return;
}

if (args.Length > 0 && args[0] == "test-config")
{
    await ConfigTestRunner.RunSaveConfigurationTests();
    return;
}

if (args.Length > 0 && args[0] == "test-factory")
{
    FactoryTestRunner.RunAllFactoryTests();
    return;
}

if (args.Length > 0 && args[0] == "test-endpoint")
{
    await ConfigEndpointTestRunner.RunAllEndpointTests();
    return;
}

if (args.Length > 0 && args[0] == "test-promptprocessor")
{
    await PromptProcessorTestRunner.RunAllPromptProcessorTests();
    return;
}

if (args.Length > 0 && args[0] == "test-startup")
{
    await StartupValidationServiceTests.Main(args);
    return;
}

if (args.Length > 0 && args[0] == "test-taskexecutor")
{
    await TaskExecutorManualTests.Main(args);
    return;
}

if (args.Length > 0 && args[0] == "test-integration")
{
    var integrationTest = new TaskExecutorIntegrationTests();
    var success = await integrationTest.RunFullIntegrationTestAsync();
    Environment.Exit(success ? 0 : 1);
    return;
}

if (args.Length > 0 && args[0] == "test-commandrecognizer")
{
    CommandRecognizerTests.RunAllTests();
    return;
}

if (args.Length > 0 && args[0] == "test-taskspathresolver")
{
    await TasksFilePathResolverTests.RunAllTests();
    return;
}

if (args.Length > 0 && args[0] == "test-agentcommands")
{
    await AgentCommandIntegrationTests.RunAllTests();
    return;
}

if (args.Length > 0 && args[0] == "test-websocket")
{
    await WebSocketInfrastructureTests.Main(args);
    return;
}

if (args.Length > 0 && args[0] == "test-websocket-integration")
{
    await WebSocketIntegrationTests.Main(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Настройка JSON сериализации для обработки циклических ссылок
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

// Add DbContext
builder.Services.AddDbContext<RefactoringDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services
builder.Services.AddSingleton<IMcpClient, McpClient>();
builder.Services.AddScoped<IGitService, GitService>();
builder.Services.AddScoped<ISerenaService, SerenaService>();
builder.Services.AddScoped<IDirectShellService, DirectShellService>();
builder.Services.AddScoped<PathValidator>();
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<ILlmServiceFactory, LlmServiceFactory>();
builder.Services.AddScoped<IStartupValidationService, StartupValidationService>();
builder.Services.AddScoped<IProjectManagementService, ProjectManagementService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ILlmService>(sp =>
{
    var factory = sp.GetRequiredService<ILlmServiceFactory>();
    return factory.CreateLlmService();
});
builder.Services.AddScoped<IPromptProcessor, PromptProcessor>();
builder.Services.AddScoped<ITaskExecutorService, TaskExecutorService>();
builder.Services.AddScoped<IDeepSeekOrchestratorService, DeepSeekOrchestratorService>();
builder.Services.AddScoped<ITaskExecutionService, TaskExecutionService>();

// Регистрация Lazy<ITaskExecutorService> для разрешения циклической зависимости
builder.Services.AddScoped<Lazy<ITaskExecutorService>>(sp => 
    new Lazy<ITaskExecutorService>(() => sp.GetRequiredService<ITaskExecutorService>()));

// Регистрация компонентов для агентского режима
builder.Services.AddSingleton<CommandRecognizer>();
builder.Services.AddScoped<TasksFilePathResolver>();
builder.Services.AddScoped<IReasoningService, ReasoningService>();

// Регистрация WebSocket компонентов
builder.Services.AddSingleton<IWebSocketManager, CSharpRefactoringAssistant.Services.WebSocketManager>();
builder.Services.AddScoped<IStreamingService, StreamingService>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Enable WebSockets
app.UseWebSockets();

// Enable static files - must be in this order
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
    dbContext.Database.EnsureCreated();
    
    // Initialize MCP Client
    // MCP Client будет инициализирован при первом использовании
    app.Logger.LogInformation("MCP Client will be initialized on first use");
}

app.UseCors();

// Dialogue endpoints
app.MapPost("/api/dialogues", async (
    CreateDialogueRequest request,
    RefactoringDbContext dbContext,
    IGitService gitService,
    ISerenaService serenaService,
    PathValidator pathValidator) =>
{
    // Validate project path
    if (!pathValidator.ValidatePath(request.ProjectPath, out var errorMessage))
        return Results.BadRequest(errorMessage);

    // Initialize Git if needed
    if (!await gitService.IsGitRepositoryAsync(request.ProjectPath))
    {
        await gitService.InitializeRepositoryAsync(request.ProjectPath);
    }

    // Create dialogue
    var dialogue = new Dialogue
    {
        ProjectPath = request.ProjectPath,
        CreatedAt = DateTime.UtcNow
    };

    dbContext.Dialogues.Add(dialogue);
    await dbContext.SaveChangesAsync();

    // Activate project in Serena
    try
    {
        await serenaService.ActivateProjectAsync(request.ProjectPath);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to activate project in Serena");
    }

    return Results.Ok(dialogue);
});

app.MapGet("/api/dialogues", async (
    RefactoringDbContext dbContext,
    IProjectManagementService projectService) =>
{
    // Получаем выбранный проект
    var selectedProject = await projectService.GetSelectedProjectAsync();
    
    if (selectedProject == null)
    {
        // Если нет выбранного проекта, возвращаем все диалоги
        var allDialogues = await dbContext.Dialogues
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
        return Results.Ok(allDialogues);
    }
    
    // Возвращаем только диалоги выбранного проекта
    var dialogues = await dbContext.Dialogues
        .Where(d => d.ProjectPath == selectedProject.Path)
        .OrderByDescending(d => d.CreatedAt)
        .ToListAsync();
    
    return Results.Ok(dialogues);
});

app.MapGet("/api/dialogues/{id}", async (
    int id,
    RefactoringDbContext dbContext) =>
{
    var dialogue = await dbContext.Dialogues
        .Include(d => d.Messages.OrderBy(m => m.Timestamp))
        .Include(d => d.Checkpoints.OrderByDescending(c => c.CreatedAt))
        .FirstOrDefaultAsync(d => d.Id == id);

    if (dialogue == null)
        return Results.NotFound();

    return Results.Ok(dialogue);
});

app.MapDelete("/api/dialogues/{id}", async (
    int id,
    RefactoringDbContext dbContext) =>
{
    var dialogue = await dbContext.Dialogues
        .Include(d => d.Messages)
        .Include(d => d.Checkpoints)
        .FirstOrDefaultAsync(d => d.Id == id);

    if (dialogue == null)
        return Results.NotFound("Dialogue not found");

    // Remove all related messages and checkpoints (cascade delete)
    dbContext.Dialogues.Remove(dialogue);
    await dbContext.SaveChangesAsync();

    app.Logger.LogInformation("Deleted dialogue {DialogueId} with {MessageCount} messages and {CheckpointCount} checkpoints",
        id, dialogue.Messages.Count, dialogue.Checkpoints.Count);

    return Results.Ok(new { message = "Dialogue deleted successfully" });
});

// Message endpoints
app.MapPost("/api/dialogues/{id}/messages", async (
    int id,
    SendMessageRequest request,
    IPromptProcessor promptProcessor) =>
{
    try
    {
        var response = await promptProcessor.ProcessPromptAsync(id, request.Content);
        return Results.Ok(new { content = response });
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(ex.Message);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error processing message");
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/api/messages/{messageId}", async (
    int messageId,
    RefactoringDbContext dbContext) =>
{
    var message = await dbContext.Messages.FindAsync(messageId);
    
    if (message == null)
        return Results.NotFound(new { message = "Message not found" });

    dbContext.Messages.Remove(message);
    await dbContext.SaveChangesAsync();

    app.Logger.LogInformation("Deleted message {MessageId} from dialogue {DialogueId}",
        messageId, message.DialogueId);

    return Results.Ok(new { message = "Message deleted successfully" });
});

// DialogueGroup endpoints
app.MapGet("/api/dialogue-groups", async (
    RefactoringDbContext dbContext,
    IProjectManagementService projectService) =>
{
    var selectedProject = await projectService.GetSelectedProjectAsync();
    
    if (selectedProject == null)
    {
        return Results.Ok(new List<DialogueGroup>());
    }
    
    var groups = await dbContext.DialogueGroups
        .Include(g => g.Dialogues)
        .Where(g => g.ProjectPath == selectedProject.Path)
        .OrderByDescending(g => g.CreatedAt)
        .ToListAsync();
    
    return Results.Ok(groups);
});

app.MapPost("/api/dialogue-groups", async (
    CreateDialogueGroupRequest request,
    RefactoringDbContext dbContext) =>
{
    var group = new DialogueGroup
    {
        Name = request.Name,
        ProjectPath = request.ProjectPath,
        CreatedAt = DateTime.UtcNow
    };
    
    dbContext.DialogueGroups.Add(group);
    await dbContext.SaveChangesAsync();
    
    return Results.Ok(group);
});

app.MapPut("/api/dialogue-groups/{id}", async (
    int id,
    UpdateDialogueGroupRequest request,
    RefactoringDbContext dbContext) =>
{
    var group = await dbContext.DialogueGroups.FindAsync(id);
    if (group == null)
        return Results.NotFound();
    
    group.Name = request.Name;
    group.IsCollapsed = request.IsCollapsed;
    
    await dbContext.SaveChangesAsync();
    return Results.Ok(group);
});

app.MapPut("/api/dialogue-groups/{id}/context", async (
    int id,
    UpdateDialogueGroupContextRequest request,
    RefactoringDbContext dbContext) =>
{
    var group = await dbContext.DialogueGroups.FindAsync(id);
    if (group == null)
        return Results.NotFound();
    
    group.Requirements = request.Requirements;
    group.Design = request.Design;
    group.Tasks = request.Tasks;
    
    await dbContext.SaveChangesAsync();
    return Results.Ok(group);
});

app.MapDelete("/api/dialogue-groups/{id}", async (
    int id,
    RefactoringDbContext dbContext) =>
{
    var group = await dbContext.DialogueGroups
        .Include(g => g.Dialogues)
        .FirstOrDefaultAsync(g => g.Id == id);
    
    if (group == null)
        return Results.NotFound();
    
    dbContext.DialogueGroups.Remove(group);
    await dbContext.SaveChangesAsync();
    
    return Results.Ok(new { message = "Group deleted successfully" });
});

app.MapPost("/api/dialogue-groups/{groupId}/dialogues", async (
    int groupId,
    RefactoringDbContext dbContext) =>
{
    var group = await dbContext.DialogueGroups.FindAsync(groupId);
    if (group == null)
        return Results.NotFound("Group not found");
    
    var dialogue = new Dialogue
    {
        ProjectPath = group.ProjectPath,
        DialogueGroupId = groupId,
        CreatedAt = DateTime.UtcNow
    };
    
    dbContext.Dialogues.Add(dialogue);
    await dbContext.SaveChangesAsync();
    
    return Results.Ok(dialogue);
});

// Checkpoint endpoints
app.MapGet("/api/dialogues/{id}/checkpoints", async (
    int id,
    RefactoringDbContext dbContext) =>
{
    var checkpoints = await dbContext.Checkpoints
        .Where(c => c.DialogueId == id)
        .OrderByDescending(c => c.CreatedAt)
        .ToListAsync();

    return Results.Ok(checkpoints);
});

app.MapPost("/api/dialogues/{id}/checkpoints", async (
    int id,
    CreateCheckpointRequest request,
    RefactoringDbContext dbContext,
    IGitService gitService) =>
{
    var dialogue = await dbContext.Dialogues.FindAsync(id);
    if (dialogue == null)
        return Results.NotFound(new { message = "Dialogue not found" });

    try
    {
        // Создаем чекпойнт
        var description = request.Description ?? "Manual checkpoint";
        var commitHash = await gitService.CreateCheckpointAsync(
            dialogue.ProjectPath,
            description);

        var checkpoint = new Checkpoint
        {
            DialogueId = id,
            Description = description,
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

app.MapPost("/api/dialogues/{id}/rollback", async (
    int id,
    RollbackRequest request,
    RefactoringDbContext dbContext,
    IGitService gitService,
    ISerenaService serenaService) =>
{
    var dialogue = await dbContext.Dialogues.FindAsync(id);
    if (dialogue == null)
        return Results.NotFound("Dialogue not found");

    var checkpoint = await dbContext.Checkpoints.FindAsync(request.CheckpointId);
    if (checkpoint == null || checkpoint.DialogueId != id)
        return Results.NotFound("Checkpoint not found");

    // Check for uncommitted changes
    if (await gitService.HasUncommittedChangesAsync(dialogue.ProjectPath))
        return Results.BadRequest("Cannot rollback with uncommitted changes");

    try
    {
        // Perform rollback
        await gitService.RollbackToCheckpointAsync(dialogue.ProjectPath, checkpoint.CommitHash);

        // Reactivate project in Serena
        await serenaService.ActivateProjectAsync(dialogue.ProjectPath);

        return Results.Ok(new { message = "Rollback successful" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error during rollback");
        return Results.Problem(ex.Message);
    }
});

// Configuration endpoints
app.MapGet("/api/configuration", async (IConfigurationService configService) =>
{
    try
    {
        var config = await configService.GetConfigurationAsync();
        return Results.Ok(new ConfigurationResponse
        {
            Success = true,
            Configuration = config
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error retrieving configuration");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Configuration Error");
    }
});

app.MapPost("/api/configuration", async (
    SaveConfigurationRequest request,
    IConfigurationService configService) =>
{
    try
    {
        // Convert request to LlmConfiguration
        var config = new LlmConfiguration
        {
            Provider = request.Provider,
            OpenAI = request.OpenAI,
            Ollama = request.Ollama
        };

        // Validate configuration
        var isValid = await configService.ValidateConfigurationAsync(config);
        if (!isValid)
        {
            return Results.BadRequest(new ConfigurationResponse
            {
                Success = false,
                Message = "Configuration validation failed. Please check that all required fields are provided and URLs are valid."
            });
        }

        // Save configuration
        await configService.SaveConfigurationAsync(config);

        // Return success response with updated configuration
        var savedConfig = await configService.GetConfigurationAsync();
        return Results.Ok(new ConfigurationResponse
        {
            Success = true,
            Message = "Configuration saved successfully",
            Configuration = savedConfig
        });
    }
    catch (ArgumentException ex)
    {
        app.Logger.LogWarning(ex, "Configuration validation failed");
        return Results.BadRequest(new ConfigurationResponse
        {
            Success = false,
            Message = ex.Message
        });
    }
    catch (InvalidOperationException ex)
    {
        app.Logger.LogError(ex, "Error saving configuration");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Configuration Save Error");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error saving configuration");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Configuration Error");
    }
});

app.MapGet("/api/configuration/ollama/models", async (
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) =>
{
    try
    {
        // Read Ollama base URL from configuration
        var ollamaBaseUrl = configuration["Llm:Ollama:BaseUrl"];
        
        if (string.IsNullOrWhiteSpace(ollamaBaseUrl))
        {
            // Use default Ollama URL if not configured
            ollamaBaseUrl = "http://localhost:11434";
        }

        // Create HTTP client and make request to Ollama API
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5); // Set reasonable timeout
        
        var response = await httpClient.GetAsync($"{ollamaBaseUrl}/api/tags");
        
        if (!response.IsSuccessStatusCode)
        {
            app.Logger.LogWarning("Failed to fetch Ollama models. Status: {StatusCode}", response.StatusCode);
            return Results.Ok(new OllamaModelsResponse
            {
                Models = new List<string>()
            });
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);

        var models = new List<string>();
        
        // Parse the models from the response
        if (responseData.TryGetProperty("models", out var modelsArray))
        {
            foreach (var model in modelsArray.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var nameProperty))
                {
                    var modelName = nameProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(modelName))
                    {
                        models.Add(modelName);
                    }
                }
            }
        }

        app.Logger.LogInformation("Successfully fetched {Count} models from Ollama", models.Count);
        
        return Results.Ok(new OllamaModelsResponse
        {
            Models = models
        });
    }
    catch (HttpRequestException ex)
    {
        app.Logger.LogWarning(ex, "Unable to connect to Ollama instance");
        // Return empty list on connection error - graceful handling
        return Results.Ok(new OllamaModelsResponse
        {
            Models = new List<string>()
        });
    }
    catch (TaskCanceledException ex)
    {
        app.Logger.LogWarning(ex, "Request to Ollama timed out");
        // Return empty list on timeout - graceful handling
        return Results.Ok(new OllamaModelsResponse
        {
            Models = new List<string>()
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error fetching Ollama models");
        // Return empty list on any error - graceful handling
        return Results.Ok(new OllamaModelsResponse
        {
            Models = new List<string>()
        });
    }
});

app.MapPost("/api/configuration/test", async (
    SaveConfigurationRequest request,
    IHttpClientFactory httpClientFactory,
    ILogger<OpenAiLlmService> openAiLogger,
    ILogger<OllamaLlmService> ollamaLogger) =>
{
    try
    {
        // Create a temporary in-memory configuration with the test settings
        var configData = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = request.Provider
        };

        if (request.Provider?.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) == true && request.OpenAI != null)
        {
            configData["Llm:OpenAI:ApiKey"] = request.OpenAI.ApiKey;
            configData["Llm:OpenAI:Model"] = request.OpenAI.Model;
            configData["Llm:OpenAI:BaseUrl"] = request.OpenAI.BaseUrl;
        }
        else if (request.Provider?.Equals("Ollama", StringComparison.OrdinalIgnoreCase) == true && request.Ollama != null)
        {
            configData["Llm:Ollama:BaseUrl"] = request.Ollama.BaseUrl;
            configData["Llm:Ollama:Model"] = request.Ollama.Model;
        }
        else
        {
            return Results.BadRequest(new TestConnectionResponse
            {
                Success = false,
                Message = "Invalid provider configuration. Please specify either OpenAI or Ollama settings."
            });
        }

        var testConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Create the appropriate LLM service based on provider
        ILlmService llmService;
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10); // Set reasonable timeout for test

        if (request.Provider?.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) == true)
        {
            llmService = new OpenAiLlmService(httpClient, testConfiguration, openAiLogger);
        }
        else if (request.Provider?.Equals("Ollama", StringComparison.OrdinalIgnoreCase) == true)
        {
            llmService = new OllamaLlmService(httpClient, testConfiguration, ollamaLogger);
        }
        else
        {
            return Results.BadRequest(new TestConnectionResponse
            {
                Success = false,
                Message = "Unsupported provider type. Please use 'OpenAI' or 'Ollama'."
            });
        }

        // Send a simple test prompt
        var testPrompt = "Hello, this is a connection test. Please respond with 'OK'.";
        var response = await llmService.SendPromptAsync(testPrompt, new List<Message>(), new List<FunctionDefinition>());

        // Check if we got a response
        if (response != null && (!string.IsNullOrWhiteSpace(response.TextContent) || response.FunctionCalls?.Count > 0))
        {
            app.Logger.LogInformation("Connection test successful for provider: {Provider}", request.Provider);
            return Results.Ok(new TestConnectionResponse
            {
                Success = true,
                Message = $"Connection successful! The {request.Provider} service is responding correctly."
            });
        }
        else
        {
            app.Logger.LogWarning("Connection test returned empty response for provider: {Provider}", request.Provider);
            return Results.Ok(new TestConnectionResponse
            {
                Success = false,
                Message = "Connection test failed: Received empty response from the service."
            });
        }
    }
    catch (ArgumentException ex)
    {
        app.Logger.LogWarning(ex, "Configuration validation failed during connection test");
        return Results.Ok(new TestConnectionResponse
        {
            Success = false,
            Message = $"Configuration error: {ex.Message}"
        });
    }
    catch (LlmException ex)
    {
        app.Logger.LogWarning(ex, "LLM service error during connection test");
        return Results.Ok(new TestConnectionResponse
        {
            Success = false,
            Message = $"Connection failed: {ex.Message}"
        });
    }
    catch (HttpRequestException ex)
    {
        app.Logger.LogWarning(ex, "HTTP request failed during connection test");
        return Results.Ok(new TestConnectionResponse
        {
            Success = false,
            Message = $"Connection failed: Unable to reach the service. Please check the base URL and network connectivity."
        });
    }
    catch (TaskCanceledException ex)
    {
        app.Logger.LogWarning(ex, "Connection test timed out");
        return Results.Ok(new TestConnectionResponse
        {
            Success = false,
            Message = "Connection test timed out. The service may be unavailable or slow to respond."
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unexpected error during connection test");
        return Results.Ok(new TestConnectionResponse
        {
            Success = false,
            Message = $"Unexpected error: {ex.Message}"
        });
    }
});

// Project management endpoints
app.MapGet("/api/projects", async (IProjectManagementService projectService, ILogger<Program> logger) =>
{
    var projects = await projectService.GetAllProjectsAsync();
    logger.LogInformation("GET /api/projects: возвращено {Count} проектов. Выбранный: {Selected}", 
        projects.Count, 
        projects.FirstOrDefault(p => p.IsSelected)?.Name ?? "нет");
    return Results.Ok(projects);
});

app.MapGet("/api/projects/selected", async (IProjectManagementService projectService) =>
{
    var project = await projectService.GetSelectedProjectAsync();
    return project != null ? Results.Ok(project) : Results.NotFound();
});

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
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

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
        return Results.NotFound(new { message = ex.Message });
    }
});

app.MapPost("/api/projects/{id}/select", async (
    int id,
    IProjectManagementService projectService,
    ILogger<Program> logger) =>
{
    try
    {
        await projectService.SelectProjectAsync(id);
        logger.LogInformation("POST /api/projects/{Id}/select: проект успешно выбран", id);
        return Results.Ok(new { message = "Проект выбран" });
    }
    catch (ArgumentException ex)
    {
        logger.LogWarning("POST /api/projects/{Id}/select: проект не найден", id);
        return Results.NotFound(new { message = ex.Message });
    }
});

// Startup validation endpoint
app.MapGet("/api/startup/validate", async (IStartupValidationService validationService) =>
{
    var result = await validationService.ValidateModelConnectionAsync();
    return Results.Ok(result);
});

// Reasoning model validation endpoint
app.MapGet("/api/startup/validate-reasoning", async (IConfigurationService configService, IHttpClientFactory httpClientFactory) =>
{
    try
    {
        app.Logger.LogInformation("[ValidateReasoning] Начало проверки reasoning модели");
        
        var config = await configService.GetConfigurationAsync();
        
        app.Logger.LogInformation("[ValidateReasoning] Provider: {Provider}, ReasoningModel: {Model}", 
            config.Provider, config.Ollama?.ReasoningModel ?? "null");
        
        // Проверяем, настроена ли reasoning модель
        if (config.Provider != "Ollama" || string.IsNullOrEmpty(config.Ollama?.ReasoningModel))
        {
            app.Logger.LogWarning("[ValidateReasoning] Reasoning модель не настроена");
            return Results.Ok(new
            {
                isConnected = false,
                errorMessage = "Reasoning модель не настроена",
                modelName = (string?)null
            });
        }
        
        // Проверяем доступность reasoning модели через Ollama API
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        
        var baseUrl = config.Ollama.BaseUrl;
        var model = config.Ollama.ReasoningModel;
        
        app.Logger.LogInformation("[ValidateReasoning] Проверка модели {Model} в Ollama {BaseUrl}", model, baseUrl);
        
        // Проверяем, что модель существует в списке доступных моделей
        var response = await httpClient.GetAsync($"{baseUrl}/api/tags");
        
        if (!response.IsSuccessStatusCode)
        {
            app.Logger.LogWarning("[ValidateReasoning] Не удалось подключиться к Ollama: {Status}", response.StatusCode);
            return Results.Ok(new
            {
                isConnected = false,
                errorMessage = $"Не удалось подключиться к Ollama: {response.StatusCode}",
                modelName = model
            });
        }
        
        var responseJson = await response.Content.ReadAsStringAsync();
        var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);
        
        var modelExists = false;
        var availableModels = new List<string>();
        
        if (responseData.TryGetProperty("models", out var modelsArray))
        {
            foreach (var modelItem in modelsArray.EnumerateArray())
            {
                if (modelItem.TryGetProperty("name", out var nameProperty))
                {
                    var modelName = nameProperty.GetString();
                    if (modelName != null)
                    {
                        availableModels.Add(modelName);
                        
                        // Проверяем точное совпадение или совпадение без тега версии
                        if (modelName.Equals(model, StringComparison.OrdinalIgnoreCase) ||
                            modelName.StartsWith(model.Split(':')[0], StringComparison.OrdinalIgnoreCase))
                        {
                            modelExists = true;
                        }
                    }
                }
            }
        }
        
        app.Logger.LogInformation("[ValidateReasoning] Доступные модели в Ollama: {Models}", string.Join(", ", availableModels));
        app.Logger.LogInformation("[ValidateReasoning] Модель {Model} найдена: {Found}", model, modelExists);
        
        if (!modelExists)
        {
            return Results.Ok(new
            {
                isConnected = false,
                errorMessage = $"Модель {model} не найдена в Ollama. Доступные: {string.Join(", ", availableModels.Take(5))}",
                modelName = model
            });
        }
        
        return Results.Ok(new
        {
            isConnected = true,
            errorMessage = (string?)null,
            modelName = model
        });
    }
    catch (HttpRequestException ex)
    {
        app.Logger.LogWarning(ex, "[ValidateReasoning] Не удалось подключиться к Ollama");
        return Results.Ok(new
        {
            isConnected = false,
            errorMessage = "Не удалось подключиться к Ollama",
            modelName = (string?)null
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "[ValidateReasoning] Ошибка при проверке reasoning модели");
        return Results.Ok(new
        {
            isConnected = false,
            errorMessage = $"Ошибка: {ex.Message}",
            modelName = (string?)null
        });
    }
});

// DEBUG: Диагностический endpoint для проверки конфигурации
app.MapGet("/api/debug/config", async (IConfigurationService configService) =>
{
    var config = await configService.GetConfigurationAsync();
    return Results.Ok(new
    {
        provider = config.Provider,
        ollamaBaseUrl = config.Ollama?.BaseUrl,
        ollamaModel = config.Ollama?.Model,
        ollamaReasoningModel = config.Ollama?.ReasoningModel,
        hasOllama = config.Ollama != null
    });
});

// Task execution endpoints
app.MapPost("/api/dialogues/{id}/execute-tasks", async (
    int id,
    ExecuteTasksRequest request,
    ITaskExecutorService taskExecutorService,
    RefactoringDbContext dbContext) =>
{
    try
    {
        var sessionId = await taskExecutorService.ExecuteTasksAsync(id, request.TasksFilePath, request.SkipOptional);
        return Results.Accepted($"/api/dialogues/{id}/execution-status", new { sessionId });
    }
    catch (ArgumentException ex) when (ex.Message.Contains("Dialogue not found"))
    {
        return Results.NotFound(new { message = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error starting task execution");
        return Results.Problem(ex.Message);
    }
});

// Endpoint для прямого выполнения задач через DeepSeek API с инструментами
app.MapPost("/api/dialogues/{id}/execute-tasks-direct", async (
    int id,
    RefactoringDbContext dbContext,
    ITaskExecutionService taskExecutionService,
    IStreamingService streamingService) =>
{
    try
    {
        // Получаем диалог с группой
        var dialogue = await dbContext.Dialogues
            .Include(d => d.DialogueGroup)
            .FirstOrDefaultAsync(d => d.Id == id);
        
        if (dialogue == null)
            return Results.NotFound(new { message = "Диалог не найден" });
        
        if (dialogue.DialogueGroup == null)
            return Results.BadRequest(new { message = "Диалог не принадлежит группе" });
        
        if (string.IsNullOrWhiteSpace(dialogue.DialogueGroup.Tasks))
            return Results.BadRequest(new { message = "Задачи не заполнены в группе" });
        
        // Запускаем выполнение в фоновом режиме
        _ = Task.Run(async () =>
        {
            try
            {
                app.Logger.LogInformation("Начало прямого выполнения задач для диалога {DialogueId}", id);
                
                // Выполняем задачи через DeepSeek API
                var result = await taskExecutionService.ExecuteTasksAsync(
                    id,
                    dialogue.DialogueGroup.Requirements ?? "",
                    dialogue.DialogueGroup.Design ?? "",
                    dialogue.DialogueGroup.Tasks);
                
                // Если выполнение успешно, помечаем все задачи как выполненные
                if (result.Success)
                {
                    using var scope = app.Services.CreateScope();
                    var scopedDbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
                    
                    var dialogueToUpdate = await scopedDbContext.Dialogues
                        .Include(d => d.DialogueGroup)
                        .FirstOrDefaultAsync(d => d.Id == id);
                    
                    if (dialogueToUpdate?.DialogueGroup != null && !string.IsNullOrWhiteSpace(dialogueToUpdate.DialogueGroup.Tasks))
                    {
                        // Заменяем все [ ] на [x] в поле Tasks
                        dialogueToUpdate.DialogueGroup.Tasks = dialogueToUpdate.DialogueGroup.Tasks.Replace("[ ]", "[x]");
                        await scopedDbContext.SaveChangesAsync();
                        
                        app.Logger.LogInformation("Все задачи помечены как выполненные для группы {GroupId}", dialogueToUpdate.DialogueGroup.Id);
                    }
                }
                
                // Формируем итоговое сообщение
                var finalMessage = result.Success 
                    ? $"✅ Задачи успешно выполнены!\n\n{result.Message}\n{result.LaunchInstructions}"
                    : $"❌ {result.Message}";
                
                // Отправляем итоговое сообщение в чат через streaming
                await streamingService.StreamResponseAsync(id, finalMessage);
                
                app.Logger.LogInformation("Прямое выполнение задач завершено для диалога {DialogueId}", id);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Ошибка прямого выполнения задач для диалога {DialogueId}", id);
                
                // Отправляем сообщение об ошибке в чат
                await streamingService.StreamResponseAsync(id, $"❌ Ошибка выполнения задач: {ex.Message}");
            }
        });
        
        return Results.Ok(new { message = "Выполнение задач запущено" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Ошибка запуска прямого выполнения задач");
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/dialogues/{id}/stop-execution", async (
    int id,
    ITaskExecutorService taskExecutorService) =>
{
    try
    {
        await taskExecutorService.StopExecutionAsync(id);
        return Results.Ok(new { message = "Execution stopped" });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error stopping task execution");
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/dialogues/{id}/resume-execution", async (
    int id,
    ITaskExecutorService taskExecutorService) =>
{
    try
    {
        await taskExecutorService.ResumeExecutionAsync(id);
        return Results.Accepted($"/api/dialogues/{id}/execution-status", new { message = "Execution resumed" });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error resuming task execution");
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/dialogues/{id}/execution-status", async (
    int id,
    ITaskExecutorService taskExecutorService) =>
{
    try
    {
        var status = await taskExecutorService.GetExecutionStatusAsync(id);
        return Results.Ok(status);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error getting execution status");
        return Results.Problem(ex.Message);
    }
});

// Remove the root endpoint - let static files handle it
// app.MapGet("/", () => "C# Refactoring Assistant API");

// WebSocket endpoint
app.Map("/ws", async (HttpContext context) =>
{
    app.Logger.LogInformation("WebSocket запрос получен: Path={Path}, Query={Query}", 
        context.Request.Path, context.Request.QueryString);
    
    // Проверка, что это WebSocket запрос
    if (!context.WebSockets.IsWebSocketRequest)
    {
        app.Logger.LogWarning("Получен не-WebSocket запрос на /ws endpoint");
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket connection expected");
        return;
    }

    // Извлечение dialogueId из query string
    if (!context.Request.Query.TryGetValue("dialogueId", out var dialogueIdStr) ||
        !int.TryParse(dialogueIdStr, out var dialogueId))
    {
        app.Logger.LogWarning("Отсутствует или неверный dialogueId в query string: {QueryString}", 
            context.Request.QueryString);
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("dialogueId query parameter is required");
        return;
    }

    app.Logger.LogInformation("Проверка существования диалога: DialogueId={DialogueId}", dialogueId);

    // Проверка существования диалога
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
    var dialogue = await dbContext.Dialogues.FindAsync(dialogueId);
    
    if (dialogue == null)
    {
        app.Logger.LogWarning("Диалог не найден: DialogueId={DialogueId}", dialogueId);
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsync($"Dialogue {dialogueId} not found");
        return;
    }

    app.Logger.LogInformation("Диалог найден, принятие WebSocket соединения: DialogueId={DialogueId}", dialogueId);

    // Принятие WebSocket соединения
    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = Guid.NewGuid().ToString();
    
    app.Logger.LogInformation("WebSocket соединение установлено: ConnectionId={ConnectionId}, DialogueId={DialogueId}, Timestamp={Timestamp}", 
        connectionId, dialogueId, DateTime.UtcNow);

    // Получаем сервисы из scope
    var webSocketManager = scope.ServiceProvider.GetRequiredService<IWebSocketManager>();
    var streamingService = scope.ServiceProvider.GetRequiredService<IStreamingService>();

    try
    {
        // Регистрируем соединение в WebSocketManager (отправит connection_ack автоматически)
        await webSocketManager.RegisterConnectionAsync(connectionId, webSocket, dialogueId);

        // Буфер для получения сообщений
        var buffer = new byte[1024 * 4];

        // Обработка входящих сообщений
        while (webSocket.State == System.Net.WebSockets.WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None);

            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            {
                app.Logger.LogInformation("WebSocket закрытие запрошено клиентом: ConnectionId={ConnectionId}", connectionId);
                await webSocket.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    "Connection closed by client",
                    CancellationToken.None);
                break;
            }

            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
            {
                var messageJson = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                app.Logger.LogInformation("Получено WebSocket сообщение: {Message}", messageJson);

                try
                {
                    // Настройка десериализации для camelCase (frontend отправляет type, payload)
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    
                    var message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson, options);
                    
                    if (message == null)
                    {
                        app.Logger.LogWarning("Не удалось десериализовать WebSocket сообщение");
                        continue;
                    }

                    app.Logger.LogInformation("Десериализованное сообщение - Type: {Type}, Payload: {Payload}", 
                        message.Type, message.Payload?.ToString() ?? "null");

                    // Обработка различных типов сообщений
                    switch (message.Type)
                    {
                        case WebSocketMessageTypes.Ping:
                            // Ответ на ping
                            await webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                            {
                                Type = WebSocketMessageTypes.Pong,
                                Payload = new { connectionId }
                            });
                            break;

                        case WebSocketMessageTypes.UserMessage:
                            // Обработка сообщения пользователя через StreamingService
                            app.Logger.LogInformation("Получено сообщение пользователя: DialogueId={DialogueId}, ConnectionId={ConnectionId}", 
                                dialogueId, connectionId);
                            
                            try
                            {
                                // Извлекаем содержимое сообщения из payload
                                var payloadElement = (JsonElement)message.Payload!;
                                var content = payloadElement.GetProperty("content").GetString();
                                
                                if (string.IsNullOrWhiteSpace(content))
                                {
                                    app.Logger.LogWarning("Получено пустое сообщение пользователя");
                                    await webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                                    {
                                        Type = WebSocketMessageTypes.Error,
                                        Payload = new ErrorPayload
                                        {
                                            DialogueId = dialogueId,
                                            Message = "Содержимое сообщения не может быть пустым"
                                        }
                                    });
                                    break;
                                }
                                
                                // Запускаем обработку в фоновом режиме
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await streamingService.ProcessPromptWithStreamingAsync(
                                            dialogueId,
                                            content,
                                            connectionId,
                                            CancellationToken.None);
                                    }
                                    catch (Exception ex)
                                    {
                                        app.Logger.LogError(ex, "Ошибка при обработке сообщения пользователя");
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                app.Logger.LogError(ex, "Ошибка при извлечении содержимого сообщения");
                                await webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                                {
                                    Type = WebSocketMessageTypes.Error,
                                    Payload = new ErrorPayload
                                    {
                                        DialogueId = dialogueId,
                                        Message = "Неверный формат сообщения"
                                    }
                                });
                            }
                            break;

                        case WebSocketMessageTypes.CancelGeneration:
                            // Обработка отмены генерации
                            app.Logger.LogInformation("Получен запрос на отмену генерации: DialogueId={DialogueId}, ConnectionId={ConnectionId}", 
                                dialogueId, connectionId);
                            
                            await streamingService.CancelGenerationAsync(connectionId);
                            break;

                        default:
                            app.Logger.LogWarning("Неизвестный тип сообщения: {Type}", message.Type);
                            break;
                    }
                }
                catch (JsonException ex)
                {
                    app.Logger.LogError(ex, "Ошибка парсинга WebSocket сообщения");
                    
                    // Отправка сообщения об ошибке
                    await webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                    {
                        Type = WebSocketMessageTypes.Error,
                        Payload = new ErrorPayload
                        {
                            Message = "Неверный формат сообщения",
                            DialogueId = dialogueId
                        }
                    });
                }
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Ошибка в WebSocket соединении: ConnectionId={ConnectionId}", connectionId);
    }
    finally
    {
        app.Logger.LogInformation("WebSocket соединение закрыто: ConnectionId={ConnectionId}, Timestamp={Timestamp}", 
            connectionId, DateTime.UtcNow);
        
        // Удаляем соединение из WebSocketManager
        await webSocketManager.UnregisterConnectionAsync(connectionId);
        
        // Закрываем WebSocket, если он еще открыт
        if (webSocket.State == System.Net.WebSockets.WebSocketState.Open ||
            webSocket.State == System.Net.WebSockets.WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    "Connection closed",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Ошибка при закрытии WebSocket: ConnectionId={ConnectionId}", connectionId);
            }
        }
        
        webSocket.Dispose();
    }
});

// Ollama management endpoint
app.MapPost("/api/ollama/start", async (IConfiguration configuration) =>
{
    try
    {
        var ollamaConfig = configuration.GetSection("Llm:Ollama");
        var model = ollamaConfig["Model"] ?? "llama3.1:8b";
        
        app.Logger.LogInformation("Attempting to start Ollama model: {Model}", model);
        
        // Запускаем команду ollama run в фоновом режиме
        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start /min ollama run {model}",
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized
        };
        
        System.Diagnostics.Process.Start(processStartInfo);
        
        app.Logger.LogInformation("Ollama model start command executed");
        
        // Даем модели время на запуск
        await Task.Delay(3000);
        
        return Results.Ok(new { message = $"Модель {model} запускается..." });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error starting Ollama model");
        return Results.Problem($"Ошибка запуска модели: {ex.Message}");
    }
});

// Ollama reasoning model management endpoint
app.MapPost("/api/ollama/start-reasoning", async (IConfiguration configuration, IConfigurationService configService) =>
{
    try
    {
        app.Logger.LogInformation("Starting reasoning model endpoint called");
        
        // Получаем текущую конфигурацию
        var config = await configService.GetConfigurationAsync();
        app.Logger.LogInformation("Current provider: {Provider}", config.Provider);
        
        // Проверяем, что используется Ollama
        if (config.Provider != "Ollama")
        {
            app.Logger.LogWarning("Provider is not Ollama: {Provider}", config.Provider);
            return Results.BadRequest(new { message = "Reasoning модель доступна только для провайдера Ollama" });
        }
        
        // Если ReasoningModel не настроена, устанавливаем значение по умолчанию
        if (string.IsNullOrEmpty(config.Ollama?.ReasoningModel))
        {
            app.Logger.LogInformation("ReasoningModel не настроена, устанавливаем значение по умолчанию: deepseek-r1:7b");
            
            if (config.Ollama == null)
            {
                config.Ollama = new OllamaSettings
                {
                    BaseUrl = "http://localhost:11434",
                    Model = "llama3.1:8b"
                };
            }
            
            config.Ollama.ReasoningModel = "deepseek-r1:7b";
            
            try
            {
                // Сохраняем обновленную конфигурацию
                await configService.SaveConfigurationAsync(config);
                app.Logger.LogInformation("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Failed to save configuration");
                return Results.Problem($"Не удалось сохранить конфигурацию: {ex.Message}");
            }
        }
        
        var model = config.Ollama.ReasoningModel;
        
        app.Logger.LogInformation("Attempting to start Ollama reasoning model: {Model}", model);
        
        try
        {
            // Запускаем команду ollama run в фоновом режиме
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start /min ollama run {model}",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized
            };
            
            var process = System.Diagnostics.Process.Start(processStartInfo);
            
            if (process == null)
            {
                app.Logger.LogError("Failed to start process");
                return Results.Problem("Не удалось запустить процесс Ollama");
            }
            
            app.Logger.LogInformation("Ollama reasoning model start command executed successfully");
            
            // Даем модели время на запуск
            await Task.Delay(3000);
            
            return Results.Ok(new { message = $"Reasoning модель {model} запускается..." });
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to start Ollama process");
            return Results.Problem($"Не удалось запустить Ollama: {ex.Message}");
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error starting Ollama reasoning model");
        return Results.Problem($"Ошибка запуска reasoning модели: {ex.Message}");
    }
});

app.Run();

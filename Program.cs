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

// Enable static files - must be in this order
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
    dbContext.Database.EnsureCreated();
    
    // Initialize MCP Client
    var mcpClient = scope.ServiceProvider.GetRequiredService<IMcpClient>();
    try
    {
        await mcpClient.InitializeAsync();
        app.Logger.LogInformation("MCP Client initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to initialize MCP Client. Serena may not be available.");
    }
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

app.MapGet("/api/dialogues", async (RefactoringDbContext dbContext) =>
{
    var dialogues = await dbContext.Dialogues
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
app.MapGet("/api/projects", async (IProjectManagementService projectService) =>
{
    var projects = await projectService.GetAllProjectsAsync();
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
    IProjectManagementService projectService) =>
{
    try
    {
        await projectService.SelectProjectAsync(id);
        return Results.Ok(new { message = "Проект выбран" });
    }
    catch (ArgumentException ex)
    {
        return Results.NotFound(new { message = ex.Message });
    }
});

// Startup validation endpoint
app.MapGet("/api/startup/validate", async (IStartupValidationService validationService) =>
{
    var result = await validationService.ValidateModelConnectionAsync();
    return Results.Ok(result);
});

// Remove the root endpoint - let static files handle it
// app.MapGet("/", () => "C# Refactoring Assistant API");

app.Run();

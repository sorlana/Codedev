# Project Structure

## Root Directory

```
CSharpRefactoringAssistant/
├── Program.cs                      # Application entry point, API endpoints, DI setup
├── appsettings.json               # Configuration (LLM, Serena, database, security)
├── appsettings.Development.json   # Development environment overrides
├── CSharpRefactoringAssistant.csproj  # Project file with dependencies
└── refactoring.db                 # SQLite database file
```

## Core Folders

### Data/
Entity Framework DbContext and database configuration.
- `RefactoringDbContext.cs` - EF Core context with Dialogues, Messages, Checkpoints

### Models/
Data models and DTOs.
- `Dialogue.cs` - Dialogue entity (project conversations)
- `Message.cs` - Message entity (user/assistant messages)
- `Checkpoint.cs` - Git checkpoint entity
- `ConfigurationModels.cs` - LLM configuration models
- `LlmModels.cs` - LLM request/response models
- `McpResponse.cs` - MCP client response models
- `RequestModels.cs` - API request DTOs

### Services/
Business logic and external integrations.
- `ISerenaService.cs` / `SerenaService.cs` - Serena MCP tool wrapper
- `IMcpClient.cs` / `McpClient.cs` - JSON-RPC 2.0 MCP client
- `IGitService.cs` / `GitService.cs` - Git operations (checkpoints, rollback)
- `ILlmService.cs` / `OpenAiLlmService.cs` / `OllamaLlmService.cs` - LLM integrations
- `ILlmServiceFactory.cs` / `LlmServiceFactory.cs` - LLM provider factory
- `IPromptProcessor.cs` / `PromptProcessor.cs` - Main orchestration logic
- `IConfigurationService.cs` / `ConfigurationService.cs` - Configuration management
- `PathValidator.cs` - Security validation for file paths
- `IStartupValidationService.cs` - Startup validation

### Migrations/
Entity Framework database migrations.
- Auto-generated migration files
- `RefactoringDbContextModelSnapshot.cs` - Current schema snapshot

### Tests/
Manual test runners (not xUnit tests).
- `*Tests.cs` - Test implementations
- `E2E_MANUAL_TEST_PLAN.md` - Manual testing guide
- `E2E_TEST_SUMMARY.md` - Test results summary

### wwwroot/
Static web files served by ASP.NET Core.
- `index.html` - Main UI
- `app.js` - Frontend JavaScript (dialogue management, API calls)
- `test-validation.html` - Validation testing page

### _Readme/
Documentation files.
- `README.md` - Main project documentation
- `USAGE_GUIDE.md` - User guide
- Task summaries and implementation notes

## Architecture Patterns

### Service Layer Pattern
All business logic in Services/ with interface-based contracts for testability and DI.

### Repository Pattern
Entity Framework DbContext acts as repository with direct entity access.

### Factory Pattern
`LlmServiceFactory` creates appropriate LLM service based on configuration.

### Minimal API Pattern
Endpoints defined inline in Program.cs using ASP.NET Core Minimal API style.

## Naming Conventions

- Interfaces: `I{ServiceName}` (e.g., `ISerenaService`)
- Services: `{ServiceName}` (e.g., `SerenaService`)
- Models: PascalCase nouns (e.g., `Dialogue`, `Message`)
- API endpoints: `/api/{resource}` (e.g., `/api/dialogues`)
- Private fields: `_camelCase` (e.g., `_dbContext`)
- Async methods: `{MethodName}Async` suffix

## Configuration Structure

```json
{
  "ConnectionStrings": { "DefaultConnection": "..." },
  "Llm": {
    "Provider": "OpenAI|Ollama",
    "OpenAI": { "ApiKey", "Model", "BaseUrl" },
    "Ollama": { "BaseUrl", "Model" }
  },
  "Serena": {
    "StdioCommand": "docker",
    "StdioArgs": ["exec", "-i", "serena-container", "serena-mcp"]
  },
  "Security": {
    "AllowedRootDirectory": null
  }
}
```

## Key Design Decisions

- SQLite for simplicity (single-file database)
- Minimal API for reduced boilerplate
- Interface-based services for testability
- Manual test runners via command-line args
- Vanilla JS frontend (no build step required)
- Git integration for automatic checkpointing
- MCP protocol for Serena communication

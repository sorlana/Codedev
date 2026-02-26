# Technology Stack

## Backend

- .NET 10 SDK
- ASP.NET Core 10 Minimal API
- Entity Framework Core 10.0.3
- SQLite database (refactoring.db)

## Frontend

- Vanilla JavaScript (ES6+)
- HTML5/CSS3
- No framework dependencies

## External Dependencies

- Docker (for Serena MCP Server container)
- Git (for version control and checkpoints)
- Serena MCP Server (semantic C# code analysis)
- LLM Providers: OpenAI API or Ollama (local)

## Key Libraries

- Microsoft.EntityFrameworkCore.Sqlite (10.0.3)
- Microsoft.EntityFrameworkCore.InMemory (10.0.3) - for testing
- xunit (2.9.3) - testing framework
- Microsoft.NET.Test.Sdk (18.0.1)

## Common Commands

### Build and Run
```bash
# Restore dependencies
dotnet restore

# Build project
dotnet build

# Run application (default: http://localhost:5000)
dotnet run
```

### Database
```bash
# Create/update database
dotnet ef database update

# Create new migration
dotnet ef migrations add <MigrationName>
```

### Testing
```bash
# Run validation tests
dotnet run test-validation

# Run configuration tests
dotnet run test-config

# Run factory tests
dotnet run test-factory

# Run endpoint tests
dotnet run test-endpoint

# Run prompt processor tests
dotnet run test-promptprocessor
```

### Docker (Serena MCP)
```bash
# Start Serena container
docker run -d --name serena-container serena-mcp-image

# Check container status
docker ps | grep serena-container

# Execute Serena MCP command
docker exec -i serena-container serena-mcp
```

## Configuration Files

- `appsettings.json` - Main configuration (LLM, Serena, database)
- `appsettings.Development.json` - Development overrides
- `CSharpRefactoringAssistant.csproj` - Project file

## Platform

- Target: Windows (cmd shell)
- Cross-platform compatible via .NET 10

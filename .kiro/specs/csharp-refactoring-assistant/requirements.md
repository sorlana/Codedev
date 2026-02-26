# Requirements Document

## Introduction

This document specifies the requirements for an AI-assisted C# code refactoring tool. The system is a locally-running ASP.NET Core web application that enables developers to perform semantic code refactoring operations on C# projects through natural language commands. The application integrates with the Serena MCP Server for semantic code analysis and editing, uses LLM for natural language understanding, and provides automatic version control through Git checkpoints with rollback capabilities.

## Glossary

- **Application**: The AI-assisted C# code refactoring web application
- **Serena**: The MCP (Model Context Protocol) server that provides semantic analysis and editing capabilities for C# code
- **MCP_Client**: The JSON-RPC 2.0 client component that communicates with Serena over stdio
- **LLM**: Large Language Model (OpenAI API or Ollama) used for interpreting natural language prompts
- **Dialogue**: A conversation session associated with a specific C# project folder
- **Checkpoint**: A Git commit created automatically before code modifications
- **Project_Folder**: The absolute path to a C# ASP.NET Core project directory
- **User**: The developer using the application
- **Assistant**: The AI system responding to user prompts
- **Database**: SQLite database storing dialogues, messages, and checkpoint metadata

## Requirements

### Requirement 1: Dialogue Management

**User Story:** As a developer, I want to create and manage multiple dialogue sessions for different projects, so that I can work on multiple codebases independently.

#### Acceptance Criteria

1. WHEN a User creates a new dialogue with a valid Project_Folder path, THE Application SHALL create a new dialogue record in the Database
2. WHEN a User requests the list of dialogues, THE Application SHALL return all existing dialogues with their associated project paths
3. WHEN a User switches to an existing dialogue, THE Application SHALL load and display the complete message history for that dialogue
4. WHEN a User provides an invalid or non-existent Project_Folder path, THE Application SHALL reject the dialogue creation and return a descriptive error message
5. THE Application SHALL store each message with its role (user or assistant), timestamp, and associated dialogue identifier

### Requirement 2: Chat Interface and Message Processing

**User Story:** As a developer, I want to send natural language refactoring commands through a chat interface, so that I can modify my codebase without writing complex scripts.

#### Acceptance Criteria

1. WHEN a User submits a prompt through the chat interface, THE Application SHALL save the message to the Database with role "user"
2. WHEN a User message is saved, THE Application SHALL send the prompt to the LLM with Serena tool descriptions using function calling
3. WHEN the LLM returns tool calls, THE Application SHALL execute each tool call sequentially via the MCP_Client
4. WHEN all tool calls complete, THE Application SHALL format the results and save them as an assistant message in the Database
5. WHEN the assistant message is saved, THE Application SHALL return the response to the chat interface for display
6. WHEN any step in the processing pipeline fails, THE Application SHALL return a clear error message to the User

### Requirement 3: Serena MCP Integration

**User Story:** As a developer, I want the application to use Serena for semantic code analysis and editing, so that refactoring operations are accurate and preserve code semantics.

#### Acceptance Criteria

1. WHEN the Application starts, THE Application SHALL initialize a single MCP_Client connection to Serena that persists for all dialogues
2. WHEN a dialogue is created or switched, THE Application SHALL call mcp__serena__activate_project with the Project_Folder path
3. THE Application SHALL provide wrapper methods for these Serena tools: mcp__serena__find_symbol, mcp__serena__find_referencing_symbols, mcp__serena__replace_symbol_body, mcp__serena__execute_shell_command, read_file, insert_before_symbol, delete_lines
4. WHEN a Serena tool call fails, THE Application SHALL capture the error and return it to the User with context about which operation failed
5. WHEN the LLM requests a tool call, THE Application SHALL validate the tool name exists before attempting execution
6. THE MCP_Client SHALL communicate with Serena using JSON-RPC 2.0 protocol over stdio

### Requirement 4: Git Checkpoint System

**User Story:** As a developer, I want automatic Git checkpoints created before each refactoring operation, so that I can safely experiment with changes and rollback if needed.

#### Acceptance Criteria

1. WHEN a new dialogue is created with a Project_Folder, THE Application SHALL check if the folder is a Git repository
2. IF the Project_Folder is not a Git repository, THEN THE Application SHALL execute git init and create an initial commit with a standard .NET .gitignore file
3. WHEN a User submits a prompt that may modify files, THE Application SHALL create a Git commit before calling the LLM
4. THE Application SHALL generate commit messages in the format: "Checkpoint: {prompt description}"
5. WHEN a checkpoint is created, THE Application SHALL store the checkpoint metadata in the Database including dialogue ID, commit hash, description, and timestamp
6. WHEN checkpoint creation fails, THE Application SHALL abort the refactoring operation and return an error to the User

### Requirement 5: Rollback Functionality

**User Story:** As a developer, I want to rollback my codebase to any previous checkpoint, so that I can undo unwanted changes.

#### Acceptance Criteria

1. WHEN a User requests the list of checkpoints for a dialogue, THE Application SHALL return all checkpoints ordered by timestamp
2. WHEN a User initiates a rollback to a specific checkpoint, THE Application SHALL execute git reset --hard with the checkpoint's commit hash
3. WHEN a rollback completes successfully, THE Application SHALL call mcp__serena__activate_project to refresh Serena's view of the project
4. WHEN a rollback fails, THE Application SHALL return a descriptive error message and leave the repository in its current state
5. THE Application SHALL prevent rollback operations if there are uncommitted changes in the Project_Folder

### Requirement 6: LLM Integration with Function Calling

**User Story:** As a developer, I want the application to interpret my natural language commands and translate them into appropriate Serena tool calls, so that I don't need to learn Serena's API.

#### Acceptance Criteria

1. THE Application SHALL support OpenAI API (deepseek) as the primary LLM provider
2. WHERE Ollama is configured, THE Application SHALL support local models as a fallback LLM provider
3. WHEN sending a prompt to the LLM, THE Application SHALL include function definitions for all available Serena tools
4. WHEN the LLM response contains function calls, THE Application SHALL parse and execute them in the order specified
5. WHEN the LLM response contains no function calls, THE Application SHALL return the text response directly to the User
6. THE Application SHALL include dialogue history in LLM requests to maintain context across multiple turns

### Requirement 7: Data Persistence

**User Story:** As a developer, I want my dialogue history and checkpoints persisted locally, so that I can resume work across application restarts.

#### Acceptance Criteria

1. THE Application SHALL use SQLite with Entity Framework Core for data persistence
2. THE Application SHALL store dialogue entities with fields: ID, project path, creation timestamp
3. THE Application SHALL store message entities with fields: ID, dialogue ID, role, content, timestamp
4. THE Application SHALL store checkpoint entities with fields: ID, dialogue ID, commit hash, description, timestamp
5. WHEN the Application starts, THE Application SHALL initialize the Database schema if it does not exist
6. THE Application SHALL maintain referential integrity between dialogues, messages, and checkpoints

### Requirement 8: Security and Path Validation

**User Story:** As a developer, I want the application to validate project paths, so that it cannot access or modify files outside permitted directories.

#### Acceptance Criteria

1. WHEN a User provides a Project_Folder path, THE Application SHALL validate that the path is an absolute path
2. WHERE a root directory restriction is configured, THE Application SHALL reject paths outside the permitted root directory
3. WHEN validating paths, THE Application SHALL resolve symbolic links and relative path components before checking permissions
4. THE Application SHALL reject Project_Folder paths that do not exist or are not directories
5. THE Application SHALL sanitize all file paths before passing them to Serena or Git commands

### Requirement 9: Error Handling and User Feedback

**User Story:** As a developer, I want clear error messages when operations fail, so that I can understand what went wrong and how to fix it.

#### Acceptance Criteria

1. WHEN a Serena tool call fails, THE Application SHALL return an error message indicating which tool failed and why
2. WHEN a Git operation fails, THE Application SHALL return an error message with the Git command output
3. WHEN the LLM API call fails, THE Application SHALL return an error message indicating the LLM provider is unavailable
4. WHEN the MCP_Client loses connection to Serena, THE Application SHALL attempt to reconnect and notify the User
5. THE Application SHALL log all errors with timestamps and context for debugging purposes

### Requirement 10: Frontend Interface

**User Story:** As a developer, I want an intuitive web interface for interacting with the refactoring assistant, so that I can efficiently manage dialogues and send commands.

#### Acceptance Criteria

1. THE Application SHALL provide a web interface accessible via browser at localhost
2. THE Application SHALL display a list of existing dialogues with their project paths
3. WHEN displaying a dialogue, THE Application SHALL show the complete message history with clear visual distinction between user and assistant messages
4. THE Application SHALL provide an input field and send button for submitting new prompts
5. THE Application SHALL provide a button or interface element for creating new dialogues
6. THE Application SHALL provide a button or interface element for viewing and selecting checkpoints for rollback
7. WHEN processing a prompt, THE Application SHALL display a loading indicator to the User

### Requirement 11: Application Lifecycle and Serena Management

**User Story:** As a developer, I want the application to manage the Serena MCP server lifecycle efficiently, so that I don't experience delays or resource issues.

#### Acceptance Criteria

1. WHEN the Application starts, THE Application SHALL verify that Serena MCP server is running and accessible
2. THE Application SHALL maintain a single persistent connection to Serena throughout its lifetime
3. WHEN the Application shuts down, THE Application SHALL gracefully close the MCP_Client connection
4. IF Serena is not running at startup, THEN THE Application SHALL return a clear error message instructing the User to start Serena
5. THE Application SHALL reuse the same Serena connection for all dialogues to optimize performance

### Requirement 12: Build Verification

**User Story:** As a developer, I want the application to verify that my project still builds after refactoring operations, so that I can catch breaking changes immediately.

#### Acceptance Criteria

1. WHERE a User requests build verification, THE Application SHALL execute mcp__serena__execute_shell_command with "dotnet build"
2. WHEN a build command completes, THE Application SHALL capture and return the build output to the User
3. WHEN a build fails, THE Application SHALL format the error messages for readability in the chat interface
4. THE Application SHALL allow Users to optionally enable automatic build verification after each refactoring operation
5. WHEN automatic build verification is enabled and a build fails, THE Application SHALL notify the User but not automatically rollback

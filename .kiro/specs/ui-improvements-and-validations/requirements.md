# Requirements Document

## Introduction

This document specifies requirements for UI improvements and validations in the CSharpRefactoringAssistant application. The feature enhances user experience by adding model connection validation, improving project management interface, and providing manual checkpoint control.

## Glossary

- **Application**: The CSharpRefactoringAssistant system
- **Model**: An Ollama AI model used for code refactoring
- **Project**: A C# project directory managed by the Application
- **Checkpoint**: A saved state of project files that can be restored
- **Configuration**: Persistent application settings stored locally
- **Modal_Window**: A dialog window that appears over the main interface
- **Project_Selector**: A dropdown UI component for selecting projects

## Requirements

### Requirement 1: Model Connection Validation

**User Story:** As a user, I want to be notified when the selected model is unavailable at startup, so that I can take action to start the required model.

#### Acceptance Criteria

1. WHEN the Application starts AND a Model is configured in local settings, THE Application SHALL verify connection to the Model
2. IF the configured Model is unreachable, THEN THE Application SHALL display a message "Нет подключения к модели {model_name}, запустите модель в Ollama"
3. WHEN the connection check completes, THE Application SHALL log the connection status
4. THE Application SHALL perform the connection check before allowing user interactions with model-dependent features

### Requirement 2: Project Management Interface

**User Story:** As a user, I want to manage multiple projects through a visual interface, so that I can easily switch between projects and maintain my project list.

#### Acceptance Criteria

1. THE Application SHALL replace the text input field "Путь к проекту С#" with a Project_Selector dropdown
2. THE Application SHALL display a folder icon button next to the Project_Selector
3. WHEN the user clicks the folder icon button, THE Application SHALL open a Modal_Window displaying the project list
4. WHEN the Modal_Window is displayed, THE Application SHALL show each Project with its name and a delete button (trash icon)
5. WHEN the Modal_Window is displayed, THE Application SHALL show an "Добавить" button below the project list
6. WHEN the user clicks the "Добавить" button, THE Application SHALL open a folder selection dialog
7. WHEN the user selects a folder in the dialog, THE Application SHALL add the folder path to the project list
8. WHEN a Project is added, THE Application SHALL display it in both the Modal_Window list and the Project_Selector dropdown
9. WHEN the user clicks a delete button next to a Project, THE Application SHALL remove that Project from the list
10. WHEN the user selects a Project from the Project_Selector, THE Application SHALL persist this selection to Configuration
11. WHEN the Application starts, THE Application SHALL display the last selected Project as the default selection in the Project_Selector

### Requirement 3: Manual Checkpoint Management

**User Story:** As a user, I want to manually control when checkpoints are created, so that I can manage project state snapshots according to my workflow needs.

#### Acceptance Criteria

1. THE Application SHALL NOT create checkpoints automatically on file changes
2. THE Application SHALL NOT create checkpoints automatically on prompt submissions
3. THE Application SHALL display an "Добавить чекпойнт" button in the user interface
4. WHEN the user clicks the "Добавить чекпойнт" button, THE Application SHALL create a new Checkpoint of the current project state
5. THE Application SHALL provide a mechanism for users to restore previous Checkpoints manually
6. WHEN a Checkpoint is created, THE Application SHALL confirm the action to the user

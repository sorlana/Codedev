# Implementation Plan: Model Configuration UI

## Overview

This implementation plan breaks down the Model Configuration UI feature into discrete coding tasks. The approach follows a bottom-up strategy: first implementing backend services and data models, then API endpoints, and finally the frontend UI. Each task builds incrementally, with property-based tests integrated close to implementation to catch errors early.

## Tasks

- [x] 1. Set up data models and configuration infrastructure
  - [x] 1.1 Create LlmConfiguration data models
    - Create `LlmConfiguration`, `ProviderSettings`, and `OllamaSettings` classes in Models directory
    - Create request/response models: `SaveConfigurationRequest`, `ConfigurationResponse`, `OllamaModelsResponse`, `TestConnectionResponse`
    - _Requirements: 4.4, 4.5_
  
  - [x] 1.2 Create IConfigurationService interface
    - Define interface with `GetConfigurationAsync`, `SaveConfigurationAsync`, and `ValidateConfigurationAsync` methods
    - _Requirements: 4.1, 4.2_

- [ ] 2. Implement ConfigurationService
  - [x] 2.1 Implement configuration reading from appsettings.json
    - Implement `GetConfigurationAsync` to read current Llm section from IConfiguration
    - Map configuration values to LlmConfiguration DTO
    - _Requirements: 4.2_
  
  - [x] 2.2 Implement configuration validation logic
    - Implement `ValidateConfigurationAsync` with required field checks
    - Add URL format validation for base URLs
    - Validate provider-specific required fields (API key for OpenAI, base URL for Ollama)
    - _Requirements: 2.5, 2.6, 3.3, 3.4, 5.1, 5.2_
  
  - [ ]* 2.3 Write property test for required field validation
    - **Property 5: Required Field Validation**
    - **Validates: Requirements 2.5, 3.3**
  
  - [ ]* 2.4 Write property test for URL format validation
    - **Property 6: URL Format Validation**
    - **Validates: Requirements 2.6, 3.4**
  
  - [x] 2.5 Implement configuration persistence to appsettings.json
    - Implement `SaveConfigurationAsync` to read, update, and write appsettings.json
    - Use JSON serialization to update Llm section
    - Implement file locking for concurrent write protection
    - _Requirements: 4.1, 4.3_
  
  - [ ]* 2.6 Write property test for configuration persistence round-trip
    - **Property 1: Configuration Persistence Round-Trip**
    - **Validates: Requirements 4.1, 4.2, 4.4, 4.5, 7.2**
  
  - [ ]* 2.7 Write property test for API key removal
    - **Property 13: API Key Removal**
    - **Validates: Requirements 7.4**

- [ ] 3. Implement OllamaLlmService
  - [x] 3.1 Create OllamaLlmService class implementing ILlmService
    - Implement constructor reading Ollama configuration from IConfiguration
    - Implement `SendPromptAsync` method for Ollama API
    - Build Ollama-compatible request format
    - Parse Ollama response and map to LlmResponse
    - Handle tool calls if supported by Ollama
    - _Requirements: 6.2_
  
  - [ ]* 3.2 Write unit tests for OllamaLlmService
    - Test request building with various message histories
    - Test response parsing with mock Ollama responses
    - Test error handling for connection failures
    - _Requirements: 6.2_

- [ ] 4. Implement LlmServiceFactory
  - [x] 4.1 Create ILlmServiceFactory interface and implementation
    - Define interface with `CreateLlmService` method
    - Implement factory reading Provider from configuration
    - Implement service creation logic for OpenAI and Ollama providers
    - Add default fallback to OpenAI service
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5_
  
  - [ ]* 4.2 Write property test for factory type selection
    - **Property 11: LLM Service Factory Type Selection**
    - **Validates: Requirements 6.1, 6.2, 6.4**
  
  - [ ]* 4.3 Write property test for service configuration propagation
    - **Property 12: Service Configuration Propagation**
    - **Validates: Requirements 6.3**
  
  - [ ]* 4.4 Write unit test for default configuration fallback
    - Test factory behavior when no valid configuration exists
    - Verify default OpenAI service is created
    - _Requirements: 6.5_

- [ ] 5. Checkpoint - Ensure backend services tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. Implement configuration API endpoints
  - [x] 6.1 Add GET /api/configuration endpoint
    - Create endpoint in Program.cs
    - Call ConfigurationService.GetConfigurationAsync
    - Return ConfigurationResponse with current settings
    - Handle errors and return appropriate status codes
    - _Requirements: 1.4_
  
  - [x] 6.2 Add POST /api/configuration endpoint
    - Create endpoint accepting SaveConfigurationRequest
    - Validate configuration using ConfigurationService
    - Save configuration if valid
    - Return success/error response
    - Handle validation errors with 400 Bad Request
    - _Requirements: 2.7, 3.5, 4.1, 5.3, 5.4_
  
  - [ ]* 6.3 Write property test for active configuration update
    - **Property 7: Active Configuration Update**
    - **Validates: Requirements 2.7, 3.5**
  
  - [ ]* 6.4 Write property test for configuration changes apply immediately
    - **Property 8: Configuration Changes Apply Immediately**
    - **Validates: Requirements 4.3**
  
  - [x] 6.5 Add GET /api/configuration/ollama/models endpoint
    - Create endpoint to fetch models from Ollama instance
    - Read Ollama base URL from configuration
    - Make HTTP request to Ollama API to list models
    - Parse response and return model names
    - Handle connection errors gracefully
    - _Requirements: 3.6_
  
  - [x] 6.6 Add POST /api/configuration/test endpoint
    - Create endpoint accepting configuration to test
    - Attempt to create LLM service with provided configuration
    - Send simple test prompt to LLM
    - Return TestConnectionResponse with success/failure
    - _Requirements: 5.5_
  
  - [ ]* 6.7 Write integration tests for configuration API endpoints
    - Test GET /api/configuration returns current settings
    - Test POST /api/configuration with valid data
    - Test POST /api/configuration with invalid data returns 400
    - Test GET /api/configuration/ollama/models with mock Ollama
    - Test POST /api/configuration/test with valid configuration
    - _Requirements: 1.4, 2.7, 3.5, 3.6, 5.5_

- [ ] 7. Update Program.cs to use LlmServiceFactory
  - [x] 7.1 Register services in dependency injection
    - Register IConfigurationService as singleton
    - Register ILlmServiceFactory as singleton
    - Update ILlmService registration to use factory
    - _Requirements: 6.1, 6.2, 6.3_
  
  - [x] 7.2 Ensure PromptProcessor uses factory-created LLM service
    - Verify PromptProcessor receives ILlmService from DI
    - Test that configuration changes are reflected in service behavior
    - _Requirements: 6.3, 4.3_

- [ ] 8. Checkpoint - Ensure backend integration tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Create frontend UI structure
  - [x] 9.1 Add model configuration modal HTML to index.html
    - Create modal container with overlay
    - Add tab buttons for Provider and Local tabs
    - Create Provider tab form with API key, base URL, and model inputs
    - Create Local tab form with Ollama URL and model selection
    - Add save buttons and status message area
    - Add button/link in main interface to open configuration modal
    - _Requirements: 1.1, 2.1, 2.2, 2.3, 3.1, 3.2, 8.1_
  
  - [ ]* 9.2 Write unit test for UI element presence
    - Test that modal contains Provider and Local tabs
    - Test that Provider tab has required input fields
    - Test that Local tab has required input fields
    - Test that configuration is accessible from main interface
    - _Requirements: 1.1, 2.1, 2.2, 2.3, 3.1, 3.2, 8.1, 8.5_
  
  - [x] 9.3 Add CSS styling for configuration modal
    - Style modal overlay and container
    - Style tabs and tab switching
    - Style form inputs and buttons
    - Add focus states and hover effects
    - Make responsive for different screen sizes
    - _Requirements: 8.2, 8.3_

- [ ] 10. Implement frontend configuration logic
  - [x] 10.1 Implement tab switching functionality
    - Add event listeners for tab buttons
    - Show/hide tab content based on active tab
    - Preserve input values when switching tabs
    - _Requirements: 1.2_
  
  - [ ]* 10.2 Write property test for tab state preservation
    - **Property 2: Tab State Preservation**
    - **Validates: Requirements 1.2**
  
  - [x] 10.3 Implement loadConfiguration function
    - Fetch configuration from GET /api/configuration
    - Populate form fields with loaded values
    - Set active tab based on provider type
    - Display current active configuration
    - _Requirements: 1.4_
  
  - [ ]* 10.4 Write property test for configuration display on load
    - **Property 3: Configuration Display on Load**
    - **Validates: Requirements 1.4**
  
  - [x] 10.5 Implement API key masking
    - Set input type to "password" for API key field
    - Ensure API key is masked when displayed
    - _Requirements: 2.4, 7.1_
  
  - [ ]* 10.6 Write property test for API key masking
    - **Property 4: API Key Masking**
    - **Validates: Requirements 2.4, 7.1**
  
  - [x] 10.7 Implement client-side validation
    - Validate required fields before submission
    - Validate URL format for base URLs
    - Display validation error messages
    - Highlight missing or invalid fields
    - _Requirements: 5.1, 5.2_
  
  - [ ]* 10.8 Write property test for validation error display
    - **Property 9: Validation Error Display**
    - **Validates: Requirements 5.1, 5.2**

- [ ] 11. Implement configuration save functionality
  - [x] 11.1 Implement saveConfiguration function
    - Collect form data from active tab
    - Build SaveConfigurationRequest object
    - Send POST request to /api/configuration
    - Display loading indicator during save
    - Handle success and error responses
    - _Requirements: 2.7, 3.5, 5.3, 5.4, 8.4_
  
  - [ ]* 11.2 Write property test for save feedback messaging
    - **Property 10: Save Feedback Messaging**
    - **Validates: Requirements 5.3, 5.4**
  
  - [ ]* 11.3 Write property test for loading indicator during save
    - **Property 14: Loading Indicator During Save**
    - **Validates: Requirements 8.4**
  
  - [x] 11.4 Implement Ollama model fetching
    - Add "Refresh Models" button handler
    - Fetch models from GET /api/configuration/ollama/models
    - Populate model dropdown with fetched models
    - Handle connection errors gracefully
    - _Requirements: 3.6_
  
  - [ ]* 11.5 Write unit test for Ollama model fetching
    - Test model list population with mock API response
    - Test error handling when Ollama is not accessible
    - _Requirements: 3.6_
  
  - [x] 11.6 Implement test connection functionality
    - Add "Test Connection" button to both tabs
    - Send POST request to /api/configuration/test
    - Display success or failure message
    - _Requirements: 5.5_
  
  - [ ]* 11.7 Write unit test for test connection feature
    - Test success message display with mock success response
    - Test error message display with mock failure response
    - _Requirements: 5.5_

- [x] 12. Wire up modal open/close functionality
  - [x] 12.1 Add event listeners for opening configuration modal
    - Add button/link in main interface to open modal
    - Show modal and load current configuration when opened
    - _Requirements: 8.1_
  
  - [x] 12.2 Add event listeners for closing configuration modal
    - Add close button to modal
    - Close modal on overlay click
    - Close modal on Escape key press
    - _Requirements: 8.1_

- [ ] 13. Final integration and testing
  - [x] 13.1 Test end-to-end configuration flow
    - Open configuration modal from main interface
    - Enter provider configuration and save
    - Verify configuration persists to appsettings.json
    - Reload application and verify configuration is loaded
    - Send LLM request and verify correct service is used
    - Switch to local configuration and repeat
    - _Requirements: All_
  
  - [ ]* 13.2 Write integration test for complete configuration flow
    - Test configuration creation, persistence, loading, and service usage
    - _Requirements: All_

- [ ] 14. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at key milestones
- Property tests validate universal correctness properties using FsCheck or CsCheck
- Unit tests validate specific examples, edge cases, and integration points
- The implementation follows a bottom-up approach: backend services → API → frontend
- Configuration changes apply immediately without requiring application restart
- API keys are masked in the UI for security
- The factory pattern enables flexible LLM provider switching

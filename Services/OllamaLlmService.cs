using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public class OllamaLlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly ILogger<OllamaLlmService> _logger;

    public OllamaLlmService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OllamaLlmService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = configuration["Llm:Ollama:Model"] ?? throw new ArgumentException("Ollama model not configured");
    }

    public async Task<LlmResponse> SendPromptAsync(
        string prompt,
        List<Message> history,
        List<FunctionDefinition> tools)
    {
        try
        {
            _logger.LogInformation("=== Ollama Request Start ===");
            _logger.LogInformation("Model: {Model}", _model);
            _logger.LogInformation("Prompt: {Prompt}", prompt);
            _logger.LogInformation("History count: {Count}", history.Count);
            _logger.LogInformation("Tools count: {Count}", tools.Count);
            
            // Build messages array
            var messages = new List<object>();
            
            foreach (var msg in history)
            {
                messages.Add(new
                {
                    role = msg.Role,
                    content = msg.Content
                });
            }

            messages.Add(new
            {
                role = "user",
                content = prompt
            });

            // Build Ollama request
            var requestBody = new
            {
                model = _model,
                messages = messages,
                stream = false,
                tools = tools.Count > 0 ? tools.Select(t => new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = t.Parameters
                    }
                }).ToArray() : null
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            
            _logger.LogInformation("Request JSON length: {Length}", requestJson.Length);
            _logger.LogDebug("Request JSON: {Json}", requestJson);
            
            var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", requestContent);
            
            _logger.LogInformation("Response status: {StatusCode}", response.StatusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Ollama API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new LlmException($"Ollama API error: {response.StatusCode} - {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Ollama response received, length: {Length}", responseJson.Length);
            _logger.LogDebug("Response JSON: {Json}", responseJson);
            
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var llmResponse = new LlmResponse();

            // Parse Ollama response
            if (responseData.TryGetProperty("message", out var message))
            {
                // DeepSeek-R1 может возвращать и текст (рассуждения), и tool calls одновременно
                // Сначала извлекаем текстовое содержимое
                if (message.TryGetProperty("content", out var contentProp))
                {
                    var content = contentProp.GetString();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        llmResponse.TextContent = content;
                        _logger.LogInformation("Response has text content, length: {Length}", content.Length);
                    }
                }

                // Затем проверяем наличие tool calls
                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    _logger.LogInformation("Found {Count} tool calls in response", toolCalls.GetArrayLength());
                    llmResponse.FunctionCalls = new List<FunctionCall>();
                    
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var function = toolCall.GetProperty("function");
                        var functionName = function.GetProperty("name").GetString() ?? string.Empty;
                        
                        _logger.LogInformation("Parsing tool call: {FunctionName}", functionName);
                        
                        // Ollama returns arguments as an object, not a JSON string
                        var arguments = new Dictionary<string, object>();
                        if (function.TryGetProperty("arguments", out var argsElement))
                        {
                            arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsElement.GetRawText())
                                ?? new Dictionary<string, object>();
                            _logger.LogInformation("Parsed {Count} arguments for {FunctionName}", arguments.Count, functionName);
                        }

                        llmResponse.FunctionCalls.Add(new FunctionCall
                        {
                            Name = functionName,
                            Arguments = arguments
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning("Response does not contain 'message' property");
            }

            _logger.LogInformation("=== Ollama Request End ===");
            return llmResponse;
        }
        catch (Exception ex) when (ex is not LlmException)
        {
            _logger.LogError(ex, "Error sending prompt to Ollama");
            throw new LlmException("Error sending prompt to Ollama", ex);
        }
    }
}

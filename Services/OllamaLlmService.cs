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
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Ollama API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new LlmException($"Ollama API error: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var llmResponse = new LlmResponse();

            // Parse Ollama response
            if (responseData.TryGetProperty("message", out var message))
            {
                // Check for tool calls
                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    llmResponse.FunctionCalls = new List<FunctionCall>();
                    
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var function = toolCall.GetProperty("function");
                        var functionName = function.GetProperty("name").GetString() ?? string.Empty;
                        
                        // Ollama returns arguments as an object, not a JSON string
                        var arguments = new Dictionary<string, object>();
                        if (function.TryGetProperty("arguments", out var argsElement))
                        {
                            arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsElement.GetRawText())
                                ?? new Dictionary<string, object>();
                        }

                        llmResponse.FunctionCalls.Add(new FunctionCall
                        {
                            Name = functionName,
                            Arguments = arguments
                        });
                    }
                }
                // Check for text content
                else if (message.TryGetProperty("content", out var contentProp))
                {
                    llmResponse.TextContent = contentProp.GetString();
                }
            }

            return llmResponse;
        }
        catch (Exception ex) when (ex is not LlmException)
        {
            _logger.LogError(ex, "Error sending prompt to Ollama");
            throw new LlmException("Error sending prompt to Ollama", ex);
        }
    }
}

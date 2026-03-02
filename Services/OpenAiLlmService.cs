using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public class OpenAiLlmService : ILlmService, IStreamingLlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly ILogger<OpenAiLlmService> _logger;

    public OpenAiLlmService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAiLlmService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(10); // Увеличенный таймаут для генерации детальных планов
        _logger = logger;
        _apiKey = configuration["Llm:OpenAI:ApiKey"] ?? throw new ArgumentException("OpenAI API key not configured");
        _model = configuration["Llm:OpenAI:Model"] ?? "deepseek-chat";
        _baseUrl = configuration["Llm:OpenAI:BaseUrl"] ?? "https://api.deepseek.com/v1";
    }

    public async Task<LlmResponse> SendPromptAsync(
        string prompt,
        List<Message> history,
        List<FunctionDefinition> tools,
        bool forceJson = false)
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

            // Build request
            var requestBody = new
            {
                model = _model,
                messages = messages,
                response_format = forceJson ? new { type = "json_object" } : null,
                tools = tools.Select(t => new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = t.Parameters
                    }
                }).ToArray()
            };

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new LlmException($"OpenAI API error: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var llmResponse = new LlmResponse();

            // Parse response
            if (responseData.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");

                // Check for tool calls
                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    llmResponse.FunctionCalls = new List<FunctionCall>();
                    
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var function = toolCall.GetProperty("function");
                        var functionName = function.GetProperty("name").GetString() ?? string.Empty;
                        var argumentsJson = function.GetProperty("arguments").GetString() ?? "{}";
                        
                        var arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson)
                            ?? new Dictionary<string, object>();

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
            _logger.LogError(ex, "Error sending prompt to OpenAI");
            throw new LlmException("Error sending prompt to OpenAI", ex);
        }
    }

    public async IAsyncEnumerable<string> StreamPromptAsync(
        string prompt,
        List<Message> history,
        List<FunctionDefinition> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== OpenAI Streaming Request Start ===");
        _logger.LogInformation("Model: {Model}", _model);
        _logger.LogInformation("Prompt length: {Length}", prompt.Length);
        
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

        // Build request with stream=true
        var requestBody = new
        {
            model = _model,
            messages = messages,
            stream = true,
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
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("OpenAI API streaming error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            throw new LlmException($"OpenAI API streaming error: {response.StatusCode}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // SSE формат: "data: {json}" или "data: [DONE]"
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
            {
                continue;
            }

            var data = line.Substring(6); // Убираем "data: "
            
            if (data == "[DONE]")
            {
                _logger.LogInformation("Streaming completed");
                break;
            }

            // Парсинг JSON без try-catch (ошибки будут выброшены наружу)
            JsonElement chunk;
            if (!JsonHelper.TryDeserialize(data, out chunk))
            {
                _logger.LogWarning("Failed to parse streaming chunk: {Data}", data);
                continue;
            }
            
            if (chunk.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                
                if (firstChoice.TryGetProperty("delta", out var delta))
                {
                    if (delta.TryGetProperty("content", out var contentProp))
                    {
                        var contentChunk = contentProp.GetString();
                        if (!string.IsNullOrEmpty(contentChunk))
                        {
                            _logger.LogDebug("Yielding chunk: {Length} chars", contentChunk.Length);
                            yield return contentChunk;
                        }
                    }
                }
            }
        }
        
        _logger.LogInformation("=== OpenAI Streaming Request End ===");
    }
}

public class LlmException : Exception
{
    public LlmException(string message) : base(message) { }
    public LlmException(string message, Exception innerException) : base(message, innerException) { }
}

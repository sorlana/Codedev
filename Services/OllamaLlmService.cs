using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public class OllamaLlmService : ILlmService, IStreamingLlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _reasoningModel;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OllamaLlmService> _logger;

    public OllamaLlmService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OllamaLlmService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromMinutes(10); // Увеличенный таймаут для генерации детальных планов
        _logger = logger;
        _configuration = configuration;
        _baseUrl = configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = configuration["Llm:Ollama:Model"] ?? throw new ArgumentException("Ollama model not configured");
        _reasoningModel = configuration["Llm:Ollama:ReasoningModel"];
        
        _logger.LogInformation("OllamaLlmService инициализирован: Model={Model}, ReasoningModel={ReasoningModel}", 
            _model, _reasoningModel);
    }

    public async Task<LlmResponse> SendPromptAsync(
        string prompt,
        List<Message> history,
        List<FunctionDefinition> tools,
        bool forceJson = false)
    {
        // Читаем актуальное значение конфигурации при каждом запросе
        var useDeepSeekApiStr = _configuration["Llm:UseDeepSeekApi"];
        var deepSeekApiKey = _configuration["Llm:DeepSeek:ApiKey"];
        var deepSeekBaseUrl = _configuration["Llm:DeepSeek:BaseUrl"] ?? "https://api.deepseek.com";
        var deepSeekChatModel = _configuration["Llm:DeepSeek:ChatModel"] ?? "deepseek-chat";
        
        _logger.LogWarning("=== КОНФИГУРАЦИЯ DEBUG ===");
        _logger.LogWarning("UseDeepSeekApi (строка): '{Value}'", useDeepSeekApiStr ?? "NULL");
        _logger.LogWarning("DeepSeekApiKey: '{Value}'", string.IsNullOrEmpty(deepSeekApiKey) ? "ПУСТО" : "ЕСТЬ");
        
        var useDeepSeekApi = bool.Parse(useDeepSeekApiStr ?? "false");
        _logger.LogWarning("UseDeepSeekApi (bool): {Value}", useDeepSeekApi);
        
        // Если включен DeepSeek API, используем его
        if (useDeepSeekApi && !string.IsNullOrEmpty(deepSeekApiKey))
        {
            _logger.LogWarning(">>> ИСПОЛЬЗУЕТСЯ DEEPSEEK API <<<");
            return await SendDeepSeekApiRequestAsync(prompt, history, tools, deepSeekApiKey, deepSeekBaseUrl, deepSeekChatModel);
        }
        
        _logger.LogWarning(">>> ИСПОЛЬЗУЕТСЯ ЛОКАЛЬНАЯ OLLAMA <<<");
        
        // Если настроена reasoning модель, используем её для чата
        var modelToUse = !string.IsNullOrEmpty(_reasoningModel) ? _reasoningModel : _model;
        
        try
        {
            _logger.LogInformation("=== Ollama Request Start ===");
            _logger.LogInformation("Model: {Model}", modelToUse);
            _logger.LogInformation("Prompt: {Prompt}", prompt);
            _logger.LogInformation("History count: {Count}", history?.Count ?? 0);
            _logger.LogInformation("Tools count: {Count}", tools?.Count ?? 0);
            
            // Build messages array
            var messages = new List<object>();
            
            if (history != null && history.Count > 0)
            {
                foreach (var msg in history)
                {
                    messages.Add(new
                    {
                        role = msg.Role,
                        content = msg.Content
                    });
                }
            }

            messages.Add(new
            {
                role = "user",
                content = prompt
            });

            // Build Ollama request
            var requestBody = new
            {
                model = modelToUse,
                messages = messages,
                stream = false,
                format = forceJson ? "json" : (string?)null,
                tools = tools != null && tools.Count > 0 ? tools.Select(t => new
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

    public async IAsyncEnumerable<string> StreamPromptAsync(
        string prompt,
        List<Message> history,
        List<FunctionDefinition> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== Ollama Streaming Request Start ===");
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

        // Build Ollama request with stream=true
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
        
        var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = requestContent
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Ollama API streaming error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            throw new LlmException($"Ollama API streaming error: {response.StatusCode} - {errorContent}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Парсинг JSON без try-catch (ошибки будут выброшены наружу)
            JsonElement chunk;
            if (!JsonHelper.TryDeserialize(line, out chunk))
            {
                _logger.LogWarning("Failed to parse streaming chunk: {Line}", line);
                continue;
            }
            
            // Проверяем флаг завершения
            if (chunk.TryGetProperty("done", out var done) && done.GetBoolean())
            {
                _logger.LogInformation("Streaming completed");
                break;
            }
            
            // Извлекаем содержимое из message.content
            if (chunk.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var contentProp))
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
        
        _logger.LogInformation("=== Ollama Streaming Request End ===");
    }

    /// <summary>
    /// Отправляет запрос к DeepSeek API для чата
    /// </summary>
    private async Task<LlmResponse> SendDeepSeekApiRequestAsync(
        string prompt,
        List<Message> history,
        List<FunctionDefinition> tools,
        string apiKey,
        string baseUrl,
        string chatModel)
    {
        try
        {
            _logger.LogInformation("=== DeepSeek API Request Start ===");
            _logger.LogInformation("BaseUrl: {BaseUrl}", baseUrl);
            _logger.LogInformation("Model: {Model}", chatModel);
            _logger.LogWarning("=== HISTORY DEBUG ===");
            _logger.LogWarning("History count: {Count}", history?.Count ?? 0);
            
            // Ограничиваем историю последними 4 сообщениями (2 пары user-assistant)
            // чтобы избежать повторения паттернов
            var limitedHistory = history != null && history.Count > 4 
                ? history.Skip(history.Count - 4).ToList() 
                : history;
            
            _logger.LogWarning("Limited history count: {Count}", limitedHistory?.Count ?? 0);
            if (limitedHistory != null)
            {
                for (int i = 0; i < limitedHistory.Count; i++)
                {
                    _logger.LogWarning("LimitedHistory[{Index}]: Role={Role}, Content={Content}", 
                        i, limitedHistory[i].Role, 
                        limitedHistory[i].Content.Length > 100 ? limitedHistory[i].Content.Substring(0, 100) + "..." : limitedHistory[i].Content);
                }
            }
            _logger.LogWarning("=== END HISTORY ===");
            
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            // Build messages array
            var messages = new List<object>();
            
            if (limitedHistory != null && limitedHistory.Count > 0)
            {
                foreach (var msg in limitedHistory)
                {
                    // Проверяем наличие tool_calls (для assistant messages)
                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        messages.Add(new
                        {
                            role = msg.Role,
                            content = msg.Content,
                            tool_calls = msg.ToolCalls
                        });
                    }
                    // Проверяем наличие tool_call_id (для tool messages)
                    else if (!string.IsNullOrEmpty(msg.ToolCallId))
                    {
                        messages.Add(new
                        {
                            role = msg.Role,
                            content = msg.Content,
                            tool_call_id = msg.ToolCallId
                        });
                    }
                    else
                    {
                        messages.Add(new
                        {
                            role = msg.Role,
                            content = msg.Content
                        });
                    }
                }
            }

            // Добавляем текущий промпт только если он не пустой
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                messages.Add(new
                {
                    role = "user",
                    content = prompt
                });
            }

            // Build DeepSeek request
            // Используем deepseek-chat для обычного чата (быстрее и дешевле)
            // Передаем tools если они есть, чтобы модель могла анализировать код
            object requestBody;
            
            if (tools != null && tools.Count > 0)
            {
                requestBody = new
                {
                    model = chatModel,
                    messages = messages,
                    temperature = 0.7,
                    max_tokens = 4096,
                    tools = tools.Select(t => new
                    {
                        type = "function",
                        function = new
                        {
                            name = t.Name,
                            description = t.Description,
                            parameters = t.Parameters
                        }
                    }).ToList()
                };
            }
            else
            {
                requestBody = new
                {
                    model = chatModel,
                    messages = messages,
                    temperature = 0.7,
                    max_tokens = 4096
                };
            }

            var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            
            _logger.LogInformation("DeepSeek Request JSON length: {Length}", requestJson.Length);
            
            var requestContent = new StringContent(requestJson, Encoding.UTF8, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"));

            var response = await httpClient.PostAsync($"{baseUrl}/v1/chat/completions", requestContent);
            
            _logger.LogInformation("DeepSeek Response status: {StatusCode}", response.StatusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("DeepSeek API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new LlmException($"DeepSeek API error: {response.StatusCode} - {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("DeepSeek response received, length: {Length}", responseJson.Length);
            _logger.LogWarning("=== DEEPSEEK RESPONSE CONTENT ===");
            _logger.LogWarning("{Content}", responseJson);
            _logger.LogWarning("=== END DEEPSEEK RESPONSE ===");
            
            var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var llmResponse = new LlmResponse();

            // Parse DeepSeek response
            if (responseData.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    // Извлекаем текстовое содержимое
                    if (message.TryGetProperty("content", out var contentProp))
                    {
                        var content = contentProp.GetString();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            llmResponse.TextContent = content;
                            _logger.LogInformation("DeepSeek response has text content, length: {Length}", content.Length);
                            _logger.LogWarning("=== DEEPSEEK TEXT CONTENT ===");
                            _logger.LogWarning("{Content}", content);
                            _logger.LogWarning("=== END TEXT CONTENT ===");
                        }
                    }

                    // Проверяем наличие tool calls
                    if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                    {
                        _logger.LogInformation("Found {Count} tool calls in DeepSeek response", toolCalls.GetArrayLength());
                        llmResponse.FunctionCalls = new List<FunctionCall>();
                        
                        foreach (var toolCall in toolCalls.EnumerateArray())
                        {
                            var function = toolCall.GetProperty("function");
                            var functionName = function.GetProperty("name").GetString() ?? string.Empty;
                            
                            _logger.LogInformation("Parsing tool call: {FunctionName}", functionName);
                            
                            var arguments = new Dictionary<string, object>();
                            if (function.TryGetProperty("arguments", out var argsString))
                            {
                                var argsJson = argsString.GetString() ?? "{}";
                                var argsElement = JsonSerializer.Deserialize<JsonElement>(argsJson);
                                
                                foreach (var prop in argsElement.EnumerateObject())
                                {
                                    arguments[prop.Name] = prop.Value.ValueKind switch
                                    {
                                        JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                                        JsonValueKind.Number => prop.Value.GetDouble(),
                                        JsonValueKind.True => true,
                                        JsonValueKind.False => false,
                                        _ => prop.Value.ToString()
                                    };
                                }
                            }

                            llmResponse.FunctionCalls.Add(new FunctionCall
                            {
                                Name = functionName,
                                Arguments = arguments
                            });
                        }
                    }
                }
            }

            _logger.LogInformation("=== DeepSeek API Request End ===");
            return llmResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling DeepSeek API");
            throw new LlmException($"DeepSeek API error: {ex.Message}", ex);
        }
    }
}


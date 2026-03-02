using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис-оркестратор для управления multi-turn tool calling с DeepSeek API
/// Реализует паттерн из официальной документации DeepSeek
/// </summary>
public class DeepSeekOrchestratorService : IDeepSeekOrchestratorService
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<DeepSeekOrchestratorService> _logger;
    private readonly IWebSocketManager _webSocketManager;

    public DeepSeekOrchestratorService(
        IConfigurationService configService,
        ILogger<DeepSeekOrchestratorService> logger,
        IWebSocketManager webSocketManager)
    {
        _configService = configService;
        _logger = logger;
        _webSocketManager = webSocketManager;
    }

    public async Task<OrchestratorResult> ExecuteTurnAsync(
        int dialogueId,
        List<object> messages,
        List<object> tools,
        Func<string, string, Task<string>> onToolCall,
        int maxSubTurns = 15)
    {
        var result = new OrchestratorResult
        {
            Success = true,
            UpdatedMessages = messages
        };

        try
        {
            var config = await _configService.GetConfigurationAsync();
            
            if (!config.UseDeepSeekApi || config.DeepSeek == null || string.IsNullOrEmpty(config.DeepSeek.ApiKey))
            {
                throw new Exception("DeepSeek API не настроен или отключен");
            }

            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.DeepSeek.ApiKey}");

            int subTurn = 1;

            while (subTurn <= maxSubTurns)
            {
                _logger.LogInformation("Turn {DialogueId}, Sub-turn {SubTurn}: отправка запроса к DeepSeek API", 
                    dialogueId, subTurn);

                // Отправляем прогресс в UI
                await _webSocketManager.BroadcastToDialogueAsync(dialogueId, new WebSocketMessage
                {
                    Type = "task_execution_progress",
                    Payload = new TaskExecutionProgressPayload
                    {
                        Current = subTurn,
                        Total = maxSubTurns,
                        Message = $"Обработка запроса {subTurn}/{maxSubTurns}..."
                    }
                });

                // Формируем запрос к DeepSeek API
                var requestBody = new
                {
                    model = config.DeepSeek.ChatModel,
                    messages = messages,
                    tools = tools,
                    temperature = 0.7,
                    max_tokens = 4096,
                    extra_body = new
                    {
                        thinking = new { type = "enabled" }
                    }
                };

                var requestJson = JsonSerializer.Serialize(requestBody);
                var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(
                    $"{config.DeepSeek.BaseUrl}/v1/chat/completions",
                    requestContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("DeepSeek API error: {Error}", errorContent);
                    throw new Exception($"DeepSeek API error: {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseJson);

                if (!responseData.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    throw new Exception("DeepSeek API вернул пустой ответ");
                }

                var firstChoice = choices[0];
                var message = firstChoice.GetProperty("message");
                
                // Извлекаем reasoning_content (если есть)
                string? reasoningContent = null;
                if (message.TryGetProperty("reasoning_content", out var reasoningElement))
                {
                    reasoningContent = reasoningElement.GetString();
                    _logger.LogInformation("Reasoning content получен (длина: {Length})", 
                        reasoningContent?.Length ?? 0);
                }

                // Извлекаем content
                string? content = null;
                if (message.TryGetProperty("content", out var contentElement))
                {
                    content = contentElement.GetString();
                }

                // Добавляем ответ ассистента в историю (включая reasoning_content)
                var assistantMessage = new Dictionary<string, object>
                {
                    ["role"] = "assistant"
                };

                if (!string.IsNullOrEmpty(reasoningContent))
                {
                    assistantMessage["reasoning_content"] = reasoningContent;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    assistantMessage["content"] = content;
                }

                // Проверяем наличие tool_calls
                if (message.TryGetProperty("tool_calls", out var toolCallsElement))
                {
                    var toolCallsList = new List<object>();
                    
                    foreach (var toolCall in toolCallsElement.EnumerateArray())
                    {
                        toolCallsList.Add(new Dictionary<string, object>
                        {
                            ["id"] = toolCall.GetProperty("id").GetString()!,
                            ["type"] = toolCall.GetProperty("type").GetString()!,
                            ["function"] = new Dictionary<string, object>
                            {
                                ["name"] = toolCall.GetProperty("function").GetProperty("name").GetString()!,
                                ["arguments"] = toolCall.GetProperty("function").GetProperty("arguments").GetString()!
                            }
                        });
                    }
                    
                    assistantMessage["tool_calls"] = toolCallsList;
                }

                messages.Add(assistantMessage);

                // Проверяем причину остановки
                var finishReason = firstChoice.GetProperty("finish_reason").GetString();
                
                _logger.LogInformation("Finish reason: {FinishReason}", finishReason);

                if (finishReason == "tool_calls")
                {
                    // Модель хочет вызвать инструменты
                    if (message.TryGetProperty("tool_calls", out var toolCalls))
                    {
                        var toolResults = new List<object>();

                        foreach (var toolCall in toolCalls.EnumerateArray())
                        {
                            var toolCallId = toolCall.GetProperty("id").GetString();
                            var function = toolCall.GetProperty("function");
                            var functionName = function.GetProperty("name").GetString();
                            var argumentsJson = function.GetProperty("arguments").GetString();

                            _logger.LogInformation("Sub-turn {SubTurn}: вызов инструмента {FunctionName}", 
                                subTurn, functionName);

                            // Выполняем инструмент через callback
                            var toolResult = await onToolCall(functionName!, argumentsJson!);

                            // Добавляем результат в историю
                            toolResults.Add(new Dictionary<string, object>
                            {
                                ["role"] = "tool",
                                ["tool_call_id"] = toolCallId!,
                                ["content"] = toolResult
                            });
                        }

                        // Добавляем все результаты инструментов в историю
                        messages.AddRange(toolResults);
                    }

                    // Продолжаем цикл для следующего суб-запроса
                    subTurn++;
                    result.SubTurnsExecuted = subTurn - 1;
                }
                else if (finishReason == "stop")
                {
                    // Модель завершила работу
                    result.FinalAnswer = content ?? "";
                    result.SubTurnsExecuted = subTurn;
                    
                    _logger.LogInformation("DeepSeek завершил работу после {SubTurns} суб-запросов", subTurn);
                    
                    break;
                }
                else
                {
                    _logger.LogWarning("Неожиданная причина остановки: {FinishReason}", finishReason);
                    result.FinalAnswer = content ?? "";
                    result.SubTurnsExecuted = subTurn;
                    break;
                }
            }

            if (subTurn > maxSubTurns)
            {
                _logger.LogWarning("Достигнут лимит суб-запросов ({MaxSubTurns})", maxSubTurns);
                result.FinalAnswer += "\n\n⚠️ Достигнут лимит итераций. Некоторые задачи могут быть не выполнены.";
                result.SubTurnsExecuted = maxSubTurns;
            }

            result.UpdatedMessages = messages;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения раунда оркестратора");
            
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.FinalAnswer = $"❌ Ошибка: {ex.Message}";
            
            return result;
        }
    }
}

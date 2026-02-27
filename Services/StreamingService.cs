using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для обработки потоковой передачи ответов от LLM через WebSocket
/// </summary>
public class StreamingService : IStreamingService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebSocketManager _webSocketManager;
    private readonly ILogger<StreamingService> _logger;
    
    // Словарь активных генераций: connectionId -> CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeGenerations;
    
    public StreamingService(
        IServiceProvider serviceProvider,
        IWebSocketManager webSocketManager,
        ILogger<StreamingService> logger)
    {
        _serviceProvider = serviceProvider;
        _webSocketManager = webSocketManager;
        _logger = logger;
        _activeGenerations = new ConcurrentDictionary<string, CancellationTokenSource>();
    }
    
    /// <summary>
    /// Обрабатывает промпт с потоковой передачей фрагментов ответа через WebSocket
    /// </summary>
    public async Task<string> ProcessPromptWithStreamingAsync(
        int dialogueId,
        string prompt,
        string connectionId,
        CancellationToken cancellationToken)
    {
        // Создаем CancellationTokenSource для этой генерации
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        // Регистрируем активную генерацию
        if (!_activeGenerations.TryAdd(connectionId, cts))
        {
            _logger.LogWarning(
                "Попытка начать генерацию для соединения с уже активной генерацией: ConnectionId={ConnectionId}",
                connectionId);
            
            // Отменяем предыдущую генерацию
            await CancelGenerationAsync(connectionId);
            
            // Пытаемся добавить снова
            _activeGenerations.TryAdd(connectionId, cts);
        }
        
        var fullResponse = new StringBuilder();
        int? messageId = null;
        
        try
        {
            _logger.LogInformation(
                "Начало потоковой генерации: DialogueId={DialogueId}, ConnectionId={ConnectionId}, Timestamp={Timestamp}",
                dialogueId, connectionId, DateTime.UtcNow);
            
            // Создаем новый scope для получения scoped сервисов
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
            var promptProcessor = scope.ServiceProvider.GetRequiredService<IPromptProcessor>();
            var llmService = scope.ServiceProvider.GetRequiredService<ILlmService>();
            var projectService = scope.ServiceProvider.GetRequiredService<IProjectManagementService>();
            
            // Загружаем диалог и историю сообщений
            var dialogue = await dbContext.Dialogues
                .Include(d => d.Messages)
                .FirstOrDefaultAsync(d => d.Id == dialogueId, cts.Token);
            
            if (dialogue == null)
            {
                throw new ArgumentException($"Диалог с ID {dialogueId} не найден");
            }
            
            // Получаем выбранный проект (приоритет над dialogue.ProjectPath)
            var selectedProject = await projectService.GetSelectedProjectAsync();
            var projectPath = selectedProject?.Path ?? dialogue.ProjectPath;
            
            _logger.LogInformation(
                "Используется путь проекта: {ProjectPath} (выбранный проект: {SelectedProject})",
                projectPath,
                selectedProject?.Name ?? "нет");
            
            // Сохраняем сообщение пользователя
            var userMessage = new Message
            {
                DialogueId = dialogueId,
                Role = "user",
                Content = prompt,
                Timestamp = DateTime.UtcNow
            };
            dbContext.Messages.Add(userMessage);
            await dbContext.SaveChangesAsync(cts.Token);
            
            // Отправляем уведомление о начале генерации
            await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
            {
                Type = WebSocketMessageTypes.AssistantMessageStart,
                Payload = new MessageChunkPayload
                {
                    DialogueId = dialogueId,
                    MessageId = null, // Временный ID будет создан на фронтенде
                    Content = string.Empty,
                    IsComplete = false
                }
            });
            
            // Получаем определения инструментов из PromptProcessor
            var tools = promptProcessor.GetAvailableTools();
            
            // Получаем историю сообщений
            var history = dialogue.Messages
                .Where(m => m.Id != userMessage.Id)
                .OrderBy(m => m.Timestamp)
                .ToList();
            
            // Проверяем, поддерживает ли LLM сервис streaming И нет ли инструментов
            // Если есть инструменты, используем обычный API для корректной обработки tool calls
            if (llmService is IStreamingLlmService streamingLlmService && tools.Count == 0)
            {
                // Используем streaming API только если нет инструментов
                _logger.LogInformation("Используется streaming API для генерации ответа (без инструментов)");
                
                // Потоковая генерация
                await foreach (var chunk in streamingLlmService.StreamPromptAsync(
                    prompt, history, tools, cts.Token))
                {
                    // Проверяем отмену
                    cts.Token.ThrowIfCancellationRequested();
                    
                    // Добавляем фрагмент к полному ответу
                    fullResponse.Append(chunk);
                    
                    // Отправляем фрагмент через WebSocket
                    await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                    {
                        Type = WebSocketMessageTypes.AssistantMessageChunk,
                        Payload = new MessageChunkPayload
                        {
                            DialogueId = dialogueId,
                            MessageId = messageId,
                            Content = chunk,
                            IsComplete = false
                        }
                    });
                    
                    _logger.LogDebug(
                        "Отправлен фрагмент: ConnectionId={ConnectionId}, ChunkSize={Size}",
                        connectionId, chunk.Length);
                }
            }
            else
            {
                // Fallback: используем обычный API (если есть инструменты или streaming не поддерживается)
                _logger.LogInformation("Используется обычный API для генерации ответа (инструментов: {Count})", tools.Count);
                
                // Вызываем LLM напрямую
                var llmResponse = await llmService.SendPromptAsync(prompt, history, tools);
                
                // Обрабатываем ответ
                string responseText;
                if (llmResponse.FunctionCalls != null && llmResponse.FunctionCalls.Count > 0)
                {
                    // Если есть вызовы функций, выполняем их и отправляем результаты в реальном времени
                    var functionResults = new StringBuilder();
                    
                    // Добавляем контекстное сообщение
                    var firstCall = llmResponse.FunctionCalls.First();
                    if (firstCall.Name == "execute_shell_command" && firstCall.Arguments.ContainsKey("command"))
                    {
                        var command = firstCall.Arguments["command"]?.ToString() ?? "";
                        string contextMessage = "";
                        
                        if (command.Contains("del", StringComparison.OrdinalIgnoreCase) || 
                            command.Contains("rm", StringComparison.OrdinalIgnoreCase))
                        {
                            contextMessage = "Удаляю файл...\n\n";
                        }
                        else if (command.Contains(">") || command.Contains("echo"))
                        {
                            contextMessage = "Создаю файл...\n\n";
                        }
                        
                        if (!string.IsNullOrEmpty(contextMessage))
                        {
                            functionResults.Append(contextMessage);
                            fullResponse.Append(contextMessage);
                            
                            // Отправляем контекстное сообщение через WebSocket
                            await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                            {
                                Type = WebSocketMessageTypes.AssistantMessageChunk,
                                Payload = new MessageChunkPayload
                                {
                                    DialogueId = dialogueId,
                                    MessageId = null,
                                    Content = contextMessage,
                                    IsComplete = false
                                }
                            });
                        }
                    }
                    
                    foreach (var functionCall in llmResponse.FunctionCalls)
                    {
                        try
                        {
                            _logger.LogInformation(
                                "Выполнение функции {FunctionName} в директории: {ProjectPath}",
                                functionCall.Name,
                                projectPath);
                            
                            var result = await promptProcessor.ExecuteFunctionAsync(
                                functionCall.Name,
                                functionCall.Arguments,
                                projectPath);
                            
                            // Форматируем результат
                            string formattedResult;
                            if (!string.IsNullOrWhiteSpace(result))
                            {
                                formattedResult = $"✅ {result}\n";
                            }
                            else
                            {
                                formattedResult = "✅ Команда выполнена успешно\n";
                            }
                            
                            functionResults.Append(formattedResult);
                            fullResponse.Append(formattedResult);
                            
                            // Отправляем результат через WebSocket сразу после выполнения
                            await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                            {
                                Type = WebSocketMessageTypes.AssistantMessageChunk,
                                Payload = new MessageChunkPayload
                                {
                                    DialogueId = dialogueId,
                                    MessageId = null,
                                    Content = formattedResult,
                                    IsComplete = false
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка выполнения функции {FunctionName}", functionCall.Name);
                            
                            var errorMessage = $"❌ Ошибка: {ex.Message}\n";
                            functionResults.Append(errorMessage);
                            fullResponse.Append(errorMessage);
                            
                            // Отправляем сообщение об ошибке через WebSocket
                            await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                            {
                                Type = WebSocketMessageTypes.AssistantMessageChunk,
                                Payload = new MessageChunkPayload
                                {
                                    DialogueId = dialogueId,
                                    MessageId = null,
                                    Content = errorMessage,
                                    IsComplete = false
                                }
                            });
                        }
                    }
                    
                    var finalMessage = "\nГотово! Операция выполнена успешно.\n";
                    functionResults.Append(finalMessage);
                    fullResponse.Append(finalMessage);
                    
                    // Отправляем финальное сообщение через WebSocket
                    await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                    {
                        Type = WebSocketMessageTypes.AssistantMessageChunk,
                        Payload = new MessageChunkPayload
                        {
                            DialogueId = dialogueId,
                            MessageId = null,
                            Content = finalMessage,
                            IsComplete = false
                        }
                    });
                    
                    responseText = functionResults.ToString();
                }
                else
                {
                    responseText = llmResponse.TextContent ?? string.Empty;
                    fullResponse.Append(responseText);
                    
                    // Отправляем текстовый ответ через WebSocket
                    await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                    {
                        Type = WebSocketMessageTypes.AssistantMessageChunk,
                        Payload = new MessageChunkPayload
                        {
                            DialogueId = dialogueId,
                            MessageId = null,
                            Content = responseText,
                            IsComplete = false
                        }
                    });
                }
            }
            
            // Сохраняем завершенное сообщение ассистента в базу данных
            var assistantMessage = new Message
            {
                DialogueId = dialogueId,
                Role = "assistant",
                Content = fullResponse.ToString(),
                Timestamp = DateTime.UtcNow
            };
            dbContext.Messages.Add(assistantMessage);
            await dbContext.SaveChangesAsync(cts.Token);
            
            messageId = assistantMessage.Id;
            
            // Отправляем финальное сообщение о завершении
            await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
            {
                Type = WebSocketMessageTypes.AssistantMessageEnd,
                Payload = new MessageChunkPayload
                {
                    DialogueId = dialogueId,
                    MessageId = messageId,
                    Content = fullResponse.ToString(),
                    IsComplete = true
                }
            });
            
            _logger.LogInformation(
                "Потоковая генерация завершена: DialogueId={DialogueId}, ConnectionId={ConnectionId}, MessageId={MessageId}, Length={Length}, Timestamp={Timestamp}",
                dialogueId, connectionId, messageId, fullResponse.Length, DateTime.UtcNow);
            
            return fullResponse.ToString();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Генерация отменена пользователем: DialogueId={DialogueId}, ConnectionId={ConnectionId}, PartialLength={Length}",
                dialogueId, connectionId, fullResponse.Length);
            
            // Сохраняем частичный ответ, если он есть
            if (fullResponse.Length > 0)
            {
                // Создаем новый scope для сохранения частичного ответа
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
                
                var partialMessage = new Message
                {
                    DialogueId = dialogueId,
                    Role = "assistant",
                    Content = fullResponse.ToString() + "\n\n[Генерация прервана пользователем]",
                    Timestamp = DateTime.UtcNow
                };
                dbContext.Messages.Add(partialMessage);
                await dbContext.SaveChangesAsync(CancellationToken.None);
                
                messageId = partialMessage.Id;
                
                // Отправляем уведомление об отмене с частичным ответом
                await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
                {
                    Type = WebSocketMessageTypes.AssistantMessageEnd,
                    Payload = new MessageChunkPayload
                    {
                        DialogueId = dialogueId,
                        MessageId = messageId,
                        Content = partialMessage.Content,
                        IsComplete = true
                    }
                });
            }
            
            return fullResponse.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка во время потоковой генерации: DialogueId={DialogueId}, ConnectionId={ConnectionId}",
                dialogueId, connectionId);
            
            // Создаем новый scope для сохранения сообщения об ошибке
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
            
            // Сохраняем частичный ответ с сообщением об ошибке
            var errorContent = fullResponse.Length > 0
                ? fullResponse.ToString() + $"\n\n[Ошибка: {ex.Message}]"
                : $"[Ошибка генерации: {ex.Message}]";
            
            var errorMessage = new Message
            {
                DialogueId = dialogueId,
                Role = "assistant",
                Content = errorContent,
                Timestamp = DateTime.UtcNow
            };
            dbContext.Messages.Add(errorMessage);
            await dbContext.SaveChangesAsync(CancellationToken.None);
            
            messageId = errorMessage.Id;
            
            // Отправляем сообщение об ошибке через WebSocket
            await _webSocketManager.SendMessageAsync(connectionId, new WebSocketMessage
            {
                Type = WebSocketMessageTypes.Error,
                Payload = new
                {
                    dialogueId,
                    messageId,
                    message = $"Ошибка генерации ответа: {ex.Message}",
                    partialResponse = fullResponse.ToString()
                }
            });
            
            throw;
        }
        finally
        {
            // Удаляем активную генерацию из словаря
            _activeGenerations.TryRemove(connectionId, out _);
            
            // Освобождаем ресурсы CancellationTokenSource
            cts.Dispose();
        }
    }
    
    /// <summary>
    /// Отменяет текущую генерацию ответа для указанного соединения
    /// </summary>
    public async Task CancelGenerationAsync(string connectionId)
    {
        try
        {
            if (_activeGenerations.TryRemove(connectionId, out var cts))
            {
                _logger.LogInformation(
                    "Отмена генерации: ConnectionId={ConnectionId}, Timestamp={Timestamp}",
                    connectionId, DateTime.UtcNow);
                
                // Отменяем генерацию
                cts.Cancel();
                
                // Освобождаем ресурсы
                cts.Dispose();
                
                _logger.LogInformation(
                    "Генерация успешно отменена: ConnectionId={ConnectionId}",
                    connectionId);
            }
            else
            {
                _logger.LogWarning(
                    "Попытка отменить несуществующую генерацию: ConnectionId={ConnectionId}",
                    connectionId);
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка при отмене генерации: ConnectionId={ConnectionId}",
                connectionId);
            throw;
        }
    }
}

/// <summary>
/// Расширенный интерфейс для LLM сервисов с поддержкой streaming
/// </summary>
public interface IStreamingLlmService : ILlmService
{
    /// <summary>
    /// Потоковая генерация ответа с возвратом фрагментов по мере генерации
    /// </summary>
    /// <param name="prompt">Текст промпта</param>
    /// <param name="history">История сообщений</param>
    /// <param name="tools">Доступные инструменты</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Асинхронный поток фрагментов ответа</returns>
    new IAsyncEnumerable<string> StreamPromptAsync(
        string prompt,
        List<Message> history,
        List<FunctionDefinition> tools,
        CancellationToken cancellationToken = default);
}

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Управляет WebSocket соединениями и маршрутизацией сообщений
/// </summary>
public class WebSocketManager : IWebSocketManager
{
    // Словарь: connectionId -> (WebSocket, dialogueId)
    private readonly ConcurrentDictionary<string, (WebSocket WebSocket, int DialogueId)> _connections;
    
    // Словарь: dialogueId -> List<connectionId>
    private readonly ConcurrentDictionary<int, List<string>> _dialogueConnections;
    
    private readonly ILogger<WebSocketManager> _logger;
    
    public WebSocketManager(ILogger<WebSocketManager> logger)
    {
        _logger = logger;
        _connections = new ConcurrentDictionary<string, (WebSocket, int)>();
        _dialogueConnections = new ConcurrentDictionary<int, List<string>>();
    }
    
    /// <summary>
    /// Регистрирует новое WebSocket соединение
    /// </summary>
    public async Task RegisterConnectionAsync(string connectionId, WebSocket webSocket, int dialogueId)
    {
        try
        {
            // Добавляем соединение в основной словарь
            if (_connections.TryAdd(connectionId, (webSocket, dialogueId)))
            {
                // Добавляем connectionId в список соединений диалога
                _dialogueConnections.AddOrUpdate(
                    dialogueId,
                    new List<string> { connectionId },
                    (key, existingList) =>
                    {
                        lock (existingList)
                        {
                            existingList.Add(connectionId);
                        }
                        return existingList;
                    });
                
                _logger.LogInformation(
                    "WebSocket соединение зарегистрировано: ConnectionId={ConnectionId}, DialogueId={DialogueId}, Timestamp={Timestamp}",
                    connectionId, dialogueId, DateTime.UtcNow);
                
                // Отправляем подтверждение подключения
                await SendMessageAsync(connectionId, new WebSocketMessage
                {
                    Type = WebSocketMessageTypes.ConnectionAck,
                    Payload = new { connectionId, dialogueId }
                });
            }
            else
            {
                _logger.LogWarning(
                    "Не удалось зарегистрировать соединение (уже существует): ConnectionId={ConnectionId}",
                    connectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка при регистрации WebSocket соединения: ConnectionId={ConnectionId}, DialogueId={DialogueId}",
                connectionId, dialogueId);
            throw;
        }
    }
    
    /// <summary>
    /// Удаляет соединение из менеджера
    /// </summary>
    public async Task UnregisterConnectionAsync(string connectionId)
    {
        try
        {
            if (_connections.TryRemove(connectionId, out var connectionInfo))
            {
                var (webSocket, dialogueId) = connectionInfo;
                
                // Удаляем connectionId из списка соединений диалога
                if (_dialogueConnections.TryGetValue(dialogueId, out var connectionList))
                {
                    lock (connectionList)
                    {
                        connectionList.Remove(connectionId);
                        
                        // Если список пуст, удаляем запись для диалога
                        if (connectionList.Count == 0)
                        {
                            _dialogueConnections.TryRemove(dialogueId, out _);
                        }
                    }
                }
                
                // Закрываем WebSocket соединение, если оно еще открыто
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed by server",
                        CancellationToken.None);
                }
                
                _logger.LogInformation(
                    "WebSocket соединение удалено: ConnectionId={ConnectionId}, DialogueId={DialogueId}, Timestamp={Timestamp}",
                    connectionId, dialogueId, DateTime.UtcNow);
            }
            else
            {
                _logger.LogWarning(
                    "Попытка удалить несуществующее соединение: ConnectionId={ConnectionId}",
                    connectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка при удалении WebSocket соединения: ConnectionId={ConnectionId}",
                connectionId);
            throw;
        }
    }

    
    /// <summary>
    /// Отправляет сообщение конкретному соединению
    /// </summary>
    public async Task SendMessageAsync(string connectionId, WebSocketMessage message)
    {
        try
        {
            if (!_connections.TryGetValue(connectionId, out var connectionInfo))
            {
                _logger.LogWarning(
                    "Попытка отправить сообщение несуществующему соединению: ConnectionId={ConnectionId}",
                    connectionId);
                return;
            }
            
            var (webSocket, dialogueId) = connectionInfo;
            
            if (webSocket.State != WebSocketState.Open)
            {
                _logger.LogWarning(
                    "Попытка отправить сообщение через закрытое соединение: ConnectionId={ConnectionId}, State={State}",
                    connectionId, webSocket.State);
                return;
            }
            
            // Сериализуем сообщение в JSON с camelCase для совместимости с JavaScript
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(message, options);
            var bytes = Encoding.UTF8.GetBytes(json);
            var buffer = new ArraySegment<byte>(bytes);
            
            // Отправляем сообщение
            await webSocket.SendAsync(
                buffer,
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);
            
            _logger.LogInformation(
                "Сообщение отправлено: ConnectionId={ConnectionId}, Type={MessageType}, Size={Size} bytes",
                connectionId, message.Type, bytes.Length);
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex,
                "WebSocket ошибка при отправке сообщения: ConnectionId={ConnectionId}",
                connectionId);
            
            // Удаляем неработающее соединение
            await UnregisterConnectionAsync(connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка при отправке сообщения: ConnectionId={ConnectionId}",
                connectionId);
            throw;
        }
    }
    
    /// <summary>
    /// Отправляет сообщение всем соединениям, привязанным к диалогу
    /// </summary>
    public async Task BroadcastToDialogueAsync(int dialogueId, WebSocketMessage message)
    {
        try
        {
            if (!_dialogueConnections.TryGetValue(dialogueId, out var connectionList))
            {
                _logger.LogDebug(
                    "Нет активных соединений для диалога: DialogueId={DialogueId}",
                    dialogueId);
                return;
            }
            
            List<string> connectionIds;
            lock (connectionList)
            {
                connectionIds = new List<string>(connectionList);
            }
            
            _logger.LogInformation(
                "Отправка broadcast сообщения: DialogueId={DialogueId}, Type={MessageType}, ConnectionCount={Count}",
                dialogueId, message.Type, connectionIds.Count);
            
            // Отправляем сообщение всем соединениям параллельно
            var sendTasks = connectionIds.Select(connectionId => 
                SendMessageAsync(connectionId, message));
            
            await Task.WhenAll(sendTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка при broadcast сообщения: DialogueId={DialogueId}",
                dialogueId);
            throw;
        }
    }
    
    /// <summary>
    /// Получает WebSocket соединение по ID
    /// </summary>
    public WebSocket? GetConnection(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var connectionInfo))
        {
            return connectionInfo.WebSocket;
        }
        
        return null;
    }
    
    /// <summary>
    /// Проверяет, активно ли соединение
    /// </summary>
    public bool IsConnectionActive(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var connectionInfo))
        {
            var (webSocket, _) = connectionInfo;
            return webSocket.State == WebSocketState.Open;
        }
        
        return false;
    }
}

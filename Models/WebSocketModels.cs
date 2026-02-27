namespace CSharpRefactoringAssistant.Models;

/// <summary>
/// Базовое сообщение WebSocket
/// </summary>
public class WebSocketMessage
{
    /// <summary>
    /// Тип сообщения (user_message, assistant_message_chunk, и т.д.)
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Полезная нагрузка сообщения
    /// </summary>
    public object? Payload { get; set; }
    
    /// <summary>
    /// Временная метка создания сообщения
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Константы типов WebSocket сообщений
/// </summary>
public static class WebSocketMessageTypes
{
    public const string UserMessage = "user_message";
    public const string AssistantMessageStart = "assistant_message_start";
    public const string AssistantMessageChunk = "assistant_message_chunk";
    public const string AssistantMessageEnd = "assistant_message_end";
    public const string Error = "error";
    public const string CancelGeneration = "cancel_generation";
    public const string ConnectionAck = "connection_ack";
    public const string Ping = "ping";
    public const string Pong = "pong";
}

/// <summary>
/// Полезная нагрузка для фрагмента сообщения
/// </summary>
public class MessageChunkPayload
{
    /// <summary>
    /// ID диалога
    /// </summary>
    public int DialogueId { get; set; }
    
    /// <summary>
    /// ID сообщения (может быть null для промежуточных фрагментов)
    /// </summary>
    public int? MessageId { get; set; }
    
    /// <summary>
    /// Содержимое фрагмента
    /// </summary>
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Флаг завершения сообщения
    /// </summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// Полезная нагрузка для подтверждения подключения
/// </summary>
public class ConnectionAckPayload
{
    /// <summary>
    /// Уникальный идентификатор соединения
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;
    
    /// <summary>
    /// ID диалога
    /// </summary>
    public int DialogueId { get; set; }
    
    /// <summary>
    /// Сообщение о подключении
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Полезная нагрузка для сообщения об ошибке
/// </summary>
public class ErrorPayload
{
    /// <summary>
    /// ID диалога
    /// </summary>
    public int DialogueId { get; set; }
    
    /// <summary>
    /// ID сообщения (если применимо)
    /// </summary>
    public int? MessageId { get; set; }
    
    /// <summary>
    /// Текст сообщения об ошибке
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Частичный ответ (если был получен до ошибки)
    /// </summary>
    public string? PartialResponse { get; set; }
}

/// <summary>
/// Полезная нагрузка для сообщения пользователя
/// </summary>
public class UserMessagePayload
{
    /// <summary>
    /// ID диалога
    /// </summary>
    public int DialogueId { get; set; }
    
    /// <summary>
    /// Содержимое сообщения пользователя
    /// </summary>
    public string Content { get; set; } = string.Empty;
}

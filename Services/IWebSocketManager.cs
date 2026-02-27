using System.Net.WebSockets;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Интерфейс для управления WebSocket соединениями и маршрутизацией сообщений
/// </summary>
public interface IWebSocketManager
{
    /// <summary>
    /// Регистрирует новое WebSocket соединение
    /// </summary>
    /// <param name="connectionId">Уникальный идентификатор соединения</param>
    /// <param name="webSocket">WebSocket соединение</param>
    /// <param name="dialogueId">ID диалога, к которому привязано соединение</param>
    Task RegisterConnectionAsync(string connectionId, WebSocket webSocket, int dialogueId);
    
    /// <summary>
    /// Удаляет соединение из менеджера
    /// </summary>
    /// <param name="connectionId">Уникальный идентификатор соединения</param>
    Task UnregisterConnectionAsync(string connectionId);
    
    /// <summary>
    /// Отправляет сообщение конкретному соединению
    /// </summary>
    /// <param name="connectionId">Уникальный идентификатор соединения</param>
    /// <param name="message">Сообщение для отправки</param>
    Task SendMessageAsync(string connectionId, WebSocketMessage message);
    
    /// <summary>
    /// Отправляет сообщение всем соединениям, привязанным к диалогу
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="message">Сообщение для отправки</param>
    Task BroadcastToDialogueAsync(int dialogueId, WebSocketMessage message);
    
    /// <summary>
    /// Получает WebSocket соединение по ID
    /// </summary>
    /// <param name="connectionId">Уникальный идентификатор соединения</param>
    /// <returns>WebSocket соединение или null, если не найдено</returns>
    WebSocket? GetConnection(string connectionId);
    
    /// <summary>
    /// Проверяет, активно ли соединение
    /// </summary>
    /// <param name="connectionId">Уникальный идентификатор соединения</param>
    /// <returns>true, если соединение активно</returns>
    bool IsConnectionActive(string connectionId);
}

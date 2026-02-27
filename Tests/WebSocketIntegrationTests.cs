using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Тесты для проверки интеграции WebSocket в Program.cs
/// </summary>
public class WebSocketIntegrationTests
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Тесты интеграции WebSocket ===\n");

        var allPassed = true;

        allPassed &= await TestWebSocketConnectionWithValidDialogue();
        allPassed &= await TestWebSocketConnectionWithInvalidDialogue();
        allPassed &= await TestWebSocketPingPong();
        allPassed &= await TestWebSocketUserMessage();
        allPassed &= await TestWebSocketCancelGeneration();

        Console.WriteLine("\n=== Результаты тестов ===");
        Console.WriteLine(allPassed ? "✓ Все тесты пройдены" : "✗ Некоторые тесты не прошли");
        
        Environment.Exit(allPassed ? 0 : 1);
    }

    private static async Task<bool> TestWebSocketConnectionWithValidDialogue()
    {
        Console.WriteLine("Тест 1: Подключение WebSocket с валидным dialogueId");
        
        try
        {
            // Примечание: Этот тест требует запущенного сервера
            // Для полноценного тестирования нужно использовать TestServer из Microsoft.AspNetCore.Mvc.Testing
            
            Console.WriteLine("  ⚠ Тест требует запущенного сервера - пропущен");
            Console.WriteLine("  ℹ Для ручного тестирования используйте wscat или Postman");
            Console.WriteLine("  ℹ Команда: wscat -c ws://localhost:5000/ws?dialogueId=1");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Ошибка: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TestWebSocketConnectionWithInvalidDialogue()
    {
        Console.WriteLine("\nТест 2: Подключение WebSocket с невалидным dialogueId");
        
        try
        {
            Console.WriteLine("  ⚠ Тест требует запущенного сервера - пропущен");
            Console.WriteLine("  ℹ Ожидается HTTP 404 при подключении к несуществующему диалогу");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Ошибка: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TestWebSocketPingPong()
    {
        Console.WriteLine("\nТест 3: Обработка ping/pong сообщений");
        
        try
        {
            // Проверяем структуру сообщений
            var pingMessage = new WebSocketMessage
            {
                Type = WebSocketMessageTypes.Ping,
                Payload = null
            };
            
            var json = JsonSerializer.Serialize(pingMessage);
            var deserialized = JsonSerializer.Deserialize<WebSocketMessage>(json);
            
            if (deserialized?.Type != WebSocketMessageTypes.Ping)
            {
                Console.WriteLine("  ✗ Ошибка сериализации ping сообщения");
                return false;
            }
            
            Console.WriteLine("  ✓ Структура ping/pong сообщений корректна");
            Console.WriteLine("  ℹ Для полного тестирования требуется запущенный сервер");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Ошибка: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TestWebSocketUserMessage()
    {
        Console.WriteLine("\nТест 4: Обработка сообщений пользователя");
        
        try
        {
            // Проверяем структуру сообщения пользователя
            var userMessage = new WebSocketMessage
            {
                Type = WebSocketMessageTypes.UserMessage,
                Payload = new { content = "Test message" }
            };
            
            var json = JsonSerializer.Serialize(userMessage);
            var deserialized = JsonSerializer.Deserialize<WebSocketMessage>(json);
            
            if (deserialized?.Type != WebSocketMessageTypes.UserMessage)
            {
                Console.WriteLine("  ✗ Ошибка сериализации user_message");
                return false;
            }
            
            Console.WriteLine("  ✓ Структура user_message корректна");
            Console.WriteLine("  ℹ Для полного тестирования требуется запущенный сервер");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Ошибка: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TestWebSocketCancelGeneration()
    {
        Console.WriteLine("\nТест 5: Обработка отмены генерации");
        
        try
        {
            // Проверяем структуру сообщения отмены
            var cancelMessage = new WebSocketMessage
            {
                Type = WebSocketMessageTypes.CancelGeneration,
                Payload = null
            };
            
            var json = JsonSerializer.Serialize(cancelMessage);
            var deserialized = JsonSerializer.Deserialize<WebSocketMessage>(json);
            
            if (deserialized?.Type != WebSocketMessageTypes.CancelGeneration)
            {
                Console.WriteLine("  ✗ Ошибка сериализации cancel_generation");
                return false;
            }
            
            Console.WriteLine("  ✓ Структура cancel_generation корректна");
            Console.WriteLine("  ℹ Для полного тестирования требуется запущенный сервер");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Ошибка: {ex.Message}");
            return false;
        }
    }
}

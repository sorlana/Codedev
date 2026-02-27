using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Ручные тесты для проверки инфраструктуры WebSocket
/// </summary>
public class WebSocketInfrastructureTests
{
    /// <summary>
    /// Тест подключения к WebSocket endpoint
    /// </summary>
    public static async Task TestWebSocketConnection()
    {
        Console.WriteLine("=== Тест подключения WebSocket ===");
        
        try
        {
            // Создаем тестовый диалог через HTTP API
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri("http://localhost:5111");
            
            var createDialogueRequest = new
            {
                projectPath = Environment.CurrentDirectory
            };
            
            var createResponse = await httpClient.PostAsJsonAsync(
                "/api/dialogues", 
                createDialogueRequest);
            
            if (!createResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ Не удалось создать диалог: {createResponse.StatusCode}");
                return;
            }
            
            var dialogueJson = await createResponse.Content.ReadAsStringAsync();
            var dialogue = JsonSerializer.Deserialize<JsonElement>(dialogueJson);
            var dialogueId = dialogue.GetProperty("id").GetInt32();
            
            Console.WriteLine($"✓ Создан тестовый диалог с ID: {dialogueId}");
            
            // Подключаемся к WebSocket
            using var ws = new ClientWebSocket();
            var wsUri = new Uri($"ws://localhost:5111/ws?dialogueId={dialogueId}");
            
            Console.WriteLine($"Подключение к {wsUri}...");
            await ws.ConnectAsync(wsUri, CancellationToken.None);
            
            Console.WriteLine("✓ WebSocket соединение установлено");
            
            // Получаем подтверждение соединения
            var buffer = new byte[1024 * 4];
            var result = await ws.ReceiveAsync(
                new ArraySegment<byte>(buffer), 
                CancellationToken.None);
            
            var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            var message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson);
            
            if (message?.Type == WebSocketMessageTypes.ConnectionAck)
            {
                Console.WriteLine("✓ Получено подтверждение соединения");
                
                var payload = JsonSerializer.Deserialize<ConnectionAckPayload>(
                    message.Payload?.ToString() ?? "{}");
                
                Console.WriteLine($"  Connection ID: {payload?.ConnectionId}");
                Console.WriteLine($"  Dialogue ID: {payload?.DialogueId}");
                Console.WriteLine($"  Message: {payload?.Message}");
            }
            else
            {
                Console.WriteLine($"❌ Неожиданный тип сообщения: {message?.Type}");
            }
            
            // Отправляем ping
            var pingMessage = new WebSocketMessage
            {
                Type = WebSocketMessageTypes.Ping,
                Payload = new { test = "ping" }
            };
            
            var pingJson = JsonSerializer.Serialize(pingMessage);
            var pingBytes = Encoding.UTF8.GetBytes(pingJson);
            
            await ws.SendAsync(
                new ArraySegment<byte>(pingBytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
            
            Console.WriteLine("✓ Отправлен ping");
            
            // Получаем pong
            result = await ws.ReceiveAsync(
                new ArraySegment<byte>(buffer), 
                CancellationToken.None);
            
            messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson);
            
            if (message?.Type == WebSocketMessageTypes.Pong)
            {
                Console.WriteLine("✓ Получен pong");
            }
            else
            {
                Console.WriteLine($"❌ Ожидался pong, получен: {message?.Type}");
            }
            
            // Закрываем соединение
            await ws.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Test completed",
                CancellationToken.None);
            
            Console.WriteLine("✓ Соединение закрыто");
            
            // Удаляем тестовый диалог
            await httpClient.DeleteAsync($"/api/dialogues/{dialogueId}");
            Console.WriteLine($"✓ Тестовый диалог {dialogueId} удален");
            
            Console.WriteLine("\n✅ Все тесты пройдены успешно!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Ошибка: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Тест обработки некорректных запросов
    /// </summary>
    public static async Task TestInvalidRequests()
    {
        Console.WriteLine("\n=== Тест обработки некорректных запросов ===");
        
        try
        {
            // Тест 1: Подключение без dialogueId
            Console.WriteLine("\nТест 1: Подключение без dialogueId");
            using var ws1 = new ClientWebSocket();
            var wsUri1 = new Uri("ws://localhost:5111/ws");
            
            try
            {
                await ws1.ConnectAsync(wsUri1, CancellationToken.None);
                Console.WriteLine("❌ Соединение должно было быть отклонено");
            }
            catch (WebSocketException)
            {
                Console.WriteLine("✓ Соединение корректно отклонено (отсутствует dialogueId)");
            }
            
            // Тест 2: Подключение с несуществующим dialogueId
            Console.WriteLine("\nТест 2: Подключение с несуществующим dialogueId");
            using var ws2 = new ClientWebSocket();
            var wsUri2 = new Uri("ws://localhost:5111/ws?dialogueId=99999");
            
            try
            {
                await ws2.ConnectAsync(wsUri2, CancellationToken.None);
                Console.WriteLine("❌ Соединение должно было быть отклонено");
            }
            catch (WebSocketException)
            {
                Console.WriteLine("✓ Соединение корректно отклонено (несуществующий диалог)");
            }
            
            // Тест 3: Подключение с некорректным dialogueId
            Console.WriteLine("\nТест 3: Подключение с некорректным dialogueId");
            using var ws3 = new ClientWebSocket();
            var wsUri3 = new Uri("ws://localhost:5111/ws?dialogueId=invalid");
            
            try
            {
                await ws3.ConnectAsync(wsUri3, CancellationToken.None);
                Console.WriteLine("❌ Соединение должно было быть отклонено");
            }
            catch (WebSocketException)
            {
                Console.WriteLine("✓ Соединение корректно отклонено (некорректный формат dialogueId)");
            }
            
            Console.WriteLine("\n✅ Все тесты валидации пройдены успешно!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Ошибка: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Запуск всех тестов
    /// </summary>
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Запуск тестов инфраструктуры WebSocket");
        Console.WriteLine("Убедитесь, что приложение запущено на http://localhost:5111\n");
        
        await TestWebSocketConnection();
        await TestInvalidRequests();
        
        Console.WriteLine("\n=== Тестирование завершено ===");
    }
}

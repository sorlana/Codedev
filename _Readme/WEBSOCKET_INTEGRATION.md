# Интеграция WebSocket в Program.cs

## Обзор

Задача 5 спецификации "Оптимизация производительности чата в реальном времени" успешно реализована. WebSocket endpoint полностью интегрирован в Program.cs с использованием WebSocketManager и StreamingService.

## Реализованная функциональность

### 1. Регистрация сервисов в DI контейнере

В Program.cs добавлены следующие регистрации:

```csharp
// Регистрация WebSocket компонентов
builder.Services.AddSingleton<IWebSocketManager, WebSocketManager>();
builder.Services.AddScoped<IStreamingService, StreamingService>();
```

- `IWebSocketManager` зарегистрирован как Singleton для управления всеми активными соединениями
- `IStreamingService` зарегистрирован как Scoped для обработки потоковой передачи в контексте запроса

### 2. Включение WebSocket middleware

```csharp
// Enable WebSockets
app.UseWebSockets();
```

Middleware добавлен в pipeline перед статическими файлами для корректной обработки WebSocket запросов.

### 3. WebSocket endpoint `/ws`

Создан endpoint для обработки WebSocket соединений с следующими возможностями:

#### Параметры подключения
- **Query параметр**: `dialogueId` (обязательный)
- **URL**: `ws://localhost:5000/ws?dialogueId={id}`

#### Валидация при подключении
- Проверка, что запрос является WebSocket запросом
- Проверка наличия и валидности параметра `dialogueId`
- Проверка существования диалога в базе данных
- Возврат соответствующих HTTP кодов ошибок (400, 404)

#### Регистрация соединения
- Генерация уникального `connectionId` (GUID)
- Регистрация соединения в `WebSocketManager`
- Автоматическая отправка подтверждения подключения (`connection_ack`)
- Логирование события подключения с временной меткой

### 4. Обработка входящих сообщений

Реализована обработка следующих типов сообщений:

#### Ping/Pong
```json
// Входящее сообщение
{
  "type": "ping",
  "payload": null
}

// Ответ
{
  "type": "pong",
  "payload": { "connectionId": "..." }
}
```

#### User Message
```json
{
  "type": "user_message",
  "payload": {
    "content": "Текст сообщения пользователя"
  }
}
```

Обработка:
- Извлечение содержимого из payload
- Валидация (проверка на пустое сообщение)
- Запуск обработки через `StreamingService` в фоновом режиме
- Отправка ошибки при невалидном формате

#### Cancel Generation
```json
{
  "type": "cancel_generation",
  "payload": null
}
```

Обработка:
- Вызов `StreamingService.CancelGenerationAsync(connectionId)`
- Прерывание текущей генерации ответа

### 5. Обработка ошибок

Реализована обработка следующих ошибок:

#### Ошибки парсинга JSON
- Отлов `JsonException`
- Отправка сообщения об ошибке клиенту
- Логирование ошибки

#### Ошибки извлечения данных
- Обработка отсутствующих полей в payload
- Отправка информативных сообщений об ошибках

#### Общие ошибки соединения
- Логирование всех исключений
- Graceful завершение соединения

### 6. Очистка ресурсов при отключении

Реализован блок `finally` для гарантированной очистки:

```csharp
finally
{
    // Логирование закрытия соединения
    app.Logger.LogInformation("WebSocket соединение закрыто: ConnectionId={ConnectionId}, Timestamp={Timestamp}", 
        connectionId, DateTime.UtcNow);
    
    // Удаление соединения из WebSocketManager
    await webSocketManager.UnregisterConnectionAsync(connectionId);
    
    // Закрытие WebSocket, если он еще открыт
    if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
    {
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
    }
    
    // Освобождение ресурсов
    webSocket.Dispose();
}
```

## Добавленные модели

### UserMessagePayload

Добавлена модель в `Models/WebSocketModels.cs`:

```csharp
public class UserMessagePayload
{
    public int DialogueId { get; set; }
    public string Content { get; set; } = string.Empty;
}
```

## Тестирование

### Автоматические тесты

Создан файл `Tests/WebSocketIntegrationTests.cs` с базовыми тестами:

1. Проверка структуры ping/pong сообщений
2. Проверка структуры user_message
3. Проверка структуры cancel_generation

Запуск тестов:
```bash
dotnet run test-websocket-integration
```

### Ручное тестирование

Для полноценного тестирования WebSocket endpoint используйте:

#### С помощью wscat (Node.js)
```bash
# Установка wscat
npm install -g wscat

# Подключение к WebSocket
wscat -c ws://localhost:5000/ws?dialogueId=1

# Отправка ping
> {"type":"ping","payload":null}

# Отправка сообщения пользователя
> {"type":"user_message","payload":{"content":"Test message"}}

# Отмена генерации
> {"type":"cancel_generation","payload":null}
```

#### С помощью Postman
1. Создайте новый WebSocket запрос
2. URL: `ws://localhost:5000/ws?dialogueId=1`
3. Подключитесь
4. Отправляйте JSON сообщения через интерфейс

## Логирование

Все операции WebSocket логируются с соответствующими уровнями:

- **Information**: Подключение, отключение, получение сообщений
- **Debug**: Детали сообщений (содержимое)
- **Warning**: Невалидные сообщения, попытки работы с несуществующими соединениями
- **Error**: Исключения, ошибки парсинга, ошибки обработки

Каждое событие включает:
- `ConnectionId` - уникальный идентификатор соединения
- `DialogueId` - ID диалога
- `Timestamp` - временная метка (UTC)

## Соответствие требованиям

Реализация полностью соответствует требованиям задачи 5:

- ✅ Зарегистрированы IWebSocketManager и IStreamingService в DI контейнере
- ✅ Добавлен WebSocket middleware в pipeline (app.UseWebSockets())
- ✅ Создан endpoint для обработки WebSocket соединений
- ✅ Обработаны входящие сообщения (user_message, cancel_generation, ping)
- ✅ Реализована обработка отключения клиента и очистка ресурсов
- ✅ Соответствует Requirements: 1.1, 1.2, 1.4, 1.5, 1.6

## Следующие шаги

После завершения задачи 5, следующие задачи в спецификации:

1. **Задача 6**: Checkpoint - Тестирование backend WebSocket функциональности
2. **Задача 7**: Реализация WebSocketClient (Frontend)
3. **Задача 8**: Интеграция WebSocketClient в существующий UI (Frontend)

## Примечания

- WebSocket endpoint работает параллельно с существующими HTTP endpoints
- Обратная совместимость полностью сохранена
- Все изменения следуют архитектурным паттернам проекта
- Код полностью документирован на русском языке

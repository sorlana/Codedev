# WebSocket Infrastructure - Документация

## Обзор

Реализована базовая инфраструктура WebSocket для real-time коммуникации между клиентом и сервером.

## Компоненты

### 1. Модели данных (Models/WebSocketModels.cs)

#### WebSocketMessage
Базовое сообщение WebSocket с полями:
- `Type` - тип сообщения (см. WebSocketMessageTypes)
- `Payload` - полезная нагрузка (object)
- `Timestamp` - временная метка (DateTime)

#### WebSocketMessageTypes
Константы типов сообщений:
- `UserMessage` - сообщение от пользователя
- `AssistantMessageStart` - начало генерации ответа
- `AssistantMessageChunk` - фрагмент ответа
- `AssistantMessageEnd` - завершение генерации
- `Error` - сообщение об ошибке
- `CancelGeneration` - команда отмены генерации
- `ConnectionAck` - подтверждение соединения
- `Ping` / `Pong` - проверка соединения

#### Payload классы
- `MessageChunkPayload` - для фрагментов сообщений
- `UserMessagePayload` - для сообщений пользователя
- `ErrorPayload` - для ошибок
- `ConnectionAckPayload` - для подтверждения соединения

### 2. WebSocket Endpoint (Program.cs)

**URL:** `ws://localhost:5000/ws?dialogueId={id}`

**Параметры:**
- `dialogueId` (обязательный) - ID диалога для привязки соединения

**Функциональность:**
- Проверка валидности WebSocket запроса
- Валидация параметра dialogueId
- Проверка существования диалога в БД
- Установка WebSocket соединения
- Отправка подтверждения соединения (ConnectionAck)
- Обработка входящих сообщений (Ping, UserMessage, CancelGeneration)
- Обработка закрытия соединения
- Логирование всех событий

**Обработка ошибок:**
- 400 Bad Request - если не WebSocket запрос или отсутствует dialogueId
- 404 Not Found - если диалог не найден
- Отправка Error сообщений при ошибках парсинга JSON

### 3. Middleware

WebSocket middleware включен в pipeline через `app.UseWebSockets()` в Program.cs.

## Тестирование

### Автоматические тесты (Tests/WebSocketInfrastructureTests.cs)

Запуск:
```bash
# Сначала запустите приложение
dotnet run

# В другом терминале запустите тесты
dotnet run test-websocket
```

**Тесты включают:**
1. `TestWebSocketConnection()` - проверка полного цикла подключения
   - Создание тестового диалога
   - Подключение к WebSocket
   - Получение ConnectionAck
   - Отправка Ping и получение Pong
   - Закрытие соединения
   - Удаление тестового диалога

2. `TestInvalidRequests()` - проверка обработки некорректных запросов
   - Подключение без dialogueId
   - Подключение с несуществующим dialogueId
   - Подключение с некорректным форматом dialogueId

### Ручное тестирование (wwwroot/test-websocket.html)

Веб-интерфейс для тестирования WebSocket соединения.

**Доступ:**
```
http://localhost:5000/test-websocket.html
```

**Функции:**
- Подключение к WebSocket с указанием dialogueId
- Отправка Ping сообщений
- Просмотр всех входящих/исходящих сообщений
- Отключение от WebSocket
- Очистка лога

## Примеры использования

### Подключение из JavaScript

```javascript
const dialogueId = 1;
const ws = new WebSocket(`ws://localhost:5000/ws?dialogueId=${dialogueId}`);

ws.onopen = () => {
    console.log('Connected');
};

ws.onmessage = (event) => {
    const message = JSON.parse(event.data);
    console.log('Received:', message.type, message.payload);
};

ws.onerror = (error) => {
    console.error('WebSocket error:', error);
};

ws.onclose = (event) => {
    console.log('Disconnected:', event.code, event.reason);
};
```

### Отправка сообщения

```javascript
const message = {
    type: 'ping',
    payload: { test: 'data' },
    timestamp: new Date().toISOString()
};

ws.send(JSON.stringify(message));
```

### Подключение из C#

```csharp
using var ws = new ClientWebSocket();
var uri = new Uri($"ws://localhost:5000/ws?dialogueId={dialogueId}");

await ws.ConnectAsync(uri, CancellationToken.None);

// Получение сообщения
var buffer = new byte[1024 * 4];
var result = await ws.ReceiveAsync(
    new ArraySegment<byte>(buffer), 
    CancellationToken.None);

var messageJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
var message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson);

// Отправка сообщения
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
```

## Логирование

Все события WebSocket логируются:
- Установка соединения (Information)
- Получение сообщений (Debug)
- Ошибки парсинга (Error)
- Неизвестные типы сообщений (Warning)
- Закрытие соединения (Information)

## Следующие шаги

В следующих задачах будут реализованы:
1. WebSocketManager - управление множественными соединениями
2. StreamingService - потоковая передача ответов LLM
3. Интеграция с ILlmService для streaming
4. Обработка UserMessage и CancelGeneration

## Требования

Реализованные требования из спецификации:
- ✅ 1.1 - Установка WebSocket соединения при загрузке
- ✅ 1.4 - Передача сообщений через WebSocket
- ✅ Валидация dialogueId
- ✅ Обработка query параметров
- ✅ Логирование событий соединения

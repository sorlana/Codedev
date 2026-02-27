# Руководство по тестированию WebSocket функциональности

## Обзор

Данное руководство описывает процесс ручного тестирования backend WebSocket функциональности для задачи 6 checkpoint.

## Предварительные требования

1. Приложение должно быть запущено: `dotnet run`
2. Приложение слушает на порту (обычно 5111 или 5000)
3. Установлен wscat (опционально): `npm install -g wscat`

## Автоматические тесты

### Запуск встроенных тестов

Приложение включает два набора тестов:

1. **Тесты инфраструктуры WebSocket**
   ```bash
   dotnet run test-websocket
   ```
   
   Проверяет:
   - Установку WebSocket соединения
   - Обработку ping/pong
   - Валидацию параметров (dialogueId)
   - Обработку некорректных запросов

2. **Интеграционные тесты WebSocket**
   ```bash
   dotnet run test-websocket-integration
   ```
   
   Проверяет:
   - Сериализацию/десериализацию сообщений
   - Структуру типов сообщений
   - Корректность payload

## Ручное тестирование с wscat

### 1. Подключение к WebSocket

```bash
# Сначала создайте диалог через HTTP API
curl -X POST http://localhost:5111/api/dialogues \
  -H "Content-Type: application/json" \
  -d "{\"projectPath\": \"C:\\\\Projects\\\\Test\"}"

# Запомните dialogueId из ответа (например, 1)

# Подключитесь к WebSocket
wscat -c ws://localhost:5111/ws?dialogueId=1
```

### 2. Тестирование ping/pong

После подключения отправьте:
```json
{"type":"ping","payload":null}
```

Ожидаемый ответ:
```json
{"type":"pong","payload":{"connectionId":"..."},"timestamp":"..."}
```

### 3. Тестирование отправки сообщения

Отправьте:
```json
{"type":"user_message","payload":{"content":"Hello, test message"}}
```

Ожидаемые ответы:
1. `assistant_message_start` - начало генерации
2. Несколько `assistant_message_chunk` - фрагменты ответа
3. `assistant_message_end` - завершение с полным ответом

### 4. Тестирование отмены генерации

Во время генерации отправьте:
```json
{"type":"cancel_generation","payload":null}
```

Генерация должна прерваться, и вы получите `assistant_message_end` с частичным ответом.

## Проверка логирования

### Логи соединения

При установке соединения должны появиться логи:
```
info: CSharpRefactoringAssistant[0]
      WebSocket соединение установлено: ConnectionId={guid}, DialogueId={id}, Timestamp={time}
```

При регистрации в WebSocketManager:
```
info: CSharpRefactoringAssistant.Services.WebSocketManager[0]
      WebSocket соединение зарегистрировано: ConnectionId={guid}, DialogueId={id}, Timestamp={time}
```

### Логи сообщений

При получении сообщения пользователя:
```
info: CSharpRefactoringAssistant[0]
      Получено сообщение пользователя: DialogueId={id}, ConnectionId={guid}
```

При начале streaming:
```
info: CSharpRefactoringAssistant.Services.StreamingService[0]
      Начало потоковой генерации: DialogueId={id}, ConnectionId={guid}, Timestamp={time}
```

При завершении streaming:
```
info: CSharpRefactoringAssistant.Services.StreamingService[0]
      Потоковая генерация завершена: DialogueId={id}, ConnectionId={guid}, MessageId={id}, Length={length}, Timestamp={time}
```

### Логи отключения

При закрытии соединения:
```
info: CSharpRefactoringAssistant[0]
      WebSocket соединение закрыто: ConnectionId={guid}, Timestamp={time}

info: CSharpRefactoringAssistant.Services.WebSocketManager[0]
      WebSocket соединение удалено: ConnectionId={guid}, DialogueId={id}, Timestamp={time}
```

## Проверочный список (Checklist)

### Backend тесты
- [x] Сборка проекта успешна (`dotnet build`)
- [ ] Тесты инфраструктуры проходят (`dotnet run test-websocket`)
- [ ] Интеграционные тесты проходят (`dotnet run test-websocket-integration`)

### Ручное тестирование
- [ ] WebSocket соединение устанавливается с валидным dialogueId
- [ ] WebSocket соединение отклоняется без dialogueId
- [ ] WebSocket соединение отклоняется с несуществующим dialogueId
- [ ] Ping/pong работает корректно
- [ ] Отправка сообщения пользователя работает
- [ ] Streaming ответа работает (получение фрагментов)
- [ ] Отмена генерации работает
- [ ] Обработка ошибок работает корректно

### Логирование
- [ ] Логируется установка соединения
- [ ] Логируется регистрация в WebSocketManager
- [ ] Логируется получение сообщений
- [ ] Логируется начало/завершение streaming
- [ ] Логируется отключение соединения
- [ ] Логируются ошибки с достаточной детализацией

## Известные проблемы

1. **Порт может отличаться**: Проверьте вывод `dotnet run` для определения фактического порта
2. **Streaming требует LLM**: Для полного тестирования streaming нужна настроенная LLM (OpenAI или Ollama)
3. **Serena MCP**: Некоторые функции требуют запущенный Serena MCP сервер

## Результаты тестирования

### Дата: [Заполнить после тестирования]

**Backend тесты:**
- Сборка: ✓ / ✗
- Тесты инфраструктуры: ✓ / ✗
- Интеграционные тесты: ✓ / ✗

**Ручное тестирование:**
- Подключение: ✓ / ✗
- Ping/pong: ✓ / ✗
- Отправка сообщений: ✓ / ✗
- Streaming: ✓ / ✗
- Отмена: ✓ / ✗

**Логирование:**
- События соединения: ✓ / ✗
- События сообщений: ✓ / ✗
- Ошибки: ✓ / ✗

**Общий результат:** ✓ Пройдено / ✗ Не пройдено

**Комментарии:**
[Заполнить после тестирования]

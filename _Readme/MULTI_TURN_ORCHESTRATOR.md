# Multi-Turn Tool Calling с DeepSeek API

## Обзор

Реализован паттерн multi-turn tool calling для DeepSeek API согласно официальной документации. DeepSeek не управляет multi-turn диалогом автоматически - вся логика реализована на стороне клиента через сервис-оркестратор.

## Архитектура

### Компоненты

1. **DeepSeekOrchestratorService** - управляет циклом multi-turn диалога
2. **TaskExecutionService** - использует оркестратор для выполнения задач
3. **Message.ReasoningContent** - хранит цепочку рассуждений модели

### Схема работы

```
Пользователь → TaskExecutionService → DeepSeekOrchestratorService
                                              ↓
                                    Цикл суб-запросов:
                                    1. Запрос к DeepSeek API
                                    2. Получение reasoning_content
                                    3. Обработка tool_calls
                                    4. Выполнение инструментов
                                    5. Добавление результатов в историю
                                    6. Повтор до finish_reason = "stop"
```

## Ключевые особенности

### 1. Reasoning Content

DeepSeek в режиме thinking генерирует:
- `reasoning_content` - цепочка рассуждений (ОБЯЗАТЕЛЬНО передавать обратно)
- `content` - финальный ответ

**КРИТИЧЕСКИ ВАЖНО**: reasoning_content должен передаваться в следующем суб-запросе, иначе API вернет ошибку 400.

### 2. Цикл суб-запросов

Один "раунд" (turn) может состоять из нескольких "суб-запросов" (sub-turns):

```
Turn 1:
  Sub-turn 1: модель вызывает activate_project
  Sub-turn 2: модель вызывает find_symbol
  Sub-turn 3: модель вызывает replace_symbol_body
  Sub-turn 4: модель возвращает финальный ответ (finish_reason = "stop")
```

### 3. Защита от зацикливания

- Максимум 15 суб-запросов на один раунд (настраивается)
- Логирование каждого шага
- Отправка прогресса через WebSocket

### 4. История сообщений

Формат сообщений в истории:

```json
[
  { "role": "system", "content": "..." },
  { "role": "user", "content": "..." },
  { 
    "role": "assistant", 
    "reasoning_content": "...",
    "tool_calls": [...]
  },
  { "role": "tool", "tool_call_id": "...", "content": "..." },
  ...
]
```

## Использование

### Регистрация в DI

```csharp
builder.Services.AddScoped<IDeepSeekOrchestratorService, DeepSeekOrchestratorService>();
```

### Вызов оркестратора

```csharp
var orchestratorResult = await _orchestrator.ExecuteTurnAsync(
    dialogueId,
    messages,
    tools,
    async (functionName, argumentsJson) =>
    {
        // Callback для выполнения инструментов
        return await ExecuteToolAsync(dialogueId, projectPath, functionName, argumentsJson, result);
    },
    maxSubTurns: 15);
```

## Миграция БД

Добавлено поле `ReasoningContent` в таблицу `Messages`:

```bash
dotnet ef migrations add AddReasoningContentToMessage
dotnet ef database update
```

## Мониторинг

Оркестратор отправляет прогресс через WebSocket:

```json
{
  "type": "task_execution_progress",
  "payload": {
    "current": 3,
    "total": 15,
    "message": "Обработка запроса 3/15..."
  }
}
```

## Логирование

Каждый суб-запрос логируется:

```
Turn 6, Sub-turn 1: отправка запроса к DeepSeek API
Sub-turn 1: вызов инструмента create_file
Turn 6, Sub-turn 2: отправка запроса к DeepSeek API
Sub-turn 2: вызов инструмента create_file
...
DeepSeek завершил работу после 4 суб-запросов
```

## Отличия от старой реализации

### Было (одиночный запрос)
- Модель останавливалась после первого вызова инструмента
- Не передавался reasoning_content
- Простой цикл while с проверкой finish_reason

### Стало (multi-turn через оркестратор)
- Модель продолжает работу после вызова инструментов
- Reasoning_content передается в каждом суб-запросе
- Отдельный сервис-оркестратор с полным контролем цикла
- Мониторинг прогресса через WebSocket
- Защита от зацикливания

## Ссылки

- [Официальная документация DeepSeek](https://api-docs.deepseek.com/)
- [Пример multi-turn tool calling](https://github.com/deepseek-ai/agentic-patterns-course-deepseek)

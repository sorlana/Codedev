# Исправление проблемы с генерацией JSON плана задач

## Проблема 1: LLM возвращала текст вместо JSON
LLM возвращала текст вместо JSON при генерации плана задач, что приводило к ошибкам парсинга:
- `'0xD0' is an invalid start of a value` (кириллица в начале ответа)
- `Ответ не является JSON объектом. Начинается с: Судя по предоставленному описанию...`

## Проблема 2: Таймаут при генерации плана
Запрос к LLM превышал таймаут HttpClient (100 секунд по умолчанию):
- `TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing`
- Генерация детального плана с полным кодом требует 3-5 минут

## Решение

### 1. Добавлена поддержка `forceJson` в OpenAI сервисе
**Файл**: `Services/OpenAiLlmService.cs`

Добавлен параметр `response_format` в запрос к OpenAI API:
```csharp
var requestBody = new
{
    model = _model,
    messages = messages,
    response_format = forceJson ? new { type = "json_object" } : null,
    tools = tools.Select(t => new { ... }).ToArray()
};
```

Добавлена сериализация с игнорированием null значений:
```csharp
var requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
{
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
});
```

### 2. Включен JSON mode в TaskPlannerService
**Файл**: `Services/TaskPlannerService.cs`

Передается `forceJson: true` при вызове LLM:
```csharp
var response = await llmService.SendPromptAsync(
    userPrompt, 
    messages, 
    new List<FunctionDefinition>(), 
    forceJson: true
);
```

### 3. Увеличен таймаут HttpClient
**Файлы**: `Services/OllamaLlmService.cs`, `Services/OpenAiLlmService.cs`

Установлен таймаут 10 минут для генерации детальных планов:
```csharp
public OllamaLlmService(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaLlmService> logger)
{
    _httpClient = httpClient;
    _httpClient.Timeout = TimeSpan.FromMinutes(10); // Увеличенный таймаут для генерации детальных планов
    // ...
}
```

### 4. Проверка сохранения в БД
Логика уже была правильной - план сохраняется ТОЛЬКО после успешного парсинга:
1. Получение ответа от LLM
2. Парсинг в `ParseTaskPlan()` (при ошибке - exception)
3. Сохранение в БД через `SaveTaskPlanToDbAsync()` (только если парсинг успешен)

## Результат
- OpenAI/DeepSeek будут возвращать только валидный JSON
- Ollama уже использовал `format: "json"` параметр
- Таймаут увеличен до 10 минут для длительной генерации
- Неудачные генерации не сохраняются в БД
- Ошибки парсинга логируются с подробностями

## Тестирование
1. Запустить приложение: `dotnet run`
2. Открыть http://localhost:5111
3. Создать группу диалогов с контекстом
4. Нажать кнопку "Задачи" для генерации плана
5. Дождаться завершения генерации (может занять 3-5 минут)
6. Проверить, что план генерируется в формате JSON и сохраняется в БД

## Статус
✅ Компиляция успешна
✅ Приложение запущено на http://localhost:5111
✅ Таймаут увеличен до 10 минут
⏳ Требуется тестирование генерации плана задач

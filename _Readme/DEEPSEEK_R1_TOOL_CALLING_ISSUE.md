# Проблема с DeepSeek-R1:7b и Tool Calling

## Проблема

Кнопка "Отправить" не работает при использовании модели `deepseek-r1:7b`.

## Причина

Модель **DeepSeek-R1:7b НЕ поддерживает tool calling** (function calling).

Проверка capabilities:
```bash
ollama show deepseek-r1:7b
```

Результат:
```
Capabilities
  completion
  thinking
```

Отсутствует capability `tools`, которая необходима для вызова функций (execute_shell_command, read_file, find_symbol и т.д.).

## Решение

### Вариант 1: Использовать Qwen2.5:7b (РЕКОМЕНДУЕТСЯ)

Qwen2.5:7b поддерживает tools и имеет схожий размер:

```json
{
  "Llm": {
    "Provider": "Ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "qwen2.5:7b"
    }
  }
}
```

Capabilities:
```
completion
tools
```

### Вариант 2: Использовать Llama3.1:8b

```json
{
  "Llm": {
    "Provider": "Ollama",
    "Ollama": {
      "BaseUrl": "http://localhost:11434",
      "Model": "llama3.1:8b"
    }
  }
}
```

Capabilities:
```
completion
tools
```

### Вариант 3: Проверить другие версии DeepSeek

Возможно, существуют другие версии DeepSeek с поддержкой tools:

```bash
ollama list | findstr deepseek
```

## Сравнение моделей

| Модель | Размер | Tool Calling | Рассуждения | Русский язык |
|--------|--------|--------------|-------------|--------------|
| deepseek-r1:7b | 4.7 GB | ❌ | ✅ Отлично | ✅ Хорошо |
| qwen2.5:7b | 4.7 GB | ✅ | ✅ Хорошо | ✅ Отлично |
| llama3.1:8b | 4.9 GB | ✅ | ⚠️ Базовые | ⚠️ Средне |

## Рекомендация

Используйте **Qwen2.5:7b** - она:
- Поддерживает tool calling
- Хорошо работает с рассуждениями
- Отлично понимает русский язык
- Имеет схожий размер с DeepSeek-R1:7b

## Как изменить модель

1. Откройте `appsettings.json`
2. Измените значение `Model`:
   ```json
   "Model": "qwen2.5:7b"
   ```
3. Перезапустите приложение:
   ```bash
   dotnet run
   ```

## Проверка поддержки tools

Чтобы проверить, поддерживает ли модель tool calling:

```bash
ollama show <model-name>
```

Ищите в разделе `Capabilities` строку `tools`.

# C# Refactoring Assistant

AI-ассистент для рефакторинга C# кода через Serena MCP + Git. Веб-приложение на ASP.NET Core с интерфейсом чата для выполнения семантического рефакторинга C# проектов с помощью естественного языка.

## Возможности

- 🤖 Интерпретация команд рефакторинга на естественном языке через LLM (OpenAI/DeepSeek)
- 🔍 Семантический анализ и редактирование C# кода через Serena MCP Server
- 💾 Автоматическое создание Git чекпоинтов перед каждым изменением
- ⏮️ Откат к любому предыдущему состоянию проекта
- 💬 Ведение истории диалогов для разных проектов
- 🌐 Веб-интерфейс для удобного взаимодействия

## Требования к системе

- .NET 10 SDK
- Git
- Docker (для запуска Serena MCP Server)
- OpenAI API ключ (DeepSeek или другой совместимый провайдер)

## Установка и настройка

### 1. Клонирование репозитория

```bash
git clone <repository-url>
cd CSharpRefactoringAssistant
```

### 2. Настройка конфигурации

Отредактируйте файл `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=refactoring.db"
  },
  "Llm": {
    "Provider": "OpenAI",
    "OpenAI": {
      "ApiKey": "ваш-api-ключ",
      "Model": "deepseek-chat",
      "BaseUrl": "https://api.deepseek.com/v1"
    }
  },
  "Serena": {
    "StdioCommand": "docker",
    "StdioArgs": ["exec", "-i", "serena-container", "serena-mcp"]
  },
  "Security": {
    "AllowedRootDirectory": null
  }
}
```

**Важно:** Замените `"ваш-api-ключ"` на реальный API ключ от DeepSeek или OpenAI.

### 3. Запуск Serena MCP Server в Docker

```bash
# Создайте и запустите контейнер Serena
docker run -d --name serena-container serena-mcp-image

# Проверьте, что контейнер запущен
docker ps | grep serena-container
```

**Примечание:** Замените `serena-mcp-image` на актуальное имя образа Serena MCP Server.

### 4. Восстановление зависимостей

```bash
dotnet restore
```

### 5. Запуск приложения

```bash
dotnet run
```

Приложение будет доступно по адресу: `http://localhost:5000` (или другой порт, указанный в консоли).

## Использование

### Создание нового диалога

1. Откройте веб-интерфейс в браузере
2. В левой панели введите абсолютный путь к вашему C# проекту
3. Нажмите "Создать диалог"
4. Если проект не является Git репозиторием, он будет автоматически инициализирован

### Выполнение рефакторинга

Введите команду на естественном языке в поле ввода, например:

```
Найди все места, где используется атрибут [Authorize], и замени его на [CustomAuthorize] с проверкой роли Admin
```

```
Переименуй метод GetUserData в GetUserInformation во всех файлах проекта
```

```
Добавь логирование в начало каждого публичного метода в классе UserService
```

### Откат изменений

1. В правой панели отображаются все чекпоинты (Git коммиты)
2. Нажмите кнопку "Откатить" рядом с нужным чекпоинтом
3. Проект будет восстановлен до этого состояния

## Архитектура

### Компоненты

- **Frontend**: HTML/JavaScript веб-интерфейс
- **Backend**: ASP.NET Core 10 Minimal API
- **Database**: SQLite с Entity Framework Core
- **MCP Client**: JSON-RPC 2.0 клиент для Serena
- **LLM Service**: Интеграция с OpenAI API (DeepSeek)
- **Git Service**: Управление версиями и чекпоинтами

### Поток обработки запроса

1. Пользователь вводит команду в чат
2. Создается Git чекпоинт (коммит)
3. Команда отправляется в LLM с описанием доступных инструментов Serena
4. LLM возвращает последовательность вызовов инструментов
5. Инструменты выполняются через Serena MCP Server
6. Результаты сохраняются в базу данных и отображаются пользователю

## API Endpoints

### Диалоги

- `POST /api/dialogues` - Создать новый диалог
- `GET /api/dialogues` - Получить список всех диалогов
- `GET /api/dialogues/{id}` - Получить диалог с сообщениями и чекпоинтами

### Сообщения

- `POST /api/dialogues/{id}/messages` - Отправить сообщение в диалог

### Чекпоинты

- `GET /api/dialogues/{id}/checkpoints` - Получить список чекпоинтов
- `POST /api/dialogues/{id}/rollback` - Откатить проект к чекпоинту

## Безопасность

- Валидация путей к проектам (только абсолютные пути)
- Опциональное ограничение корневой директории через `AllowedRootDirectory`
- Санитизация путей для предотвращения command injection
- Проверка незакоммиченных изменений перед откатом

## Устранение неполадок

### Serena MCP Server недоступен

Если при запуске появляется ошибка инициализации MCP Client:

1. Проверьте, что Docker контейнер с Serena запущен: `docker ps`
2. Проверьте настройки в `appsettings.json` (StdioCommand и StdioArgs)
3. Убедитесь, что имя контейнера совпадает с указанным в конфигурации

### Ошибки LLM API

Если запросы к LLM не работают:

1. Проверьте правильность API ключа в `appsettings.json`
2. Убедитесь, что у вас есть доступ к API (проверьте баланс/лимиты)
3. Проверьте BaseUrl для вашего провайдера

### Git ошибки

Если возникают проблемы с Git:

1. Убедитесь, что Git установлен и доступен в PATH
2. Проверьте права доступа к папке проекта
3. Убедитесь, что нет незакоммиченных изменений перед откатом

## Разработка

### Структура проекта

```
CSharpRefactoringAssistant/
├── Data/                   # Entity Framework DbContext
├── Models/                 # Модели данных
├── Services/              # Бизнес-логика и сервисы
│   ├── GitService.cs
│   ├── McpClient.cs
│   ├── SerenaService.cs
│   ├── OpenAiLlmService.cs
│   ├── PromptProcessor.cs
│   └── PathValidator.cs
├── wwwroot/               # Статические файлы (HTML, JS, CSS)
├── Program.cs             # Точка входа и настройка API
└── appsettings.json       # Конфигурация
```

### Добавление новых инструментов Serena

1. Добавьте метод в `ISerenaService` и `SerenaService`
2. Добавьте определение функции в `PromptProcessor.GetSerenaToolDefinitions()`
3. Добавьте обработчик в `PromptProcessor.ExecuteFunctionCallAsync()`

## Лицензия

[Укажите лицензию]

## Контакты

[Укажите контактную информацию]

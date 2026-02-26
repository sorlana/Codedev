# Документ дизайна: UI Integration for Agent Task Executor

## Обзор

UI Integration for Agent Task Executor расширяет существующий веб-интерфейс чата функциональностью управления агентским режимом выполнения задач. Система интегрируется с существующим TaskExecutorService через PromptProcessor, добавляя распознавание команд на естественном языке, визуальные индикаторы прогресса, кнопки управления и детальную отчетность о каждом шаге выполнения.

Дизайн следует принципу минимальных изменений существующей архитектуры, используя уже имеющиеся паттерны и компоненты. Frontend расширяется новыми функциями для polling статуса выполнения и управления UI элементами. Backend расширяется логикой распознавания команд в PromptProcessor.

## Архитектура

### Общая архитектура

```
┌─────────────────────────────────────────────────────────────┐
│                      Browser (Frontend)                      │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  app.js (расширенный)                                  │ │
│  │  ├─ sendMessage() - отправка команд                    │ │
│  │  ├─ pollExecutionStatus() - опрос статуса (NEW)       │ │
│  │  ├─ updateControlButtons() - управление кнопками (NEW)│ │
│  │  ├─ updateStatusIndicator() - индикатор прогресса(NEW)│ │
│  │  └─ displayProgressMessage() - отображение сообщений  │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                            │ HTTP/JSON
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core Backend                      │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  PromptProcessor (расширенный)                         │ │
│  │  ├─ ProcessPromptAsync() - основная обработка         │ │
│  │  ├─ IsAgentCommand() - распознавание команд (NEW)     │ │
│  │  ├─ ExecuteAgentCommand() - выполнение команд (NEW)   │ │
│  │  └─ ExtractTasksFilePath() - извлечение пути (NEW)    │ │
│  └────────────────────────────────────────────────────────┘ │
│                            │                                 │
│                            ↓                                 │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  TaskExecutorService (существующий)                    │ │
│  │  ├─ ExecuteTasksAsync()                                │ │
│  │  ├─ StopExecutionAsync()                               │ │
│  │  ├─ ResumeExecutionAsync()                             │ │
│  │  └─ GetExecutionStatusAsync()                          │ │
│  └────────────────────────────────────────────────────────┘ │
│                            │                                 │
│                            ↓                                 │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  RefactoringDbContext                                  │ │
│  │  ├─ Dialogues                                          │ │
│  │  ├─ Messages                                           │ │
│  │  └─ ExecutionSessions                                  │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Поток данных

1. **Запуск выполнения:**
   - Пользователь вводит команду "начни выполнение задач из tasks.md"
   - Frontend отправляет через sendMessage()
   - PromptProcessor распознает команду
   - PromptProcessor вызывает TaskExecutorService.ExecuteTasksAsync()
   - TaskExecutorService создает ExecutionSession и запускает фоновое выполнение
   - PromptProcessor возвращает подтверждающее сообщение
   - Frontend начинает polling статуса

2. **Отчетность о прогрессе:**
   - TaskExecutorService сохраняет сообщения о прогрессе в Messages
   - Frontend периодически вызывает loadMessages()
   - Frontend отображает новые сообщения с эмодзи и форматированием
   - Frontend обновляет индикатор прогресса из ExecutionStatus

3. **Управление выполнением:**
   - Пользователь нажимает кнопку "Остановить"
   - Frontend отправляет команду "останови выполнение"
   - PromptProcessor вызывает TaskExecutorService.StopExecutionAsync()
   - TaskExecutorService устанавливает CancellationToken
   - Frontend обновляет состояние кнопок

## Компоненты и интерфейсы

### Backend компоненты

#### 1. PromptProcessor (расширение существующего)

```csharp
public class PromptProcessor : IPromptProcessor
{
    private readonly ITaskExecutorService _taskExecutorService;
    
    // Существующие зависимости...
    
    public async Task<string> ProcessPromptAsync(int dialogueId, string prompt)
    {
        // Проверка на команду агентского режима
        if (IsAgentCommand(prompt, out var commandType, out var filePath))
        {
            return await ExecuteAgentCommandAsync(dialogueId, commandType, filePath);
        }
        
        // Существующая логика обработки промптов...
    }
    
    private bool IsAgentCommand(string prompt, out AgentCommandType commandType, out string? filePath)
    {
        // Распознавание команд на русском и английском
        // Поддержка вариаций команд
    }
    
    private async Task<string> ExecuteAgentCommandAsync(
        int dialogueId, 
        AgentCommandType commandType, 
        string? filePath)
    {
        // Выполнение команд через TaskExecutorService
    }
    
    private string? ExtractTasksFilePath(string prompt)
    {
        // Извлечение пути к файлу из команды
    }
}

public enum AgentCommandType
{
    StartExecution,
    StopExecution,
    ResumeExecution,
    ShowStatus
}
```

#### 2. CommandRecognizer (новый компонент)

```csharp
public class CommandRecognizer
{
    private static readonly Dictionary<AgentCommandType, List<string>> CommandPatterns = new()
    {
        [AgentCommandType.StartExecution] = new()
        {
            "начни выполнение", "запусти выполнение", "выполни задачи",
            "start execution", "execute tasks", "run tasks"
        },
        [AgentCommandType.StopExecution] = new()
        {
            "останови выполнение", "стоп", "прекрати выполнение",
            "stop execution", "stop", "halt execution"
        },
        [AgentCommandType.ResumeExecution] = new()
        {
            "продолжи выполнение", "возобнови выполнение", "продолжить",
            "resume execution", "continue execution", "resume"
        },
        [AgentCommandType.ShowStatus] = new()
        {
            "покажи статус", "статус выполнения", "что происходит",
            "show status", "execution status", "status"
        }
    };
    
    public bool TryRecognizeCommand(
        string prompt, 
        out AgentCommandType commandType, 
        out string? filePath)
    {
        // Нормализация промпта (lowercase, trim)
        var normalized = prompt.ToLowerInvariant().Trim();
        
        // Поиск совпадений с паттернами
        foreach (var (type, patterns) in CommandPatterns)
        {
            if (patterns.Any(p => normalized.Contains(p)))
            {
                commandType = type;
                filePath = ExtractFilePath(prompt);
                return true;
            }
        }
        
        commandType = default;
        filePath = null;
        return false;
    }
    
    private string? ExtractFilePath(string prompt)
    {
        // Регулярное выражение для извлечения пути к файлу
        // Паттерны: "из файла X", "from file X", "из X"
        var patterns = new[]
        {
            @"из\s+файла\s+([^\s]+)",
            @"из\s+([^\s]+\.md)",
            @"from\s+file\s+([^\s]+)",
            @"from\s+([^\s]+\.md)"
        };
        
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(prompt, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        
        return null;
    }
}
```

#### 3. TasksFilePathResolver (новый компонент)

```csharp
public class TasksFilePathResolver
{
    private readonly PathValidator _pathValidator;
    
    public async Task<string> ResolveTasksFilePathAsync(
        string? userProvidedPath, 
        string projectPath)
    {
        // Если путь указан пользователем
        if (!string.IsNullOrEmpty(userProvidedPath))
        {
            var fullPath = Path.Combine(projectPath, userProvidedPath);
            
            // Валидация пути
            if (!_pathValidator.IsPathValid(fullPath, projectPath))
            {
                throw new InvalidOperationException(
                    $"Невалидный путь к файлу: {userProvidedPath}");
            }
            
            // Проверка существования
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Файл не найден: {userProvidedPath}");
            }
            
            return fullPath;
        }
        
        // Поиск tasks.md в корне проекта
        var defaultPath = Path.Combine(projectPath, "tasks.md");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }
        
        throw new FileNotFoundException(
            "Файл tasks.md не найден в корне проекта. Укажите путь к файлу.");
    }
}
```

### Frontend компоненты

#### 1. Расширение app.js

```javascript
// Глобальные переменные для управления polling
let executionPollingInterval = null;
let currentExecutionStatus = 'none';

// Инициализация при загрузке страницы
document.addEventListener('DOMContentLoaded', () => {
    // Существующая инициализация...
    
    // Добавление обработчиков для кнопок управления
    setupExecutionControlButtons();
});

// Настройка кнопок управления выполнением
function setupExecutionControlButtons() {
    const stopButton = document.getElementById('stop-execution-btn');
    const resumeButton = document.getElementById('resume-execution-btn');
    
    if (stopButton) {
        stopButton.addEventListener('click', stopExecution);
    }
    
    if (resumeButton) {
        resumeButton.addEventListener('click', resumeExecution);
    }
}

// Polling статуса выполнения
async function pollExecutionStatus() {
    if (!currentDialogueId) {
        stopPollingExecutionStatus();
        return;
    }
    
    try {
        const response = await fetch(
            `${API_BASE}/api/dialogues/${currentDialogueId}/execution-status`
        );
        
        if (!response.ok) {
            console.error('Failed to fetch execution status');
            return;
        }
        
        const status = await response.json();
        
        // Обновление UI на основе статуса
        updateExecutionUI(status);
        
        // Загрузка новых сообщений
        await loadMessages(currentDialogueId);
        
        // Остановка polling если выполнение завершено
        if (status.status === 'completed' || 
            status.status === 'failed' || 
            status.status === 'none') {
            stopPollingExecutionStatus();
        }
        
    } catch (error) {
        console.error('Error polling execution status:', error);
    }
}

// Запуск polling
function startPollingExecutionStatus() {
    if (executionPollingInterval) {
        return; // Уже запущен
    }
    
    executionPollingInterval = setInterval(pollExecutionStatus, 2000);
    pollExecutionStatus(); // Немедленный первый вызов
}

// Остановка polling
function stopPollingExecutionStatus() {
    if (executionPollingInterval) {
        clearInterval(executionPollingInterval);
        executionPollingInterval = null;
    }
}

// Обновление UI на основе статуса
function updateExecutionUI(status) {
    currentExecutionStatus = status.status;
    
    // Обновление кнопок управления
    updateControlButtons(status.status);
    
    // Обновление индикатора прогресса
    updateStatusIndicator(status);
}

// Обновление кнопок управления
function updateControlButtons(status) {
    const stopButton = document.getElementById('stop-execution-btn');
    const resumeButton = document.getElementById('resume-execution-btn');
    const controlsContainer = document.getElementById('execution-controls');
    
    if (!stopButton || !resumeButton || !controlsContainer) {
        return;
    }
    
    // Показать/скрыть контейнер
    if (status === 'none' || status === 'completed') {
        controlsContainer.style.display = 'none';
        return;
    }
    
    controlsContainer.style.display = 'flex';
    
    // Управление видимостью кнопок
    if (status === 'running') {
        stopButton.style.display = 'inline-block';
        resumeButton.style.display = 'none';
    } else if (status === 'stopped' || status === 'failed') {
        stopButton.style.display = 'none';
        resumeButton.style.display = 'inline-block';
    }
}

// Обновление индикатора статуса
function updateStatusIndicator(status) {
    const indicator = document.getElementById('execution-status-indicator');
    
    if (!indicator) {
        return;
    }
    
    // Скрыть если нет активного выполнения
    if (status.status === 'none') {
        indicator.style.display = 'none';
        return;
    }
    
    indicator.style.display = 'block';
    
    // Формирование текста индикатора
    let statusText = '';
    let statusEmoji = '';
    
    switch (status.status) {
        case 'running':
            statusEmoji = '🔄';
            statusText = 'Выполняется...';
            break;
        case 'stopped':
            statusEmoji = '⏸️';
            statusText = 'Приостановлено';
            break;
        case 'completed':
            statusEmoji = '✅';
            statusText = 'Завершено';
            break;
        case 'failed':
            statusEmoji = '❌';
            statusText = 'Ошибка';
            break;
    }
    
    // Добавление прогресса
    let progressText = '';
    if (status.progress) {
        progressText = ` (${status.progress})`;
    }
    
    // Добавление текущей задачи
    let currentTaskText = '';
    if (status.currentTask) {
        const truncated = status.currentTask.substring(0, 50);
        currentTaskText = `<br><small>${truncated}${status.currentTask.length > 50 ? '...' : ''}</small>`;
    }
    
    indicator.innerHTML = `
        <span class="status-emoji">${statusEmoji}</span>
        <span class="status-text">${statusText}${progressText}</span>
        ${currentTaskText}
    `;
}

// Остановка выполнения
async function stopExecution() {
    if (!currentDialogueId) {
        return;
    }
    
    try {
        const stopButton = document.getElementById('stop-execution-btn');
        stopButton.disabled = true;
        stopButton.textContent = 'Останавливаю...';
        
        // Отправка команды через sendMessage
        const input = document.getElementById('prompt-input');
        input.value = 'останови выполнение';
        await sendMessage();
        
    } catch (error) {
        console.error('Error stopping execution:', error);
        showStatusMessage('Ошибка остановки выполнения', 'error');
    } finally {
        const stopButton = document.getElementById('stop-execution-btn');
        stopButton.disabled = false;
        stopButton.textContent = 'Остановить';
    }
}

// Возобновление выполнения
async function resumeExecution() {
    if (!currentDialogueId) {
        return;
    }
    
    try {
        const resumeButton = document.getElementById('resume-execution-btn');
        resumeButton.disabled = true;
        resumeButton.textContent = 'Возобновляю...';
        
        // Отправка команды через sendMessage
        const input = document.getElementById('prompt-input');
        input.value = 'продолжи выполнение';
        await sendMessage();
        
        // Запуск polling
        startPollingExecutionStatus();
        
    } catch (error) {
        console.error('Error resuming execution:', error);
        showStatusMessage('Ошибка возобновления выполнения', 'error');
    } finally {
        const resumeButton = document.getElementById('resume-execution-btn');
        resumeButton.disabled = false;
        resumeButton.textContent = 'Возобновить';
    }
}

// Расширение существующей функции sendMessage
async function sendMessage() {
    // Существующая логика...
    
    // После успешной отправки проверяем, была ли это команда запуска
    const content = input.value.trim().toLowerCase();
    if (content.includes('начни выполнение') || 
        content.includes('start execution') ||
        content.includes('execute tasks')) {
        // Запуск polling
        startPollingExecutionStatus();
    }
    
    // Остальная существующая логика...
}

// Расширение selectDialogue для остановки polling при смене диалога
async function selectDialogue(dialogueId) {
    // Остановка polling предыдущего диалога
    stopPollingExecutionStatus();
    
    // Существующая логика...
    
    // Проверка статуса нового диалога
    try {
        const response = await fetch(
            `${API_BASE}/api/dialogues/${dialogueId}/execution-status`
        );
        
        if (response.ok) {
            const status = await response.json();
            updateExecutionUI(status);
            
            // Запуск polling если выполнение активно
            if (status.status === 'running') {
                startPollingExecutionStatus();
            }
        }
    } catch (error) {
        console.error('Error checking execution status:', error);
    }
}
```

#### 2. Расширение index.html

```html
<!-- Добавление в #message-input-container -->
<div id="message-input-container">
    <!-- Индикатор статуса выполнения -->
    <div id="execution-status-indicator" style="display: none;">
        <span class="status-emoji"></span>
        <span class="status-text"></span>
    </div>
    
    <!-- Кнопки управления выполнением -->
    <div id="execution-controls" style="display: none;">
        <button id="stop-execution-btn" class="execution-control-btn">
            Остановить
        </button>
        <button id="resume-execution-btn" class="execution-control-btn" style="display: none;">
            Возобновить
        </button>
    </div>
    
    <!-- Существующие элементы -->
    <input type="text" id="prompt-input" placeholder="...">
    <button id="send-button">Отправить</button>
</div>
```

#### 3. Расширение CSS стилей

```css
/* Индикатор статуса выполнения */
#execution-status-indicator {
    padding: 12px 16px;
    background: #f8f9fa;
    border-radius: 8px;
    border-left: 4px solid #007bff;
    margin-bottom: 12px;
    display: flex;
    align-items: center;
    gap: 10px;
}

.status-emoji {
    font-size: 20px;
}

.status-text {
    font-weight: 500;
    color: #333;
}

#execution-status-indicator small {
    color: #666;
    font-size: 12px;
}

/* Кнопки управления выполнением */
#execution-controls {
    display: flex;
    gap: 10px;
    margin-bottom: 12px;
}

.execution-control-btn {
    padding: 10px 20px;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    font-weight: 500;
    transition: background 0.2s;
}

#stop-execution-btn {
    background: #dc3545;
    color: white;
}

#stop-execution-btn:hover {
    background: #c82333;
}

#stop-execution-btn:disabled {
    background: #ccc;
    cursor: not-allowed;
}

#resume-execution-btn {
    background: #28a745;
    color: white;
}

#resume-execution-btn:hover {
    background: #218838;
}

#resume-execution-btn:disabled {
    background: #ccc;
    cursor: not-allowed;
}

/* Стили для сообщений о прогрессе */
.message.assistant.progress {
    background: #e7f3ff;
    border-left: 4px solid #007bff;
}

.message.assistant.error {
    background: #f8d7da;
    border-left: 4px solid #dc3545;
}

.message.assistant.success {
    background: #d4edda;
    border-left: 4px solid #28a745;
}
```

## Модели данных

### ExecutionStatusDto (существующая модель)

```csharp
public class ExecutionStatusDto
{
    public string Status { get; set; } // "running", "stopped", "completed", "failed", "none"
    public string? Progress { get; set; } // "N/M"
    public string? CurrentTask { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### AgentCommandResult (новая модель)

```csharp
public class AgentCommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string? ErrorDetails { get; set; }
    public ExecutionStatusDto? Status { get; set; }
}
```

## Свойства корректности

*Свойство - это характеристика или поведение, которое должно выполняться во всех валидных выполнениях системы - по сути, формальное утверждение о том, что система должна делать. Свойства служат мостом между человекочитаемыми спецификациями и машинно-проверяемыми гарантиями корректности.*


### Свойство 1: Распознавание команд на нескольких языках

*For any* валидной команды управления агентским режимом (запуск, остановка, возобновление, статус) на русском или английском языке, с различными вариациями формулировок и регистром символов, система должна корректно распознать тип команды.

**Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 11.1, 11.2, 11.3, 11.4**

### Свойство 2: Использование контекста текущего диалога

*For any* распознанной команды управления, система должна использовать ID текущего активного диалога для выполнения операции, без необходимости явного указания ID пользователем.

**Validates: Requirements 1.9, 12.3**

### Свойство 3: Извлечение пути к файлу

*For any* команды запуска выполнения, если в тексте команды присутствует путь к файлу (паттерны "из файла X", "from file X"), система должна корректно извлечь этот путь.

**Validates: Requirements 1.10**

### Свойство 4: Валидация всех путей

*For any* пути к файлу, указанного пользователем или разрешенного автоматически, система должна валидировать путь через PathValidator перед использованием.

**Validates: Requirements 2.7**

### Свойство 5: Разрешение относительных путей

*For any* относительного пути к файлу tasks.md, система должна разрешать путь относительно корня проекта текущего диалога.

**Validates: Requirements 2.6**

### Свойство 6: Проверка каждого сообщения

*For any* сообщения от пользователя, PromptProcessor должен проверить, является ли оно командой управления агентским режимом, перед отправкой в LLM.

**Validates: Requirements 3.1**

### Свойство 7: Изоляция команд от LLM

*For any* команды управления агентским режимом, система НЕ должна отправлять эту команду в LLM, а должна обработать её локально через TaskExecutorService.

**Validates: Requirements 3.6**

### Свойство 8: Подтверждение успешного выполнения

*For any* успешно выполненной команды управления, система должна вернуть пользователю подтверждающее сообщение с описанием выполненного действия.

**Validates: Requirements 3.7**

### Свойство 9: Сообщения об ошибках с объяснением

*For any* ошибки при выполнении команды управления, система должна вернуть пользователю сообщение об ошибке с объяснением причины.

**Validates: Requirements 3.8**

### Свойство 10: Соответствие состояния кнопок статусу выполнения

*For any* статуса выполнения (running, stopped, completed, failed, none), состояние кнопок управления (видимость, активность) должно соответствовать текущему статусу согласно правилам:
- running → показать "Остановить", скрыть "Возобновить"
- stopped/failed → скрыть "Остановить", показать "Возобновить"
- completed/none → скрыть обе кнопки

**Validates: Requirements 5.1, 5.2, 5.4, 5.5, 5.7, 5.8**

### Свойство 11: Актуальность индикатора статуса

*For any* статуса выполнения, индикатор статуса должен отображать:
- Корректный эмодзи для статуса (🔄 running, ⏸️ stopped, ✅ completed, ❌ failed)
- Текстовое описание статуса
- Прогресс выполнения (N/M) если доступен
- Текущую задачу (первые 50 символов) если доступна

**Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.7, 6.9**

### Свойство 12: Активность polling соответствует статусу

*For any* диалога, polling статуса выполнения должен быть активен тогда и только тогда, когда статус выполнения является "running", и должен быть остановлен для статусов "completed", "failed", "none".

**Validates: Requirements 7.1, 7.6**

### Свойство 13: Полное обновление UI при изменении статуса

*For any* обновления статуса выполнения, полученного через polling, система должна обновить все связанные UI элементы (кнопки управления, индикатор статуса, список сообщений) без перезагрузки страницы.

**Validates: Requirements 7.2, 7.3**

### Свойство 14: Автоматическая прокрутка к новым сообщениям

*For any* новых сообщений о прогрессе, добавленных в чат, интерфейс должен автоматически прокрутить список сообщений к последнему сообщению.

**Validates: Requirements 7.4**

### Свойство 15: Устойчивость к ошибкам сети

*For any* ошибки сети при опросе статуса выполнения, интерфейс должен обработать ошибку без прерывания работы (логирование в консоль, продолжение polling).

**Validates: Requirements 7.7**

### Свойство 16: Управление polling при смене диалога

*For any* переключения между диалогами, система должна:
- Остановить polling для предыдущего диалога
- Проверить статус нового диалога
- Запустить polling если новый диалог имеет активное выполнение (status="running")

**Validates: Requirements 7.8, 7.9**

### Свойство 17: Корректный рендеринг сообщений о прогрессе

*For any* сообщения о прогрессе, сохраненного в базе данных, интерфейс должен:
- Отобразить сообщение в хронологическом порядке
- Использовать role="assistant" для стилизации
- Корректно рендерить Unicode эмодзи
- Корректно рендерить Markdown форматирование
- Применить специальную стилизацию для ошибок (красный) и успеха (зеленый)

**Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7**

### Свойство 18: Использование контекста диалога

*For any* команды управления, не содержащей явного указания пути к файлу или ID диалога, система должна использовать сохраненный контекст текущего диалога (путь к файлу из последней сессии, ID текущего диалога).

**Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5**

## Обработка ошибок

### Типы ошибок

1. **Ошибки валидации команд:**
   - Неизвестная команда → игнорировать, передать в LLM как обычный промпт
   - Невалидный путь к файлу → вернуть сообщение об ошибке с объяснением
   - Файл не найден → вернуть сообщение с предложением указать путь

2. **Ошибки выполнения:**
   - Диалог не найден (404) → отобразить "Диалог не найден"
   - Нет остановленной сессии для возобновления → отобразить "Нет остановленной сессии"
   - Выполнение уже запущено → отобразить "Выполнение уже запущено"

3. **Ошибки сети:**
   - Таймаут запроса → логировать, продолжить polling
   - Ошибка соединения → отобразить "Ошибка соединения. Проверьте подключение."
   - Ошибка сервера (500) → отобразить "Внутренняя ошибка сервера. Попробуйте позже."

### Стратегия обработки

```csharp
// Backend: PromptProcessor
public async Task<string> ProcessPromptAsync(int dialogueId, string prompt)
{
    try
    {
        // Попытка распознать команду
        if (_commandRecognizer.TryRecognizeCommand(prompt, out var commandType, out var filePath))
        {
            try
            {
                return await ExecuteAgentCommandAsync(dialogueId, commandType, filePath);
            }
            catch (FileNotFoundException ex)
            {
                return $"❌ {ex.Message}";
            }
            catch (InvalidOperationException ex)
            {
                return $"❌ {ex.Message}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing agent command");
                return "❌ Ошибка выполнения команды. Попробуйте позже.";
            }
        }
        
        // Не команда - обычная обработка через LLM
        return await ProcessWithLlmAsync(dialogueId, prompt);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing prompt");
        throw;
    }
}
```

```javascript
// Frontend: обработка ошибок polling
async function pollExecutionStatus() {
    try {
        const response = await fetch(
            `${API_BASE}/api/dialogues/${currentDialogueId}/execution-status`
        );
        
        if (!response.ok) {
            if (response.status === 404) {
                console.error('Dialogue not found');
                stopPollingExecutionStatus();
                return;
            }
            
            console.error('Failed to fetch execution status:', response.status);
            // Продолжаем polling несмотря на ошибку
            return;
        }
        
        const status = await response.json();
        updateExecutionUI(status);
        
    } catch (error) {
        // Ошибка сети - логируем и продолжаем
        console.error('Network error polling execution status:', error);
        // Не останавливаем polling - возможно временная проблема
    }
}
```

## Стратегия тестирования

### Dual Testing Approach

Система требует комбинации unit-тестов и property-based тестов для полного покрытия:

- **Unit-тесты**: Проверяют конкретные примеры, edge cases и интеграционные точки
- **Property-тесты**: Проверяют универсальные свойства на множестве сгенерированных входных данных

### Unit-тесты

**Backend (C# + xUnit):**

1. **CommandRecognizer тесты:**
   - Распознавание команды "начни выполнение задач из tasks.md"
   - Распознавание команды "execute tasks from tasks.md"
   - Распознавание команды "останови выполнение"
   - Распознавание команды "stop execution"
   - Распознавание команды "продолжи выполнение"
   - Распознавание команды "resume execution"
   - Распознавание команды "покажи статус"
   - Распознавание команды "show status"
   - Игнорирование регистра символов
   - Извлечение пути из команды "из файла .kiro/specs/feature/tasks.md"
   - Извлечение пути из команды "from file tasks.md"
   - Возврат null для команд без пути

2. **TasksFilePathResolver тесты:**
   - Разрешение пути "tasks.md" → "{projectPath}/tasks.md"
   - Разрешение пути ".kiro/specs/feature/tasks.md" → "{projectPath}/.kiro/specs/feature/tasks.md"
   - Выброс FileNotFoundException если файл не найден
   - Выброс InvalidOperationException если путь невалиден
   - Валидация через PathValidator

3. **PromptProcessor интеграционные тесты:**
   - Обработка команды запуска → вызов TaskExecutorService.ExecuteTasksAsync
   - Обработка команды остановки → вызов TaskExecutorService.StopExecutionAsync
   - Обработка команды возобновления → вызов TaskExecutorService.ResumeExecutionAsync
   - Обработка команды статуса → вызов TaskExecutorService.GetExecutionStatusAsync
   - Команды управления НЕ отправляются в LLM
   - Обычные промпты отправляются в LLM
   - Возврат подтверждающего сообщения при успехе
   - Возврат сообщения об ошибке при неудаче

**Frontend (JavaScript + Jest или manual testing):**

1. **pollExecutionStatus тесты:**
   - Вызов API endpoint с правильным dialogueId
   - Обновление UI при получении статуса
   - Остановка polling при status="completed"
   - Остановка polling при status="failed"
   - Остановка polling при status="none"
   - Продолжение polling при status="running"
   - Обработка ошибки 404 (остановка polling)
   - Обработка ошибки сети (продолжение polling)

2. **updateControlButtons тесты:**
   - Показ кнопки "Остановить" при status="running"
   - Скрытие кнопки "Возобновить" при status="running"
   - Скрытие кнопки "Остановить" при status="stopped"
   - Показ кнопки "Возобновить" при status="stopped"
   - Скрытие обеих кнопок при status="completed"
   - Скрытие обеих кнопок при status="none"

3. **updateStatusIndicator тесты:**
   - Отображение эмодзи 🔄 для status="running"
   - Отображение эмодзи ⏸️ для status="stopped"
   - Отображение эмодзи ✅ для status="completed"
   - Отображение эмодзи ❌ для status="failed"
   - Отображение прогресса "N/M задач"
   - Отображение текущей задачи (первые 50 символов)
   - Скрытие индикатора при status="none"

4. **selectDialogue тесты:**
   - Остановка polling предыдущего диалога
   - Проверка статуса нового диалога
   - Запуск polling если новый диалог имеет status="running"
   - Не запускать polling если новый диалог имеет status="none"

### Property-Based тесты

**Конфигурация:** Минимум 100 итераций на тест, использование библиотеки FsCheck для .NET.

**Property 1: Распознавание команд (Requirements 1.1-1.8)**
```csharp
[Property]
public Property CommandRecognizer_RecognizesAllValidCommands()
{
    return Prop.ForAll(
        GenerateValidCommand(),
        command =>
        {
            var recognizer = new CommandRecognizer();
            var result = recognizer.TryRecognizeCommand(
                command.Text, 
                out var commandType, 
                out var filePath);
            
            return result && commandType == command.ExpectedType;
        });
}

// Generator для валидных команд
static Arbitrary<ValidCommand> GenerateValidCommand()
{
    var patterns = new[]
    {
        ("начни выполнение", AgentCommandType.StartExecution),
        ("запусти выполнение", AgentCommandType.StartExecution),
        ("start execution", AgentCommandType.StartExecution),
        ("execute tasks", AgentCommandType.StartExecution),
        ("останови выполнение", AgentCommandType.StopExecution),
        ("stop execution", AgentCommandType.StopExecution),
        ("продолжи выполнение", AgentCommandType.ResumeExecution),
        ("resume execution", AgentCommandType.ResumeExecution),
        ("покажи статус", AgentCommandType.ShowStatus),
        ("show status", AgentCommandType.ShowStatus)
    };
    
    return Gen.Elements(patterns)
        .Select(p => new ValidCommand
        {
            Text = ApplyRandomVariations(p.Item1),
            ExpectedType = p.Item2
        })
        .ToArbitrary();
}

// Применение случайных вариаций (регистр, пробелы)
static string ApplyRandomVariations(string text)
{
    var random = new Random();
    
    // Случайный регистр
    if (random.Next(2) == 0)
    {
        text = text.ToUpper();
    }
    else if (random.Next(2) == 0)
    {
        text = char.ToUpper(text[0]) + text.Substring(1);
    }
    
    // Добавление пробелов
    if (random.Next(2) == 0)
    {
        text = "  " + text + "  ";
    }
    
    return text;
}
```

**Property 2: Валидация всех путей (Requirements 2.7)**
```csharp
[Property]
public Property TasksFilePathResolver_ValidatesAllPaths()
{
    return Prop.ForAll(
        Arb.Default.NonEmptyString(),
        Arb.Default.NonEmptyString(),
        (userPath, projectPath) =>
        {
            var resolver = new TasksFilePathResolver(new PathValidator());
            var pathValidator = new PathValidator();
            
            try
            {
                var resolved = resolver.ResolveTasksFilePathAsync(
                    userPath.Get, 
                    projectPath.Get).Result;
                
                // Если путь разрешен, он должен пройти валидацию
                return pathValidator.IsPathValid(resolved, projectPath.Get);
            }
            catch (InvalidOperationException)
            {
                // Ожидаемое исключение для невалидных путей
                return true;
            }
            catch (FileNotFoundException)
            {
                // Ожидаемое исключение для несуществующих файлов
                return true;
            }
        });
}
```

**Property 3: Изоляция команд от LLM (Requirements 3.6)**
```csharp
[Property]
public Property PromptProcessor_DoesNotSendCommandsToLlm()
{
    return Prop.ForAll(
        GenerateValidCommand(),
        command =>
        {
            var mockLlmService = new Mock<ILlmService>();
            var processor = new PromptProcessor(
                /* dependencies */,
                mockLlmService.Object);
            
            processor.ProcessPromptAsync(1, command.Text).Wait();
            
            // LLM не должен быть вызван для команд управления
            mockLlmService.Verify(
                x => x.SendPromptAsync(It.IsAny<string>(), It.IsAny<List<FunctionDefinition>>()),
                Times.Never);
            
            return true;
        });
}
```

**Property 4: Соответствие состояния кнопок статусу (Requirements 5.1-5.8)**
```javascript
// JavaScript property test (используя fast-check)
fc.assert(
    fc.property(
        fc.constantFrom('running', 'stopped', 'failed', 'completed', 'none'),
        (status) => {
            // Создаем mock DOM
            document.body.innerHTML = `
                <div id="execution-controls">
                    <button id="stop-execution-btn"></button>
                    <button id="resume-execution-btn"></button>
                </div>
            `;
            
            // Вызываем функцию обновления
            updateControlButtons(status);
            
            const stopBtn = document.getElementById('stop-execution-btn');
            const resumeBtn = document.getElementById('resume-execution-btn');
            const controls = document.getElementById('execution-controls');
            
            // Проверяем соответствие
            if (status === 'running') {
                return stopBtn.style.display !== 'none' && 
                       resumeBtn.style.display === 'none';
            } else if (status === 'stopped' || status === 'failed') {
                return stopBtn.style.display === 'none' && 
                       resumeBtn.style.display !== 'none';
            } else {
                return controls.style.display === 'none';
            }
        }
    ),
    { numRuns: 100 }
);
```

**Property 5: Активность polling соответствует статусу (Requirements 7.1, 7.6)**
```javascript
fc.assert(
    fc.property(
        fc.constantFrom('running', 'stopped', 'failed', 'completed', 'none'),
        (status) => {
            // Сброс состояния
            stopPollingExecutionStatus();
            
            // Симуляция получения статуса
            const mockStatus = { status: status };
            updateExecutionUI(mockStatus);
            
            // Проверка состояния polling
            const isPollingActive = executionPollingInterval !== null;
            
            if (status === 'running') {
                return isPollingActive;
            } else {
                return !isPollingActive;
            }
        }
    ),
    { numRuns: 100 }
);
```

### Тестовые сценарии (E2E)

1. **Полный цикл выполнения задач:**
   - Создать диалог с проектом
   - Создать файл tasks.md с несколькими задачами
   - Отправить команду "начни выполнение задач из tasks.md"
   - Проверить появление кнопки "Остановить"
   - Проверить появление индикатора прогресса
   - Дождаться завершения выполнения
   - Проверить скрытие кнопок управления
   - Проверить финальное сообщение "🎉 Все задачи выполнены"

2. **Остановка и возобновление:**
   - Запустить выполнение задач
   - Нажать кнопку "Остановить"
   - Проверить появление кнопки "Возобновить"
   - Проверить сообщение "⏸️ Выполнение остановлено"
   - Нажать кнопку "Возобновить"
   - Проверить возобновление выполнения
   - Проверить сообщение "▶️ Продолжаю выполнение задач..."

3. **Обработка ошибок:**
   - Отправить команду "начни выполнение задач" без файла tasks.md
   - Проверить сообщение "Файл tasks.md не найден"
   - Отправить команду с невалидным путем
   - Проверить сообщение об ошибке валидации
   - Отправить команду "продолжи выполнение" без остановленной сессии
   - Проверить сообщение "Нет остановленной сессии"

4. **Переключение между диалогами:**
   - Создать два диалога
   - Запустить выполнение в первом диалоге
   - Переключиться на второй диалог
   - Проверить остановку polling первого диалога
   - Проверить отсутствие кнопок управления во втором диалоге
   - Вернуться к первому диалогу
   - Проверить возобновление polling
   - Проверить отображение кнопок управления

### Конфигурация тестов

**Backend (.NET):**
```xml
<ItemGroup>
  <PackageReference Include="xunit" Version="2.9.3" />
  <PackageReference Include="FsCheck" Version="2.16.6" />
  <PackageReference Include="FsCheck.Xunit" Version="2.16.6" />
  <PackageReference Include="Moq" Version="4.20.70" />
</ItemGroup>
```

**Frontend (JavaScript):**
```json
{
  "devDependencies": {
    "jest": "^29.0.0",
    "fast-check": "^3.0.0",
    "@testing-library/dom": "^9.0.0"
  }
}
```

**Минимальные требования:**
- Property-based тесты: минимум 100 итераций
- Unit-тесты: покрытие всех публичных методов
- E2E тесты: покрытие основных пользовательских сценариев
- Каждый property-тест должен иметь комментарий с тегом:
  ```csharp
  // Feature: ui-agent-task-executor-integration, Property 1: Распознавание команд на нескольких языках
  ```

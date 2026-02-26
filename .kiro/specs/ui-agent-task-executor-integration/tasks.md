# План реализации: UI Integration for Agent Task Executor

## Обзор

Этот план описывает пошаговую реализацию UI интеграции для агентского режима выполнения задач. Система расширяет существующий веб-интерфейс чата функциональностью управления выполнением через естественные команды, добавляет визуальные индикаторы прогресса и кнопки управления.

## Задачи

- [ ] 1. Создание backend компонентов для распознавания команд
  - [x] 1.1 Создать enum AgentCommandType в Models/RequestModels.cs
    - Добавить значения: StartExecution, StopExecution, ResumeExecution, ShowStatus
    - _Требования: 1.1, 1.3, 1.5, 1.7_
  
  - [x] 1.2 Создать класс CommandRecognizer в Services/CommandRecognizer.cs
    - Реализовать метод TryRecognizeCommand(string prompt, out AgentCommandType commandType, out string? filePath)
    - Добавить словарь CommandPatterns с паттернами команд на русском и английском
    - Реализовать метод ExtractFilePath(string prompt) с регулярными выражениями
    - Поддержать игнорирование регистра символов
    - _Требования: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.10, 11.1, 11.2, 11.3, 11.4_
  
  - [ ]* 1.3 Написать property-тест для распознавания команд
    - **Property 1: Распознавание команд на нескольких языках**
    - **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 11.1, 11.2, 11.3, 11.4**
  
  - [ ]* 1.4 Написать property-тест для извлечения пути
    - **Property 3: Извлечение пути к файлу**
    - **Validates: Requirements 1.10**
  
  - [ ]* 1.5 Написать unit-тесты для CommandRecognizer
    - Тест распознавания "начни выполнение задач из tasks.md"
    - Тест распознавания "execute tasks from tasks.md"
    - Тест распознавания "останови выполнение"
    - Тест распознавания "stop execution"
    - Тест распознавания "продолжи выполнение"
    - Тест распознавания "resume execution"
    - Тест распознавания "покажи статус"
    - Тест распознавания "show status"
    - Тест игнорирования регистра
    - Тест извлечения пути "из файла .kiro/specs/feature/tasks.md"
    - Тест извлечения пути "from file tasks.md"
    - Тест возврата null для команд без пути
    - _Требования: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.10_

- [ ] 2. Создание компонента разрешения путей к файлам
  - [x] 2.1 Создать класс TasksFilePathResolver в Services/TasksFilePathResolver.cs
    - Инжектировать PathValidator
    - Реализовать метод ResolveTasksFilePathAsync(string? userProvidedPath, string projectPath)
    - Обработать случай с указанным пользователем путем (валидация, проверка существования)
    - Обработать случай без указанного пути (поиск tasks.md в корне проекта)
    - Выбрасывать FileNotFoundException с понятными сообщениями
    - Выбрасывать InvalidOperationException для невалидных путей
    - _Требования: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_
  
  - [ ]* 2.2 Написать property-тест для валидации путей
    - **Property 4: Валидация всех путей**
    - **Validates: Requirements 2.7**
  
  - [ ]* 2.3 Написать property-тест для разрешения относительных путей
    - **Property 5: Разрешение относительных путей**
    - **Validates: Requirements 2.6**
  
  - [ ]* 2.4 Написать unit-тесты для TasksFilePathResolver
    - Тест разрешения "tasks.md" → "{projectPath}/tasks.md"
    - Тест разрешения ".kiro/specs/feature/tasks.md"
    - Тест FileNotFoundException если файл не найден в корне
    - Тест FileNotFoundException если файл не найден по указанному пути
    - Тест InvalidOperationException для невалидного пути
    - Тест валидации через PathValidator
    - _Требования: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

- [x] 3. Checkpoint - Убедиться, что распознавание команд и разрешение путей работает
  - Убедиться, что все тесты проходят, задать вопросы пользователю при необходимости.

- [x] 4. Расширение PromptProcessor для обработки команд управления
  - [x] 4.1 Добавить зависимости в PromptProcessor
    - Инжектировать ITaskExecutorService
    - Создать экземпляр CommandRecognizer
    - Создать экземпляр TasksFilePathResolver
    - _Требования: 3.1, 3.2, 3.3, 3.4, 3.5_
  
  - [x] 4.2 Расширить метод ProcessPromptAsync
    - Добавить проверку команды через CommandRecognizer в начале метода
    - Если команда распознана, вызвать ExecuteAgentCommandAsync
    - Если команда не распознана, продолжить обычную обработку через LLM
    - _Требования: 3.1, 3.6_
  
  - [x] 4.3 Реализовать метод ExecuteAgentCommandAsync
    - Получить диалог из базы данных
    - Обработать команду StartExecution (разрешить путь, вызвать TaskExecutorService.ExecuteTasksAsync)
    - Обработать команду StopExecution (вызвать TaskExecutorService.StopExecutionAsync)
    - Обработать команду ResumeExecution (вызвать TaskExecutorService.ResumeExecutionAsync)
    - Обработать команду ShowStatus (вызвать TaskExecutorService.GetExecutionStatusAsync, форматировать ответ)
    - Обработать исключения (FileNotFoundException, InvalidOperationException)
    - Вернуть подтверждающее сообщение при успехе
    - Вернуть сообщение об ошибке при неудаче
    - _Требования: 3.2, 3.3, 3.4, 3.5, 3.7, 3.8, 12.1, 12.2, 12.3_
  
  - [ ]* 4.4 Написать property-тест для проверки каждого сообщения
    - **Property 6: Проверка каждого сообщения**
    - **Validates: Requirements 3.1**
  
  - [ ]* 4.5 Написать property-тест для изоляции команд от LLM
    - **Property 7: Изоляция команд от LLM**
    - **Validates: Requirements 3.6**
  
  - [ ]* 4.6 Написать property-тест для подтверждения успеха
    - **Property 8: Подтверждение успешного выполнения**
    - **Validates: Requirements 3.7**
  
  - [ ]* 4.7 Написать property-тест для сообщений об ошибках
    - **Property 9: Сообщения об ошибках с объяснением**
    - **Validates: Requirements 3.8**
  
  - [ ]* 4.8 Написать unit-тесты для PromptProcessor
    - Тест обработки команды запуска → вызов ExecuteTasksAsync
    - Тест обработки команды остановки → вызов StopExecutionAsync
    - Тест обработки команды возобновления → вызов ResumeExecutionAsync
    - Тест обработки команды статуса → вызов GetExecutionStatusAsync
    - Тест: команды управления НЕ отправляются в LLM
    - Тест: обычные промпты отправляются в LLM
    - Тест возврата подтверждающего сообщения при успехе
    - Тест возврата сообщения об ошибке при FileNotFoundException
    - Тест возврата сообщения об ошибке при InvalidOperationException
    - _Требования: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8_

- [x] 5. Checkpoint - Убедиться, что backend обработка команд работает
  - Убедиться, что все тесты проходят, задать вопросы пользователю при необходимости.

- [x] 6. Расширение frontend для управления выполнением
  - [x] 6.1 Добавить HTML элементы в wwwroot/index.html
    - Добавить div#execution-status-indicator в #message-input-container
    - Добавить div#execution-controls с кнопками stop-execution-btn и resume-execution-btn
    - Разместить элементы перед существующим input и кнопкой отправки
    - _Требования: 5.1, 5.2, 5.5, 6.1_
  
  - [x] 6.2 Добавить CSS стили в wwwroot/index.html
    - Стили для #execution-status-indicator (фон, border, padding, эмодзи)
    - Стили для #execution-controls (flexbox, gap)
    - Стили для .execution-control-btn (цвета, hover, disabled)
    - Стили для #stop-execution-btn (красный)
    - Стили для #resume-execution-btn (зеленый)
    - Стили для сообщений о прогрессе (.message.assistant.progress, .error, .success)
    - _Требования: 5.1, 5.5, 6.1, 6.7, 8.6, 8.7_
  
  - [x] 6.3 Добавить глобальные переменные в wwwroot/app.js
    - let executionPollingInterval = null
    - let currentExecutionStatus = 'none'
    - _Требования: 7.1, 7.6_
  
  - [x] 6.4 Реализовать функцию setupExecutionControlButtons()
    - Получить элементы кнопок
    - Добавить обработчики click для stopExecution и resumeExecution
    - Вызвать из DOMContentLoaded
    - _Требования: 5.3, 5.6_
  
  - [x] 6.5 Реализовать функцию pollExecutionStatus()
    - Проверить наличие currentDialogueId
    - Вызвать GET /api/dialogues/{id}/execution-status
    - Обработать ответ: обновить UI через updateExecutionUI
    - Загрузить новые сообщения через loadMessages
    - Остановить polling если status завершен (completed, failed, none)
    - Обработать ошибки сети (логирование, продолжение polling)
    - Обработать ошибку 404 (остановка polling)
    - _Требования: 7.1, 7.2, 7.3, 7.4, 7.6, 7.7_
  
  - [x] 6.6 Реализовать функции startPollingExecutionStatus() и stopPollingExecutionStatus()
    - startPolling: создать setInterval с интервалом 2000ms, немедленный первый вызов
    - stopPolling: очистить interval, установить null
    - Проверка на уже запущенный polling
    - _Требования: 7.1, 7.5, 7.6_
  
  - [x] 6.7 Реализовать функцию updateExecutionUI(status)
    - Обновить currentExecutionStatus
    - Вызвать updateControlButtons(status.status)
    - Вызвать updateStatusIndicator(status)
    - _Требования: 7.2, 7.3_
  
  - [x] 6.8 Реализовать функцию updateControlButtons(status)
    - Получить элементы кнопок и контейнера
    - Скрыть контейнер если status="none" или "completed"
    - Показать контейнер для других статусов
    - Для status="running": показать "Остановить", скрыть "Возобновить"
    - Для status="stopped"/"failed": скрыть "Остановить", показать "Возобновить"
    - _Требования: 5.1, 5.2, 5.4, 5.5, 5.7, 5.8_
  
  - [x] 6.9 Реализовать функцию updateStatusIndicator(status)
    - Скрыть индикатор если status="none"
    - Показать индикатор для других статусов
    - Установить эмодзи в зависимости от статуса (🔄, ⏸️, ✅, ❌)
    - Установить текстовое описание статуса
    - Добавить прогресс (N/M) если доступен
    - Добавить текущую задачу (первые 50 символов) если доступна
    - _Требования: 6.1, 6.2, 6.3, 6.4, 6.5, 6.7, 6.9_
  
  - [x] 6.10 Реализовать функцию stopExecution()
    - Проверить наличие currentDialogueId
    - Установить disabled и текст "Останавливаю..." на кнопке
    - Установить значение input "останови выполнение"
    - Вызвать sendMessage()
    - Восстановить состояние кнопки в finally
    - _Требования: 5.3_
  
  - [x] 6.11 Реализовать функцию resumeExecution()
    - Проверить наличие currentDialogueId
    - Установить disabled и текст "Возобновляю..." на кнопке
    - Установить значение input "продолжи выполнение"
    - Вызвать sendMessage()
    - Запустить polling через startPollingExecutionStatus()
    - Восстановить состояние кнопки в finally
    - _Требования: 5.6_
  
  - [x] 6.12 Расширить функцию sendMessage()
    - После успешной отправки проверить содержимое на команды запуска
    - Если команда запуска, вызвать startPollingExecutionStatus()
    - _Требования: 7.1_
  
  - [x] 6.13 Расширить функцию selectDialogue(dialogueId)
    - Вызвать stopPollingExecutionStatus() в начале функции
    - После загрузки сообщений и чекпоинтов проверить статус выполнения
    - Вызвать GET /api/dialogues/{id}/execution-status
    - Обновить UI через updateExecutionUI
    - Запустить polling если status="running"
    - _Требования: 7.8, 7.9_
  
  - [ ]* 6.14 Написать property-тест для соответствия кнопок статусу
    - **Property 10: Соответствие состояния кнопок статусу выполнения**
    - **Validates: Requirements 5.1, 5.2, 5.4, 5.5, 5.7, 5.8**
  
  - [ ]* 6.15 Написать property-тест для актуальности индикатора
    - **Property 11: Актуальность индикатора статуса**
    - **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.7, 6.9**
  
  - [ ]* 6.16 Написать property-тест для активности polling
    - **Property 12: Активность polling соответствует статусу**
    - **Validates: Requirements 7.1, 7.6**
  
  - [ ]* 6.17 Написать unit-тесты для frontend функций
    - Тест pollExecutionStatus: вызов API с правильным dialogueId
    - Тест pollExecutionStatus: обновление UI при получении статуса
    - Тест pollExecutionStatus: остановка при status="completed"
    - Тест pollExecutionStatus: остановка при status="failed"
    - Тест pollExecutionStatus: продолжение при status="running"
    - Тест pollExecutionStatus: обработка ошибки 404
    - Тест pollExecutionStatus: обработка ошибки сети
    - Тест updateControlButtons: показ "Остановить" при status="running"
    - Тест updateControlButtons: показ "Возобновить" при status="stopped"
    - Тест updateControlButtons: скрытие кнопок при status="completed"
    - Тест updateStatusIndicator: эмодзи для каждого статуса
    - Тест updateStatusIndicator: отображение прогресса
    - Тест updateStatusIndicator: отображение текущей задачи
    - Тест selectDialogue: остановка polling предыдущего диалога
    - Тест selectDialogue: запуск polling для активного диалога
    - _Требования: 5.1, 5.2, 5.4, 5.5, 5.7, 5.8, 6.1, 6.2, 6.3, 6.4, 6.5, 6.7, 6.9, 7.1, 7.6, 7.7, 7.8, 7.9_

- [x] 7. Checkpoint - Убедиться, что frontend управление работает
  - Убедиться, что все тесты проходят, задать вопросы пользователю при необходимости.

- [x] 8. Регистрация компонентов в DI контейнере
  - [x] 8.1 Зарегистрировать CommandRecognizer в Program.cs
    - Добавить builder.Services.AddSingleton<CommandRecognizer>()
    - _Требования: 3.1_
  
  - [x] 8.2 Зарегистрировать TasksFilePathResolver в Program.cs
    - Добавить builder.Services.AddScoped<TasksFilePathResolver>()
    - _Требования: 2.1_

- [x] 9. Интеграционное тестирование и документация
  - [x] 9.1 Провести E2E тестирование
    - Тест: полный цикл выполнения задач (запуск → прогресс → завершение)
    - Тест: остановка и возобновление выполнения
    - Тест: обработка ошибок (файл не найден, невалидный путь, нет сессии)
    - Тест: переключение между диалогами с активным выполнением
    - Тест: команды на русском языке
    - Тест: команды на английском языке
    - Тест: автоматическое определение пути к tasks.md
    - Тест: указание относительного пути к tasks.md
    - _Требования: 1.1-13.10_
  
  - [x] 9.2 Обновить документацию
    - Добавить раздел "Агентский режим выполнения задач" в README.md
    - Описать команды управления (русские и английские варианты)
    - Добавить примеры использования с скриншотами
    - Описать кнопки управления и индикатор прогресса
    - Добавить раздел "Troubleshooting" для частых ошибок
    - Обновить USAGE_GUIDE.md с примерами команд
    - _Требования: 1.1-13.10_

## Примечания

- Задачи, помеченные `*`, являются опциональными и могут быть пропущены для более быстрого MVP
- Каждая задача ссылается на конкретные требования для отслеживаемости
- Чекпоинты обеспечивают инкрементальную валидацию
- Property-тесты валидируют универсальные свойства корректности
- Unit-тесты валидируют конкретные примеры и граничные случаи
- Минимум 100 итераций для каждого property-теста
- Backend: используйте библиотеку FsCheck для property-based тестирования в .NET
- Frontend: используйте библиотеку fast-check для property-based тестирования в JavaScript
- Каждый property-тест должен иметь комментарий с тегом:
  - Backend: `// Feature: ui-agent-task-executor-integration, Property N: [название]`
  - Frontend: `// Feature: ui-agent-task-executor-integration, Property N: [название]`

# Документ требований: Agent Task Executor

## Введение

Agent Task Executor - это функция для автоматического выполнения задач из файла tasks.md в агентском режиме. Система последовательно обрабатывает незавершенные задачи, отправляя их в LLM через существующий PromptProcessor, отслеживает прогресс выполнения и обновляет статус задач в файле.

## Глоссарий

- **Task_Executor**: Сервис, управляющий процессом выполнения задач из tasks.md
- **Task_Parser**: Компонент для парсинга файла tasks.md и извлечения задач
- **Task_Item**: Элемент задачи с чекбоксом, описанием и опциональными подзадачами
- **Execution_Session**: Сессия выполнения задач, связанная с конкретным диалогом
- **Task_Status**: Статус задачи (незавершена `[ ]`, завершена `[x]`, опциональна `[ ]*`)
- **PromptProcessor**: Существующий сервис для обработки промптов через LLM
- **Dialogue**: Существующая сущность диалога в системе
- **Checkpoint**: Точка сохранения состояния проекта через Git

## Требования

### Требование 1: Парсинг файла tasks.md

**User Story:** Как система, я хочу корректно парсить файл tasks.md, чтобы извлечь все задачи с их метаданными для последующего выполнения.

#### Критерии приемки

1. WHEN система получает путь к файлу tasks.md, THEN THE Task_Parser SHALL прочитать содержимое файла
2. WHEN Task_Parser обрабатывает строку с чекбоксом `- [ ]`, THEN THE Task_Parser SHALL распознать её как незавершенную задачу
3. WHEN Task_Parser обрабатывает строку с чекбоксом `- [x]`, THEN THE Task_Parser SHALL распознать её как завершенную задачу
4. WHEN Task_Parser обрабатывает строку с чекбоксом `- [ ]*`, THEN THE Task_Parser SHALL распознать её как опциональную незавершенную задачу
5. WHEN Task_Parser встречает строку с отступом под задачей, THEN THE Task_Parser SHALL распознать её как подзадачу или описание
6. WHEN Task_Parser встречает строку `_Требования: X.Y, X.Z_`, THEN THE Task_Parser SHALL извлечь список требований для задачи
7. WHEN Task_Parser обрабатывает вложенные подзадачи с чекбоксами, THEN THE Task_Parser SHALL сохранить иерархическую структуру
8. WHEN файл tasks.md содержит секцию "## Задачи", THEN THE Task_Parser SHALL извлекать задачи только из этой секции
9. WHEN Task_Parser завершает парсинг, THEN THE Task_Parser SHALL вернуть список Task_Item с полями: номер, текст, статус, подзадачи, требования

### Требование 2: API endpoint для запуска агентского режима

**User Story:** Как пользователь, я хочу запустить автоматическое выполнение задач через API, чтобы система последовательно обработала все незавершенные задачи.

#### Критерии приемки

1. THE System SHALL предоставить endpoint `POST /api/dialogues/{id}/execute-tasks`
2. WHEN пользователь отправляет запрос на endpoint, THEN THE System SHALL принять параметр `tasksFilePath` (путь к tasks.md)
3. WHEN пользователь отправляет запрос на endpoint, THEN THE System SHALL принять опциональный параметр `skipOptional` (по умолчанию true)
4. WHEN endpoint получает запрос с несуществующим dialogueId, THEN THE System SHALL вернуть ошибку 404 с сообщением "Dialogue not found"
5. WHEN endpoint получает запрос с несуществующим файлом tasks.md, THEN THE System SHALL вернуть ошибку 400 с сообщением "Tasks file not found"
6. WHEN endpoint получает запрос с невалидным путем к файлу, THEN THE System SHALL вернуть ошибку 400 с сообщением "Invalid file path"
7. WHEN endpoint успешно запускает выполнение, THEN THE System SHALL вернуть статус 202 (Accepted) с информацией о сессии выполнения
8. WHEN endpoint запускает выполнение, THEN THE System SHALL создать новую Execution_Session и сохранить её в базе данных

### Требование 3: Последовательное выполнение задач

**User Story:** Как система, я хочу последовательно выполнять незавершенные задачи, чтобы обеспечить корректную обработку зависимостей между задачами.

#### Критерии приемки

1. WHEN Task_Executor начинает выполнение, THEN THE Task_Executor SHALL обработать задачи в порядке их следования в файле
2. WHEN Task_Executor обрабатывает задачу с подзадачами, THEN THE Task_Executor SHALL сначала выполнить все незавершенные подзадачи
3. WHEN Task_Executor встречает опциональную задачу и параметр skipOptional=true, THEN THE Task_Executor SHALL пропустить эту задачу
4. WHEN Task_Executor встречает задачу-чекпоинт (содержит "Checkpoint" или "Контрольная точка"), THEN THE Task_Executor SHALL остановиться и запросить подтверждение пользователя
5. WHEN Task_Executor выполняет задачу, THEN THE Task_Executor SHALL отправить текст задачи и все подзадачи в PromptProcessor
6. WHEN Task_Executor отправляет задачу в PromptProcessor, THEN THE Task_Executor SHALL включить контекст требований из поля "_Требования:_"
7. WHEN Task_Executor получает результат от PromptProcessor, THEN THE Task_Executor SHALL дождаться завершения выполнения
8. WHEN задача выполнена успешно, THEN THE Task_Executor SHALL обновить статус задачи в файле tasks.md (заменить `[ ]` на `[x]`)
9. WHEN все подзадачи выполнены, THEN THE Task_Executor SHALL обновить статус родительской задачи на `[x]`

### Требование 4: Создание Git чекпоинтов

**User Story:** Как система, я хочу создавать Git чекпоинты перед выполнением каждой задачи, чтобы обеспечить возможность отката изменений.

#### Критерии приемки

1. WHEN Task_Executor начинает выполнение задачи, THEN THE Task_Executor SHALL создать Git чекпоинт через IGitService
2. WHEN Task_Executor создает чекпоинт, THEN THE Task_Executor SHALL использовать описание формата "Agent: Task N - [текст задачи]"
3. WHEN создание чекпоинта завершено, THEN THE Task_Executor SHALL сохранить информацию о чекпоинте в базе данных
4. WHEN создание чекпоинта завершено, THEN THE Task_Executor SHALL связать чекпоинт с текущим диалогом
5. IF создание чекпоинта не удалось, THEN THE Task_Executor SHALL записать предупреждение в лог и продолжить выполнение

### Требование 5: Отчетность и прогресс

**User Story:** Как пользователь, я хочу видеть прогресс выполнения задач, чтобы понимать текущее состояние процесса.

#### Критерии приемки

1. WHEN Task_Executor начинает выполнение задачи, THEN THE Task_Executor SHALL сохранить сообщение в диалог с текстом "🤖 Начинаю выполнение задачи N из M: [текст задачи]"
2. WHEN Task_Executor завершает выполнение задачи, THEN THE Task_Executor SHALL сохранить сообщение в диалог с результатом выполнения
3. WHEN Task_Executor завершает выполнение задачи, THEN THE Task_Executor SHALL включить в сообщение время выполнения задачи
4. WHEN Task_Executor завершает все задачи, THEN THE Task_Executor SHALL сохранить итоговое сообщение "✅ Все задачи выполнены успешно. Выполнено N из M задач за [время]"
5. WHEN Task_Executor обрабатывает задачи, THEN THE Task_Executor SHALL обновлять поле progress в Execution_Session (формат: "N/M")
6. WHEN Task_Executor обрабатывает задачи, THEN THE Task_Executor SHALL обновлять поле currentTask в Execution_Session с текстом текущей задачи
7. WHEN пользователь запрашивает статус выполнения через GET /api/dialogues/{id}/execution-status, THEN THE System SHALL вернуть текущий прогресс и статус

### Требование 6: Обработка ошибок

**User Story:** Как система, я хочу корректно обрабатывать ошибки при выполнении задач, чтобы предоставить пользователю информацию о проблемах.

#### Критерии приемки

1. WHEN PromptProcessor возвращает ошибку при выполнении задачи, THEN THE Task_Executor SHALL сохранить сообщение об ошибке в диалог
2. WHEN PromptProcessor возвращает ошибку, THEN THE Task_Executor SHALL остановить выполнение последующих задач
3. WHEN PromptProcessor возвращает ошибку, THEN THE Task_Executor SHALL обновить статус Execution_Session на "failed"
4. WHEN PromptProcessor возвращает ошибку, THEN THE Task_Executor SHALL сохранить текст ошибки в поле errorMessage в Execution_Session
5. WHEN Task_Executor не может обновить файл tasks.md, THEN THE Task_Executor SHALL записать ошибку в лог и продолжить выполнение
6. WHEN Task_Executor встречает таймаут при выполнении задачи (более 5 минут), THEN THE Task_Executor SHALL остановить выполнение и сохранить ошибку таймаута
7. IF выполнение остановлено из-за ошибки, THEN THE System SHALL сохранить информацию о последней успешно выполненной задаче

### Требование 7: Управление состоянием выполнения

**User Story:** Как пользователь, я хочу управлять процессом выполнения задач, чтобы иметь возможность остановить или продолжить выполнение.

#### Критерии приемки

1. THE System SHALL предоставить endpoint `POST /api/dialogues/{id}/stop-execution` для остановки выполнения
2. WHEN пользователь отправляет запрос на остановку, THEN THE Task_Executor SHALL завершить текущую задачу и остановить выполнение
3. WHEN Task_Executor останавливает выполнение, THEN THE Task_Executor SHALL обновить статус Execution_Session на "stopped"
4. WHEN Task_Executor останавливает выполнение, THEN THE Task_Executor SHALL сохранить сообщение в диалог "⏸️ Выполнение остановлено пользователем. Выполнено N из M задач"
5. THE System SHALL предоставить endpoint `POST /api/dialogues/{id}/resume-execution` для продолжения выполнения
6. WHEN пользователь отправляет запрос на продолжение, THEN THE Task_Executor SHALL продолжить выполнение с первой незавершенной задачи
7. WHEN Task_Executor продолжает выполнение, THEN THE Task_Executor SHALL обновить статус Execution_Session на "running"
8. WHEN Task_Executor продолжает выполнение, THEN THE Task_Executor SHALL сохранить сообщение в диалог "▶️ Продолжаю выполнение задач..."

### Требование 8: Модель данных Execution_Session

**User Story:** Как система, я хочу хранить информацию о сессиях выполнения задач, чтобы отслеживать историю и текущее состояние.

#### Критерии приемки

1. THE System SHALL создать таблицу ExecutionSessions в базе данных
2. THE ExecutionSession SHALL содержать поле Id (int, primary key)
3. THE ExecutionSession SHALL содержать поле DialogueId (int, foreign key)
4. THE ExecutionSession SHALL содержать поле TasksFilePath (string, путь к tasks.md)
5. THE ExecutionSession SHALL содержать поле Status (string: "running", "completed", "failed", "stopped")
6. THE ExecutionSession SHALL содержать поле Progress (string, формат "N/M")
7. THE ExecutionSession SHALL содержать поле CurrentTask (string, nullable)
8. THE ExecutionSession SHALL содержать поле ErrorMessage (string, nullable)
9. THE ExecutionSession SHALL содержать поле StartedAt (DateTime)
10. THE ExecutionSession SHALL содержать поле CompletedAt (DateTime, nullable)
11. THE ExecutionSession SHALL содержать поле SkipOptional (bool)
12. WHEN система создает новую сессию, THEN THE System SHALL установить Status="running" и StartedAt=текущее время

### Требование 9: Интеграция с существующей системой

**User Story:** Как разработчик, я хочу интегрировать Task_Executor с существующими сервисами, чтобы использовать имеющуюся функциональность.

#### Критерии приемки

1. THE Task_Executor SHALL использовать IPromptProcessor для выполнения задач
2. THE Task_Executor SHALL использовать IGitService для создания чекпоинтов
3. THE Task_Executor SHALL использовать RefactoringDbContext для работы с базой данных
4. THE Task_Executor SHALL использовать ILogger для логирования операций
5. THE Task_Executor SHALL быть зарегистрирован в DI контейнере как scoped сервис
6. THE Task_Executor SHALL сохранять все сообщения о прогрессе в таблицу Messages с role="assistant"
7. THE Task_Executor SHALL использовать PathValidator для валидации пути к tasks.md

### Требование 10: Формат промпта для LLM

**User Story:** Как система, я хочу формировать структурированные промпты для LLM, чтобы обеспечить качественное выполнение задач.

#### Критерии приемки

1. WHEN Task_Executor формирует промпт для задачи, THEN THE Task_Executor SHALL включить номер задачи
2. WHEN Task_Executor формирует промпт для задачи, THEN THE Task_Executor SHALL включить полный текст задачи
3. WHEN Task_Executor формирует промпт для задачи с подзадачами, THEN THE Task_Executor SHALL включить список всех подзадач
4. WHEN Task_Executor формирует промпт для задачи с требованиями, THEN THE Task_Executor SHALL включить ссылки на требования
5. WHEN Task_Executor формирует промпт, THEN THE Task_Executor SHALL добавить инструкцию "Выполни следующую задачу из плана реализации:"
6. WHEN Task_Executor формирует промпт, THEN THE Task_Executor SHALL добавить инструкцию "После выполнения сообщи о результате"
7. THE Task_Executor SHALL использовать формат промпта на русском языке

### Требование 11: Обновление файла tasks.md

**User Story:** Как система, я хочу обновлять статус задач в файле tasks.md, чтобы отражать прогресс выполнения.

#### Критерии приемки

1. WHEN Task_Executor завершает задачу, THEN THE Task_Executor SHALL найти соответствующую строку в файле tasks.md
2. WHEN Task_Executor обновляет статус задачи, THEN THE Task_Executor SHALL заменить `- [ ]` на `- [x]` в соответствующей строке
3. WHEN Task_Executor обновляет статус подзадачи, THEN THE Task_Executor SHALL заменить `- [ ]` на `- [x]` с сохранением отступа
4. WHEN Task_Executor обновляет файл, THEN THE Task_Executor SHALL сохранить все остальное содержимое файла без изменений
5. WHEN Task_Executor обновляет файл, THEN THE Task_Executor SHALL использовать кодировку UTF-8
6. WHEN Task_Executor не может записать в файл, THEN THE Task_Executor SHALL записать ошибку в лог и продолжить выполнение
7. FOR ALL обновлений файла tasks.md, THE Task_Executor SHALL создать резервную копию перед изменением


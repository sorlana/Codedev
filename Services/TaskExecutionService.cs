using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using CSharpRefactoringAssistant.Data;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

public class TaskExecutionService : ITaskExecutionService
{
    private readonly IConfigurationService _configService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskExecutionService> _logger;
    private readonly IWebSocketManager _webSocketManager;
    private readonly ISerenaService _serenaService;
    private readonly IDeepSeekOrchestratorService _orchestrator;

    public TaskExecutionService(
        IConfigurationService configService,
        IServiceScopeFactory scopeFactory,
        ILogger<TaskExecutionService> logger,
        IWebSocketManager webSocketManager,
        ISerenaService serenaService,
        IDeepSeekOrchestratorService orchestrator)
    {
        _configService = configService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _webSocketManager = webSocketManager;
        _serenaService = serenaService;
        _orchestrator = orchestrator;
    }

    public async Task<TaskExecutionResult> ExecuteTasksAsync(
        int dialogueId, 
        string requirements, 
        string design, 
        string tasks)
    {
        _logger.LogInformation("Начало выполнения задач для диалога {DialogueId}", dialogueId);

        try
        {
            // Получаем конфигурацию
            var config = await _configService.GetConfigurationAsync();
            
            if (!config.UseDeepSeekApi || config.DeepSeek == null || string.IsNullOrEmpty(config.DeepSeek.ApiKey))
            {
                throw new Exception("DeepSeek API не настроен или отключен");
            }

            // Создаем новый scope для работы с БД
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RefactoringDbContext>();
            
            // Получаем путь к проекту
            var dialogue = await dbContext.Dialogues.FindAsync(dialogueId);
            if (dialogue == null)
            {
                throw new Exception($"Диалог {dialogueId} не найден");
            }

            var projectPath = dialogue.ProjectPath;
            if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
            {
                throw new Exception($"Путь к проекту не найден или недоступен: {projectPath}");
            }

            _logger.LogInformation("Путь к проекту: {ProjectPath}", projectPath);

            // Отправляем начальное сообщение о прогрессе
            await _webSocketManager.BroadcastToDialogueAsync(dialogueId, new WebSocketMessage
            {
                Type = "task_execution_progress",
                Payload = new TaskExecutionProgressPayload
                {
                    Current = 0,
                    Total = 0,
                    Message = "Инициализация выполнения задач..."
                }
            });

            // Вызываем DeepSeek API с инструментами
            var result = await ExecuteWithDeepSeekAsync(
                dialogueId,
                projectPath,
                requirements,
                design,
                tasks,
                config.DeepSeek);

            _logger.LogInformation("Выполнение задач завершено успешно");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения задач");
            
            return new TaskExecutionResult
            {
                Success = false,
                Message = $"Ошибка выполнения задач: {ex.Message}"
            };
        }
    }

    private async Task<TaskExecutionResult> ExecuteWithDeepSeekAsync(
        int dialogueId,
        string projectPath,
        string requirements,
        string design,
        string tasks,
        DeepSeekSettings settings)
    {
        var result = new TaskExecutionResult { Success = true };

        // Системный промпт с инструкциями
        var systemPrompt = BuildSystemPrompt(projectPath);
        
        // Пользовательский промпт с контекстом
        var userPrompt = BuildUserPrompt(requirements, design, tasks);

        // Определяем доступные инструменты (функции)
        var tools = BuildToolDefinitions();

        var messages = new List<object>
        {
            new Dictionary<string, object> { ["role"] = "system", ["content"] = systemPrompt },
            new Dictionary<string, object> { ["role"] = "user", ["content"] = userPrompt }
        };

        _logger.LogInformation("Запуск оркестратора для выполнения задач");

        // Используем оркестратор для управления multi-turn диалогом
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

        if (!orchestratorResult.Success)
        {
            result.Success = false;
            result.Message = orchestratorResult.ErrorMessage ?? "Неизвестная ошибка";
            return result;
        }

        result.Message = orchestratorResult.FinalAnswer;
        
        _logger.LogInformation("Оркестратор завершил работу. Выполнено суб-запросов: {SubTurns}", 
            orchestratorResult.SubTurnsExecuted);

        // Генерируем инструкции по запуску
        result.LaunchInstructions = GenerateLaunchInstructions(result.CreatedFiles, projectPath);

        // Отправляем финальное сообщение о завершении
        await _webSocketManager.BroadcastToDialogueAsync(dialogueId, new WebSocketMessage
        {
            Type = "task_execution_progress",
            Payload = new TaskExecutionProgressPayload
            {
                Current = result.CreatedFiles.Count,
                Total = result.CreatedFiles.Count,
                Message = $"Выполнение завершено. Создано файлов: {result.CreatedFiles.Count}, суб-запросов: {orchestratorResult.SubTurnsExecuted}"
            }
        });

        return result;
    }

    private string BuildSystemPrompt(string projectPath)
    {
        return $@"Ты - опытный разработчик-ассистент. Твоя задача - выполнять команды пользователя, используя доступные инструменты.

ПУТЬ К ПРОЕКТУ: {projectPath}

ДОСТУПНЫЕ ИНСТРУМЕНТЫ:

=== РАБОТА С ФАЙЛОВОЙ СИСТЕМОЙ ===
1. create_folder - создать папку
2. create_file - создать новый файл с содержимым (ПЕРЕЗАПИСЫВАЕТ существующий файл!)
3. list_directory - просмотреть содержимое папки
4. read_file - прочитать содержимое файла

=== АНАЛИЗ И РЕДАКТИРОВАНИЕ КОДА (Serena MCP) ===
5. activate_project - активировать проект для анализа (вызови первым при работе с кодом!)
6. find_symbol - найти символ (класс, метод, функцию) по имени
7. find_referencing_symbols - найти все места использования символа
8. replace_symbol_body - заменить тело метода/функции
9. insert_before_symbol - вставить код перед символом

КРИТИЧЕСКИ ВАЖНЫЕ ПРАВИЛА:

1. МНОГОШАГОВОЕ ВЫПОЛНЕНИЕ:
   - Если задача требует нескольких действий, выполняй их ПОСЛЕДОВАТЕЛЬНО
   - Пример: ""Прочитай файл X и добавь строку Y"" = read_file(X) → create_file(X, старое_содержимое + Y)
   - НЕ останавливайся после первого инструмента, если задача не завершена!

2. ЧТЕНИЕ ПЕРЕД ИЗМЕНЕНИЕМ:
   - ВСЕГДА используй read_file перед изменением существующего файла
   - Только после чтения можешь добавить/изменить содержимое

3. СОЗДАНИЕ ФАЙЛОВ:
   - create_file ПЕРЕЗАПИСЫВАЕТ файл полностью
   - Чтобы добавить в конец: read_file → create_file(старое + новое)
   - Чтобы изменить часть: read_file → create_file(измененное)

4. ФИНАЛЬНЫЙ ОТВЕТ:
   - Отвечай пользователю ТОЛЬКО после выполнения ВСЕХ необходимых действий
   - Опиши что именно было сделано

ПРИМЕРЫ ПРАВИЛЬНОГО ВЫПОЛНЕНИЯ:

Задача: ""Прочитай hello.txt и добавь в конец 'Вторая строка'""
Шаг 1: read_file(""hello.txt"") → получаем ""Привет, мир!""
Шаг 2: create_file(""hello.txt"", ""Привет, мир!\nВторая строка"")
Шаг 3: Ответ пользователю: ""Добавлена строка 'Вторая строка' в конец файла hello.txt""

Задача: ""Создай файл test.js с функцией hello""
Шаг 1: create_file(""test.js"", ""function hello() {{ console.log('Hello'); }}"")
Шаг 2: Ответ пользователю: ""Создан файл test.js с функцией hello""

ПОМНИ: Ты можешь вызывать инструменты НЕСКОЛЬКО РАЗ подряд. Не останавливайся после первого вызова!";
    }

    private string BuildUserPrompt(string requirements, string design, string tasks)
    {
        return $@"Выполни следующие задачи, создав все необходимые файлы и папки:

=== ТРЕБОВАНИЯ ===
{requirements}

=== ПРОЕКТИРОВАНИЕ ===
{design}

=== ЗАДАЧИ ДЛЯ ВЫПОЛНЕНИЯ ===
{tasks}

Создай ВСЕ необходимые файлы с ПОЛНЫМ кодом. После завершения напиши краткое сообщение об успешном выполнении и инструкции по запуску проекта.";
    }

    private List<object> BuildToolDefinitions()
    {
        return new List<object>
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "create_folder",
                    description = "Создает папку в проекте. Автоматически создает все родительские папки если их нет.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new
                            {
                                type = "string",
                                description = "Относительный путь к папке от корня проекта (например: 'src/components' или 'tests/unit')"
                            }
                        },
                        required = new[] { "path" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "create_file",
                    description = "Создает файл с содержимым. Автоматически создает папки если их нет.",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new
                            {
                                type = "string",
                                description = "Относительный путь к файлу от корня проекта (например: 'src/index.js' или 'package.json')"
                            },
                            content = new
                            {
                                type = "string",
                                description = "Полное содержимое файла"
                            }
                        },
                        required = new[] { "path", "content" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "list_directory",
                    description = "Просматривает содержимое папки",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new
                            {
                                type = "string",
                                description = "Относительный путь к папке от корня проекта (используй '.' для корня)"
                            }
                        },
                        required = new[] { "path" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "read_file",
                    description = "Читает содержимое файла",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            path = new
                            {
                                type = "string",
                                description = "Относительный путь к файлу от корня проекта"
                            }
                        },
                        required = new[] { "path" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "activate_project",
                    description = "Активирует проект для семантического анализа кода. ОБЯЗАТЕЛЬНО вызови первым перед использованием других инструментов анализа!",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            project_path = new
                            {
                                type = "string",
                                description = "Полный путь к проекту"
                            }
                        },
                        required = new[] { "project_path" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "find_symbol",
                    description = "Находит символ (класс, метод, функцию) по имени в проекте",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            symbol_name = new
                            {
                                type = "string",
                                description = "Имя символа для поиска (например: 'Calculator', 'Add', 'MyClass.MyMethod')"
                            }
                        },
                        required = new[] { "symbol_name" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "find_referencing_symbols",
                    description = "Находит все места использования символа",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            symbol_id = new
                            {
                                type = "string",
                                description = "ID символа из результата find_symbol"
                            }
                        },
                        required = new[] { "symbol_id" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "replace_symbol_body",
                    description = "Заменяет тело метода или функции",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            symbol_id = new
                            {
                                type = "string",
                                description = "ID символа из результата find_symbol"
                            },
                            new_body = new
                            {
                                type = "string",
                                description = "Новое тело метода/функции"
                            }
                        },
                        required = new[] { "symbol_id", "new_body" }
                    }
                }
            },
            new
            {
                type = "function",
                function = new
                {
                    name = "insert_before_symbol",
                    description = "Вставляет код перед символом",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            symbol_id = new
                            {
                                type = "string",
                                description = "ID символа из результата find_symbol"
                            },
                            content = new
                            {
                                type = "string",
                                description = "Код для вставки"
                            }
                        },
                        required = new[] { "symbol_id", "content" }
                    }
                }
            }
        };
    }

    private async Task<string> ExecuteToolAsync(
        int dialogueId,
        string projectPath,
        string functionName,
        string argumentsJson,
        TaskExecutionResult result)
    {
        try
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
            
            if (arguments == null)
            {
                return "Ошибка: не удалось распарсить аргументы";
            }

            switch (functionName)
            {
                case "create_folder":
                    return await CreateFolderAsync(dialogueId, projectPath, arguments, result);
                
                case "create_file":
                    return await CreateFileAsync(dialogueId, projectPath, arguments, result);
                
                case "list_directory":
                    return await ListDirectoryAsync(projectPath, arguments);
                
                case "read_file":
                    return await ReadFileAsync(projectPath, arguments);
                
                case "activate_project":
                    return await ActivateProjectAsync(arguments);
                
                case "find_symbol":
                    return await FindSymbolAsync(arguments);
                
                case "find_referencing_symbols":
                    return await FindReferencingSymbolsAsync(arguments);
                
                case "replace_symbol_body":
                    return await ReplaceSymbolBodyAsync(arguments);
                
                case "insert_before_symbol":
                    return await InsertBeforeSymbolAsync(arguments);
                
                default:
                    return $"Ошибка: неизвестный инструмент '{functionName}'";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения инструмента {FunctionName}", functionName);
            return $"Ошибка выполнения инструмента: {ex.Message}";
        }
    }

    private async Task<string> CreateFolderAsync(
        int dialogueId,
        string projectPath,
        Dictionary<string, JsonElement> arguments,
        TaskExecutionResult result)
    {
        if (!arguments.TryGetValue("path", out var pathElement))
        {
            return "Ошибка: отсутствует параметр 'path'";
        }

        var relativePath = pathElement.GetString();
        if (string.IsNullOrEmpty(relativePath))
        {
            return "Ошибка: путь не может быть пустым";
        }

        var fullPath = Path.Combine(projectPath, relativePath);
        
        _logger.LogInformation("Создание папки: {Path}", fullPath);

        try
        {
            Directory.CreateDirectory(fullPath);
            result.CreatedFolders.Add(relativePath);
            
            // Отправляем прогресс
            await _webSocketManager.BroadcastToDialogueAsync(dialogueId, new WebSocketMessage
            {
                Type = "task_execution_progress",
                Payload = new TaskExecutionProgressPayload
                {
                    Current = result.CreatedFiles.Count + result.CreatedFolders.Count,
                    Total = 0,
                    Message = $"Создана папка: {relativePath}"
                }
            });

            return $"✓ Папка '{relativePath}' успешно создана";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка создания папки {Path}", fullPath);
            return $"Ошибка создания папки: {ex.Message}";
        }
    }

    private async Task<string> CreateFileAsync(
        int dialogueId,
        string projectPath,
        Dictionary<string, JsonElement> arguments,
        TaskExecutionResult result)
    {
        if (!arguments.TryGetValue("path", out var pathElement))
        {
            return "Ошибка: отсутствует параметр 'path'";
        }

        if (!arguments.TryGetValue("content", out var contentElement))
        {
            return "Ошибка: отсутствует параметр 'content'";
        }

        var relativePath = pathElement.GetString();
        var content = contentElement.GetString();

        if (string.IsNullOrEmpty(relativePath))
        {
            return "Ошибка: путь не может быть пустым";
        }

        var fullPath = Path.Combine(projectPath, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        _logger.LogInformation("Создание файла: {Path} ({Length} символов)", fullPath, content?.Length ?? 0);

        try
        {
            // Создаем папку если её нет
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Создаем файл
            await File.WriteAllTextAsync(fullPath, content ?? "", Encoding.UTF8);
            result.CreatedFiles.Add(relativePath);

            // Отправляем прогресс
            await _webSocketManager.BroadcastToDialogueAsync(dialogueId, new WebSocketMessage
            {
                Type = "task_execution_progress",
                Payload = new TaskExecutionProgressPayload
                {
                    Current = result.CreatedFiles.Count,
                    Total = 0,
                    Message = $"Создан файл {result.CreatedFiles.Count}: {relativePath}"
                }
            });

            return $"✓ Файл '{relativePath}' успешно создан ({content?.Length ?? 0} символов)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка создания файла {Path}", fullPath);
            return $"Ошибка создания файла: {ex.Message}";
        }
    }

    private Task<string> ListDirectoryAsync(
        string projectPath,
        Dictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("path", out var pathElement))
        {
            return Task.FromResult("Ошибка: отсутствует параметр 'path'");
        }

        var relativePath = pathElement.GetString();
        if (string.IsNullOrEmpty(relativePath) || relativePath == ".")
        {
            relativePath = "";
        }

        var fullPath = Path.Combine(projectPath, relativePath);

        _logger.LogInformation("Просмотр папки: {Path}", fullPath);

        try
        {
            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult($"Папка '{relativePath}' не существует");
            }

            var directories = Directory.GetDirectories(fullPath)
                .Select(d => "📁 " + Path.GetFileName(d))
                .ToList();

            var files = Directory.GetFiles(fullPath)
                .Select(f => "📄 " + Path.GetFileName(f))
                .ToList();

            var items = directories.Concat(files).ToList();

            if (items.Count == 0)
            {
                return Task.FromResult($"Папка '{relativePath}' пуста");
            }

            return Task.FromResult($"Содержимое '{relativePath}':\n" + string.Join("\n", items));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка просмотра папки {Path}", fullPath);
            return Task.FromResult($"Ошибка просмотра папки: {ex.Message}");
        }
    }

    private string GenerateLaunchInstructions(List<string> createdFiles, string projectPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n📋 ИНСТРУКЦИИ ПО ЗАПУСКУ:");
        sb.AppendLine();

        // Определяем тип проекта по созданным файлам
        var hasIndexHtml = createdFiles.Any(f => f.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
        var hasPackageJson = createdFiles.Any(f => f.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));
        var hasCsProj = createdFiles.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasPyFiles = createdFiles.Any(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase));

        if (hasIndexHtml && !hasPackageJson)
        {
            // Статический HTML проект
            sb.AppendLine("1. Откройте файл index.html в браузере:");
            sb.AppendLine($"   {Path.Combine(projectPath, "index.html")}");
            sb.AppendLine();
            sb.AppendLine("2. Или запустите локальный сервер:");
            sb.AppendLine("   npx serve .");
            sb.AppendLine("   или");
            sb.AppendLine("   python -m http.server 8000");
        }
        else if (hasPackageJson)
        {
            // Node.js проект
            sb.AppendLine("1. Установите зависимости:");
            sb.AppendLine("   npm install");
            sb.AppendLine();
            sb.AppendLine("2. Запустите проект:");
            sb.AppendLine("   npm start");
            sb.AppendLine("   или");
            sb.AppendLine("   npm run dev");
        }
        else if (hasCsProj)
        {
            // C# проект
            sb.AppendLine("1. Восстановите зависимости:");
            sb.AppendLine("   dotnet restore");
            sb.AppendLine();
            sb.AppendLine("2. Запустите проект:");
            sb.AppendLine("   dotnet run");
        }
        else if (hasPyFiles)
        {
            // Python проект
            var mainPy = createdFiles.FirstOrDefault(f => f.EndsWith("main.py", StringComparison.OrdinalIgnoreCase));
            if (mainPy != null)
            {
                sb.AppendLine("1. Запустите проект:");
                sb.AppendLine($"   python {mainPy}");
            }
            else
            {
                sb.AppendLine("1. Запустите Python файлы по необходимости");
            }
        }
        else
        {
            // Неизвестный тип проекта
            sb.AppendLine("Проект создан. Смотрите документацию для инструкций по запуску.");
        }

        sb.AppendLine();
        sb.AppendLine($"📁 Путь к проекту: {projectPath}");
        sb.AppendLine($"📊 Создано файлов: {createdFiles.Count}");

        return sb.ToString();
    }

    // === Методы для работы с Serena MCP ===

    private async Task<string> ReadFileAsync(
        string projectPath,
        Dictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("path", out var pathElement))
        {
            return "Ошибка: отсутствует параметр 'path'";
        }

        var relativePath = pathElement.GetString();
        if (string.IsNullOrEmpty(relativePath))
        {
            return "Ошибка: путь не может быть пустым";
        }

        var fullPath = Path.Combine(projectPath, relativePath);

        _logger.LogInformation("Чтение файла: {Path}", fullPath);

        try
        {
            if (!File.Exists(fullPath))
            {
                return $"Файл '{relativePath}' не существует";
            }

            var content = await _serenaService.ReadFileAsync(fullPath);
            return $"Содержимое файла '{relativePath}':\n\n{content}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка чтения файла {Path}", fullPath);
            return $"Ошибка чтения файла: {ex.Message}";
        }
    }

    private async Task<string> ActivateProjectAsync(Dictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("project_path", out var pathElement))
        {
            return "Ошибка: отсутствует параметр 'project_path'";
        }

        var projectPath = pathElement.GetString();
        if (string.IsNullOrEmpty(projectPath))
        {
            return "Ошибка: путь к проекту не может быть пустым";
        }

        _logger.LogInformation("Активация проекта: {Path}", projectPath);

        try
        {
            var result = await _serenaService.ActivateProjectAsync(projectPath);
            return $"✓ Проект активирован: {result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка активации проекта {Path}", projectPath);
            return $"Ошибка активации проекта: {ex.Message}";
        }
    }

    private async Task<string> FindSymbolAsync(Dictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("symbol_name", out var nameElement))
        {
            return "Ошибка: отсутствует параметр 'symbol_name'";
        }

        var symbolName = nameElement.GetString();
        if (string.IsNullOrEmpty(symbolName))
        {
            return "Ошибка: имя символа не может быть пустым";
        }

        _logger.LogInformation("Поиск символа: {SymbolName}", symbolName);

        try
        {
            var result = await _serenaService.FindSymbolAsync(symbolName);
            return $"Результат поиска символа '{symbolName}':\n\n{result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка поиска символа {SymbolName}", symbolName);
            return $"Ошибка поиска символа: {ex.Message}";
        }
    }

    private async Task<string> FindReferencingSymbolsAsync(Dictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("symbol_id", out var idElement))
        {
            return "Ошибка: отсутствует параметр 'symbol_id'";
        }

        var symbolId = idElement.GetString();
        if (string.IsNullOrEmpty(symbolId))
        {
            return "Ошибка: ID символа не может быть пустым";
        }

        _logger.LogInformation("Поиск использований символа: {SymbolId}", symbolId);

        try
        {
            var result = await _serenaService.FindReferencingSymbolsAsync(symbolId);
            return $"Использования символа:\n\n{result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка поиска использований символа {SymbolId}", symbolId);
            return $"Ошибка поиска использований: {ex.Message}";
        }
    }

    private async Task<string> ReplaceSymbolBodyAsync(Dictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("symbol_id", out var idElement))
        {
            return "Ошибка: отсутствует параметр 'symbol_id'";
        }

        if (!arguments.TryGetValue("new_body", out var bodyElement))
        {
            return "Ошибка: отсутствует параметр 'new_body'";
        }

        var symbolId = idElement.GetString();
        var newBody = bodyElement.GetString();

        if (string.IsNullOrEmpty(symbolId))
        {
            return "Ошибка: ID символа не может быть пустым";
        }

        _logger.LogInformation("Замена тела символа: {SymbolId}", symbolId);

        try
        {
            var result = await _serenaService.ReplaceSymbolBodyAsync(symbolId, newBody ?? "");
            return $"✓ Тело символа заменено: {result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка замены тела символа {SymbolId}", symbolId);
            return $"Ошибка замены тела символа: {ex.Message}";
        }
    }

    private async Task<string> InsertBeforeSymbolAsync(Dictionary<string, JsonElement> arguments)
    {
        if (!arguments.TryGetValue("symbol_id", out var idElement))
        {
            return "Ошибка: отсутствует параметр 'symbol_id'";
        }

        if (!arguments.TryGetValue("content", out var contentElement))
        {
            return "Ошибка: отсутствует параметр 'content'";
        }

        var symbolId = idElement.GetString();
        var content = contentElement.GetString();

        if (string.IsNullOrEmpty(symbolId))
        {
            return "Ошибка: ID символа не может быть пустым";
        }

        _logger.LogInformation("Вставка кода перед символом: {SymbolId}", symbolId);

        try
        {
            var result = await _serenaService.InsertBeforeSymbolAsync(symbolId, content ?? "");
            return $"✓ Код вставлен перед символом: {result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка вставки кода перед символом {SymbolId}", symbolId);
            return $"Ошибка вставки кода: {ex.Message}";
        }
    }
}

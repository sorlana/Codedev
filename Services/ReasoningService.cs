using System.Text;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для использования reasoning модели (DeepSeek-R1) для планирования задач
/// </summary>
public class ReasoningService : IReasoningService
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<ReasoningService> _logger;

    public ReasoningService(
        IConfigurationService configService,
        ILogger<ReasoningService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Создаёт детальный план выполнения задачи используя reasoning модель
    /// </summary>
    public async Task<string> CreateTaskPlanAsync(string taskDescription, string projectPath)
    {
        _logger.LogInformation("Создание плана задачи с помощью reasoning модели");

        var config = await _configService.GetConfigurationAsync();
        
        // Проверяем, настроена ли reasoning модель
        if (config.Provider != "Ollama" || string.IsNullOrEmpty(config.Ollama?.ReasoningModel))
        {
            _logger.LogWarning("Reasoning модель не настроена, используется обычная модель");
            return taskDescription; // Возвращаем исходное описание без изменений
        }

        try
        {
            // Создаём промпт для reasoning модели
            var prompt = BuildReasoningPrompt(taskDescription, projectPath);
            
            // Вызываем reasoning модель через Ollama API
            var plan = await CallReasoningModelAsync(
                config.Ollama.BaseUrl, 
                config.Ollama.ReasoningModel, 
                prompt);
            
            _logger.LogInformation("План задачи успешно создан, длина: {Length} символов", plan.Length);
            
            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании плана задачи");
            // В случае ошибки возвращаем исходное описание
            return taskDescription;
        }
    }

    /// <summary>
    /// Формирует промпт для reasoning модели
    /// </summary>
    private string BuildReasoningPrompt(string taskDescription, string projectPath)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("Ты - архитектор программного обеспечения. Твоя задача - создать ДЕТАЛЬНЫЙ план выполнения задачи разработки.");
        sb.AppendLine();
        sb.AppendLine("ЗАДАЧА:");
        sb.AppendLine(taskDescription);
        sb.AppendLine();
        sb.AppendLine($"⚠️ КРИТИЧЕСКИ ВАЖНО - ПУТЬ К ПРОЕКТУ: {projectPath}");
        sb.AppendLine("⚠️ ВСЕ пути к файлам должны быть ОТНОСИТЕЛЬНО этого пути!");
        sb.AppendLine("⚠️ Все команды должны выполняться В ЭТОЙ директории!");
        sb.AppendLine();
        sb.AppendLine("ТВОЯ ЗАДАЧА:");
        sb.AppendLine("1. Проанализируй задачу и определи, какие файлы нужно создать или изменить");
        sb.AppendLine("2. Определи структуру папок и файлов ОТНОСИТЕЛЬНО пути к проекту");
        sb.AppendLine("3. Для КАЖДОГО файла опиши:");
        sb.AppendLine($"   - Путь к файлу ОТНОСИТЕЛЬНО {projectPath}");
        sb.AppendLine("   - Назначение файла");
        sb.AppendLine("   - Какие классы/интерфейсы/методы должны быть в файле");
        sb.AppendLine("   - Ключевые фрагменты кода (сигнатуры методов, важные свойства)");
        sb.AppendLine("4. Опиши последовательность действий для выполнения задачи");
        sb.AppendLine();
        sb.AppendLine("ФОРМАТ ОТВЕТА:");
        sb.AppendLine("```");
        sb.AppendLine("## Структура проекта");
        sb.AppendLine($"[Опиши структуру папок и файлов относительно {projectPath}]");
        sb.AppendLine();
        sb.AppendLine("## Файлы для создания/изменения");
        sb.AppendLine();
        sb.AppendLine("### Файл: [относительный путь от корня проекта]");
        sb.AppendLine("**Назначение:** [описание]");
        sb.AppendLine("**Содержимое:**");
        sb.AppendLine("[Опиши классы, методы, свойства]");
        sb.AppendLine();
        sb.AppendLine("## Последовательность действий");
        sb.AppendLine("1. [Шаг 1]");
        sb.AppendLine("2. [Шаг 2]");
        sb.AppendLine("...");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("ВАЖНО:");
        sb.AppendLine("- Будь максимально детальным");
        sb.AppendLine("- Укажи все необходимые using директивы");
        sb.AppendLine("- Опиши сигнатуры всех публичных методов");
        sb.AppendLine("- Укажи типы данных и параметры");
        sb.AppendLine("- Опиши зависимости между файлами");
        sb.AppendLine($"- ВСЕ пути указывай относительно {projectPath}");
        
        return sb.ToString();
    }

    /// <summary>
    /// Вызывает reasoning модель через Ollama API
    /// </summary>
    private async Task<string> CallReasoningModelAsync(string baseUrl, string model, string prompt)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5); // Reasoning модели могут работать долго
        
        var requestBody = new
        {
            model = model,
            prompt = prompt,
            stream = false,
            options = new
            {
                temperature = 0.7,
                num_predict = 4096 // Увеличенный лимит для детального плана
            }
        };
        
        var response = await httpClient.PostAsJsonAsync(
            $"{baseUrl}/api/generate", 
            requestBody);
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
        
        return result?.Response ?? string.Empty;
    }
}

/// <summary>
/// Модель ответа от Ollama API
/// </summary>
internal class OllamaGenerateResponse
{
    public string Response { get; set; } = string.Empty;
}

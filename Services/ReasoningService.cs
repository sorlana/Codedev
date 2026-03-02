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
        if (config.Provider != "Ollama")
        {
            _logger.LogWarning("Reasoning модель доступна только для провайдера Ollama");
            return taskDescription;
        }

        if (config.Ollama == null)
        {
            _logger.LogWarning("Настройки Ollama не найдены");
            return taskDescription;
        }

        try
        {
            // Создаём промпт для reasoning модели
            var prompt = BuildReasoningPrompt(taskDescription, projectPath);
            
            string plan;
            
            // Проверяем, использовать ли DeepSeek API
            if (config.UseDeepSeekApi && config.DeepSeek != null && !string.IsNullOrEmpty(config.DeepSeek.ApiKey))
            {
                _logger.LogInformation("Используется DeepSeek API для reasoning");
                plan = await CallDeepSeekApiAsync(
                    config.DeepSeek.ApiKey, 
                    config.DeepSeek.BaseUrl,
                    config.DeepSeek.ReasonerModel,
                    prompt);
            }
            else if (config.Ollama != null && !string.IsNullOrEmpty(config.Ollama.ReasoningModel))
            {
                _logger.LogInformation("Используется локальная Ollama модель {Model} для reasoning", config.Ollama.ReasoningModel);
                plan = await CallOllamaReasoningModelAsync(
                    config.Ollama.BaseUrl, 
                    config.Ollama.ReasoningModel, 
                    prompt);
            }
            else
            {
                _logger.LogWarning("Reasoning модель не настроена");
                return taskDescription;
            }
            
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
    private async Task<string> CallOllamaReasoningModelAsync(string baseUrl, string model, string prompt)
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

    /// <summary>
    /// Вызывает DeepSeek API для reasoning
    /// </summary>
    private async Task<string> CallDeepSeekApiAsync(string apiKey, string baseUrl, string model, string prompt)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        
        var requestBody = new
        {
            model = model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 4096
        };
        
        var response = await httpClient.PostAsJsonAsync(
            $"{baseUrl}/v1/chat/completions", 
            requestBody);
        
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<DeepSeekApiResponse>();
        
        return result?.Choices?[0]?.Message?.Content ?? string.Empty;
    }
}

/// <summary>
/// Модель ответа от Ollama API
/// </summary>
internal class OllamaGenerateResponse
{
    public string Response { get; set; } = string.Empty;
}

/// <summary>
/// Модель ответа от DeepSeek API
/// </summary>
internal class DeepSeekApiResponse
{
    public DeepSeekChoice[]? Choices { get; set; }
}

internal class DeepSeekChoice
{
    public DeepSeekMessage? Message { get; set; }
}

internal class DeepSeekMessage
{
    public string? Content { get; set; }
}

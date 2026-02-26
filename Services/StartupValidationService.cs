using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Сервис для валидации состояния системы при запуске приложения
/// </summary>
public class StartupValidationService : IStartupValidationService
{
    private readonly IConfigurationService _configService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StartupValidationService> _logger;

    public StartupValidationService(
        IConfigurationService configService,
        IHttpClientFactory httpClientFactory,
        ILogger<StartupValidationService> logger)
    {
        _configService = configService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Проверяет подключение к настроенной LLM модели
    /// </summary>
    /// <returns>Результат валидации с информацией о статусе подключения</returns>
    public async Task<ModelConnectionResult> ValidateModelConnectionAsync()
    {
        try
        {
            var config = await _configService.GetConfigurationAsync();
            
            // Проверяем только подключения к Ollama (OpenAI работает через облако)
            if (!config.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Пропуск проверки подключения для провайдера {Provider}", config.Provider);
                return new ModelConnectionResult { IsConnected = true };
            }

            if (config.Ollama == null || string.IsNullOrWhiteSpace(config.Ollama.Model))
            {
                _logger.LogWarning("Модель Ollama не настроена");
                return new ModelConnectionResult
                {
                    IsConnected = false,
                    ErrorMessage = "Модель не настроена"
                };
            }

            var modelName = config.Ollama.Model;
            var baseUrl = config.Ollama.BaseUrl;

            _logger.LogInformation("Проверка подключения к модели Ollama: {ModelName} на {BaseUrl}", modelName, baseUrl);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            var response = await httpClient.GetAsync($"{baseUrl}/api/tags");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Не удалось подключиться к Ollama. Статус: {StatusCode}", response.StatusCode);
                return new ModelConnectionResult
                {
                    IsConnected = false,
                    ModelName = modelName,
                    ErrorMessage = $"Нет подключения к модели {modelName}, запустите модель в Ollama"
                };
            }

            _logger.LogInformation("Успешное подключение к модели Ollama: {ModelName}", modelName);
            return new ModelConnectionResult
            {
                IsConnected = true,
                ModelName = modelName
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Таймаут при подключении к Ollama");
            var config = await _configService.GetConfigurationAsync();
            var modelName = config.Ollama?.Model ?? "неизвестная модель";
            
            return new ModelConnectionResult
            {
                IsConnected = false,
                ModelName = modelName,
                ErrorMessage = $"Нет подключения к модели {modelName}, запустите модель в Ollama"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ошибка HTTP при подключении к Ollama");
            var config = await _configService.GetConfigurationAsync();
            var modelName = config.Ollama?.Model ?? "неизвестная модель";
            
            return new ModelConnectionResult
            {
                IsConnected = false,
                ModelName = modelName,
                ErrorMessage = $"Нет подключения к модели {modelName}, запустите модель в Ollama"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при проверке подключения к Ollama");
            var config = await _configService.GetConfigurationAsync();
            var modelName = config.Ollama?.Model ?? "неизвестная модель";
            
            return new ModelConnectionResult
            {
                IsConnected = false,
                ModelName = modelName,
                ErrorMessage = $"Нет подключения к модели {modelName}, запустите модель в Ollama"
            };
        }
    }
}

using CSharpRefactoringAssistant.Models;
using CSharpRefactoringAssistant.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace CSharpRefactoringAssistant.Tests;

/// <summary>
/// Ручные тесты для StartupValidationService
/// </summary>
public class StartupValidationServiceTests
{
    /// <summary>
    /// Тест проверки подключения к Ollama (когда Ollama запущен)
    /// </summary>
    public static async Task TestValidateOllamaConnection_WhenRunning()
    {
        Console.WriteLine("\n=== Тест: Проверка подключения к запущенному Ollama ===");
        
        var service = CreateStartupValidationService("Ollama", "http://localhost:11434", "llama2");
        
        var result = await service.ValidateModelConnectionAsync();
        
        Console.WriteLine($"IsConnected: {result.IsConnected}");
        Console.WriteLine($"ModelName: {result.ModelName}");
        Console.WriteLine($"ErrorMessage: {result.ErrorMessage}");
        
        if (result.IsConnected)
        {
            Console.WriteLine("✓ Успешное подключение к Ollama");
        }
        else
        {
            Console.WriteLine("⚠ Ollama не запущен или недоступен (это ожидаемо, если Ollama не установлен)");
            if (result.ErrorMessage != null && result.ErrorMessage.Contains("Нет подключения к модели"))
            {
                Console.WriteLine("✓ Сообщение об ошибке корректно");
            }
            else
            {
                throw new Exception($"Ожидалось сообщение об ошибке с текстом 'Нет подключения к модели', получено: {result.ErrorMessage}");
            }
        }
    }

    /// <summary>
    /// Тест проверки подключения к несуществующему Ollama
    /// </summary>
    public static async Task TestValidateOllamaConnection_WhenNotRunning()
    {
        Console.WriteLine("\n=== Тест: Проверка подключения к несуществующему Ollama ===");
        
        // Используем несуществующий порт
        var service = CreateStartupValidationService("Ollama", "http://localhost:99999", "llama2");
        
        var result = await service.ValidateModelConnectionAsync();
        
        Console.WriteLine($"IsConnected: {result.IsConnected}");
        Console.WriteLine($"ModelName: {result.ModelName}");
        Console.WriteLine($"ErrorMessage: {result.ErrorMessage}");
        
        if (result.IsConnected)
        {
            throw new Exception("Ожидалось, что подключение не удастся");
        }
        
        if (result.ModelName != "llama2")
        {
            throw new Exception($"Ожидалось ModelName='llama2', получено: {result.ModelName}");
        }
        
        if (result.ErrorMessage == null || !result.ErrorMessage.Contains("Нет подключения к модели llama2"))
        {
            throw new Exception($"Ожидалось сообщение об ошибке с текстом 'Нет подключения к модели llama2', получено: {result.ErrorMessage}");
        }
        
        Console.WriteLine("✓ Ошибка подключения обработана корректно");
    }

    /// <summary>
    /// Тест проверки подключения с таймаутом
    /// </summary>
    public static async Task TestValidateOllamaConnection_Timeout()
    {
        Console.WriteLine("\n=== Тест: Проверка таймаута подключения ===");
        
        // Используем несуществующий хост для симуляции таймаута
        var service = CreateStartupValidationService("Ollama", "http://192.0.2.1:11434", "llama2");
        
        var startTime = DateTime.UtcNow;
        var result = await service.ValidateModelConnectionAsync();
        var elapsed = DateTime.UtcNow - startTime;
        
        Console.WriteLine($"Время выполнения: {elapsed.TotalSeconds:F2} секунд");
        Console.WriteLine($"IsConnected: {result.IsConnected}");
        Console.WriteLine($"ErrorMessage: {result.ErrorMessage}");
        
        if (result.IsConnected)
        {
            throw new Exception("Ожидалось, что подключение не удастся");
        }
        
        if (elapsed.TotalSeconds > 10)
        {
            throw new Exception($"Таймаут должен быть около 5 секунд, но прошло {elapsed.TotalSeconds:F2} секунд");
        }
        
        Console.WriteLine("✓ Таймаут работает корректно (< 10 секунд)");
    }

    /// <summary>
    /// Тест пропуска проверки для OpenAI провайдера
    /// </summary>
    public static async Task TestValidateOpenAIConnection_Skipped()
    {
        Console.WriteLine("\n=== Тест: Пропуск проверки для OpenAI ===");
        
        var service = CreateStartupValidationService("OpenAI", "https://api.openai.com/v1", "gpt-4");
        
        var result = await service.ValidateModelConnectionAsync();
        
        Console.WriteLine($"IsConnected: {result.IsConnected}");
        Console.WriteLine($"ModelName: {result.ModelName}");
        Console.WriteLine($"ErrorMessage: {result.ErrorMessage}");
        
        if (!result.IsConnected)
        {
            throw new Exception("Ожидалось, что проверка будет пропущена для OpenAI");
        }
        
        Console.WriteLine("✓ Проверка корректно пропущена для OpenAI провайдера");
    }

    /// <summary>
    /// Тест с отсутствующей конфигурацией модели
    /// </summary>
    public static async Task TestValidateOllamaConnection_NoModel()
    {
        Console.WriteLine("\n=== Тест: Проверка с отсутствующей моделью ===");
        
        var service = CreateStartupValidationService("Ollama", "http://localhost:11434", "");
        
        var result = await service.ValidateModelConnectionAsync();
        
        Console.WriteLine($"IsConnected: {result.IsConnected}");
        Console.WriteLine($"ErrorMessage: {result.ErrorMessage}");
        
        if (result.IsConnected)
        {
            throw new Exception("Ожидалось, что проверка не пройдет без модели");
        }
        
        if (result.ErrorMessage != "Модель не настроена")
        {
            throw new Exception($"Ожидалось сообщение 'Модель не настроена', получено: {result.ErrorMessage}");
        }
        
        Console.WriteLine("✓ Отсутствие модели обработано корректно");
    }

    /// <summary>
    /// Тест логирования результатов проверки
    /// </summary>
    public static async Task TestLogging()
    {
        Console.WriteLine("\n=== Тест: Проверка логирования ===");
        
        var logMessages = new List<string>();
        var service = CreateStartupValidationServiceWithLogging("Ollama", "http://localhost:99999", "test-model", logMessages);
        
        await service.ValidateModelConnectionAsync();
        
        Console.WriteLine($"Записано {logMessages.Count} лог-сообщений:");
        foreach (var msg in logMessages)
        {
            Console.WriteLine($"  - {msg}");
        }
        
        // Проверяем, что есть логирование
        var hasInfoLog = logMessages.Any(m => m.Contains("Проверка подключения"));
        var hasErrorLog = logMessages.Any(m => 
            m.Contains("Не удалось подключиться") || 
            m.Contains("Таймаут") || 
            m.Contains("Ошибка HTTP") ||
            m.Contains("Неожиданная ошибка"));
        
        if (!hasInfoLog)
        {
            throw new Exception("Ожидалось информационное сообщение о начале проверки");
        }
        
        if (!hasErrorLog)
        {
            throw new Exception("Ожидалось сообщение об ошибке подключения");
        }
        
        Console.WriteLine("✓ Логирование работает корректно");
    }

    private static StartupValidationService CreateStartupValidationService(
        string provider, 
        string baseUrl, 
        string model)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = provider
        };

        if (provider == "Ollama")
        {
            configData["Llm:Ollama:BaseUrl"] = baseUrl;
            configData["Llm:Ollama:Model"] = model;
        }
        else if (provider == "OpenAI")
        {
            configData["Llm:OpenAI:BaseUrl"] = baseUrl;
            configData["Llm:OpenAI:Model"] = model;
            configData["Llm:OpenAI:ApiKey"] = "test-key";
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var configLogger = loggerFactory.CreateLogger<ConfigurationService>();
        var validationLogger = loggerFactory.CreateLogger<StartupValidationService>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var configService = new ConfigurationService(configuration, configLogger, environment);
        var httpClientFactory = new MockHttpClientFactory();

        return new StartupValidationService(configService, httpClientFactory, validationLogger);
    }

    private static StartupValidationService CreateStartupValidationServiceWithLogging(
        string provider,
        string baseUrl,
        string model,
        List<string> logMessages)
    {
        var configData = new Dictionary<string, string?>
        {
            ["Llm:Provider"] = provider,
            ["Llm:Ollama:BaseUrl"] = baseUrl,
            ["Llm:Ollama:Model"] = model
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestLoggerProvider(logMessages));
        });

        var configLogger = loggerFactory.CreateLogger<ConfigurationService>();
        var validationLogger = loggerFactory.CreateLogger<StartupValidationService>();

        var environment = new MockWebHostEnvironment
        {
            ContentRootPath = Directory.GetCurrentDirectory()
        };

        var configService = new ConfigurationService(configuration, configLogger, environment);
        var httpClientFactory = new MockHttpClientFactory();

        return new StartupValidationService(configService, httpClientFactory, validationLogger);
    }

    public static async Task Main(string[] args)
    {
        try
        {
            await TestValidateOllamaConnection_WhenRunning();
            await TestValidateOllamaConnection_WhenNotRunning();
            await TestValidateOllamaConnection_Timeout();
            await TestValidateOpenAIConnection_Skipped();
            await TestValidateOllamaConnection_NoModel();
            await TestLogging();
            
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("✓✓✓ ВСЕ ТЕСТЫ ПРОЙДЕНЫ! ✓✓✓");
            Console.WriteLine(new string('=', 50));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Тест не пройден: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}

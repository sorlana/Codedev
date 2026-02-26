namespace CSharpRefactoringAssistant.Services;

public class LlmServiceFactory : ILlmServiceFactory
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiLlmService> _openAiLogger;
    private readonly ILogger<OllamaLlmService> _ollamaLogger;

    public LlmServiceFactory(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiLlmService> openAiLogger,
        ILogger<OllamaLlmService> ollamaLogger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _openAiLogger = openAiLogger;
        _ollamaLogger = ollamaLogger;
    }

    public ILlmService CreateLlmService()
    {
        var provider = _configuration["Llm:Provider"];

        return provider?.ToLower() switch
        {
            "openai" => CreateOpenAiService(),
            "ollama" => CreateOllamaService(),
            _ => CreateOpenAiService() // Default fallback
        };
    }

    private ILlmService CreateOpenAiService()
    {
        var httpClient = _httpClientFactory.CreateClient();
        return new OpenAiLlmService(httpClient, _configuration, _openAiLogger);
    }

    private ILlmService CreateOllamaService()
    {
        var httpClient = _httpClientFactory.CreateClient();
        return new OllamaLlmService(httpClient, _configuration, _ollamaLogger);
    }
}

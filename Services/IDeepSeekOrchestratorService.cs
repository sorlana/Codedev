namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Интерфейс сервиса-оркестратора для управления multi-turn tool calling с DeepSeek API
/// </summary>
public interface IDeepSeekOrchestratorService
{
    /// <summary>
    /// Выполняет один "раунд" (turn) диалога с моделью, обрабатывая все суб-запросы
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="messages">История сообщений</param>
    /// <param name="tools">Доступные инструменты</param>
    /// <param name="onToolCall">Callback для выполнения инструмента</param>
    /// <param name="maxSubTurns">Максимальное количество суб-запросов (защита от зацикливания)</param>
    /// <returns>Финальный ответ модели</returns>
    Task<OrchestratorResult> ExecuteTurnAsync(
        int dialogueId,
        List<object> messages,
        List<object> tools,
        Func<string, string, Task<string>> onToolCall,
        int maxSubTurns = 15);
}

/// <summary>
/// Результат выполнения раунда оркестратора
/// </summary>
public class OrchestratorResult
{
    /// <summary>
    /// Успешность выполнения
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Финальный ответ модели
    /// </summary>
    public string FinalAnswer { get; set; } = string.Empty;
    
    /// <summary>
    /// Количество выполненных суб-запросов
    /// </summary>
    public int SubTurnsExecuted { get; set; }
    
    /// <summary>
    /// Сообщение об ошибке (если есть)
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Обновленная история сообщений
    /// </summary>
    public List<object> UpdatedMessages { get; set; } = new();
}

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Интерфейс для обработки потоковой передачи ответов от LLM через WebSocket
/// </summary>
public interface IStreamingService
{
    /// <summary>
    /// Обрабатывает промпт с потоковой передачей фрагментов ответа через WebSocket
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="prompt">Текст промпта от пользователя</param>
    /// <param name="connectionId">Уникальный идентификатор WebSocket соединения</param>
    /// <param name="cancellationToken">Токен отмены для прерывания генерации</param>
    /// <returns>Полный текст сгенерированного ответа</returns>
    Task<string> ProcessPromptWithStreamingAsync(
        int dialogueId,
        string prompt,
        string connectionId,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Отправляет готовый ответ в чат через streaming
    /// </summary>
    /// <param name="dialogueId">ID диалога</param>
    /// <param name="message">Текст сообщения</param>
    Task StreamResponseAsync(int dialogueId, string message);
    
    /// <summary>
    /// Отменяет текущую генерацию ответа для указанного соединения
    /// </summary>
    /// <param name="connectionId">Уникальный идентификатор WebSocket соединения</param>
    Task CancelGenerationAsync(string connectionId);
}

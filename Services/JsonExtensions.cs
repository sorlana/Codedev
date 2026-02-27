using System.Text.Json;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Вспомогательные методы для работы с JSON
/// </summary>
internal static class JsonHelper
{
    /// <summary>
    /// Безопасная десериализация JSON строки
    /// </summary>
    public static bool TryDeserialize(string json, out JsonElement element)
    {
        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(json);
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }
}

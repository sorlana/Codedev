using System.Text;
using System.Text.RegularExpressions;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Services;

/// <summary>
/// Компонент для парсинга файлов tasks.md и извлечения задач
/// </summary>
public class TaskParser
{
    /// <summary>
    /// Парсит файл tasks.md и извлекает задачи
    /// </summary>
    /// <param name="filePath">Путь к файлу tasks.md</param>
    /// <returns>Список задач из файла</returns>
    public List<TaskItem> ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        return ExtractTasks(content);
    }
    
    /// <summary>
    /// Извлекает задачи из содержимого файла
    /// </summary>
    /// <param name="content">Содержимое файла tasks.md</param>
    /// <returns>Список извлеченных задач</returns>
    private List<TaskItem> ExtractTasks(string content)
    {
        var tasks = new List<TaskItem>();
        var lines = content.Split('\n');
        var inTasksSection = false;
        TaskItem? currentParentTask = null;
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            
            // Ищем секцию "## Задачи"
            if (line.Trim().StartsWith("## Задачи"))
            {
                inTasksSection = true;
                continue;
            }
            
            // Выходим из секции при встрече следующего заголовка
            if (inTasksSection && line.Trim().StartsWith("##"))
            {
                break;
            }
            
            if (!inTasksSection) continue;
            
            // Парсим задачи с чекбоксами
            var taskMatch = Regex.Match(line, @"^(\s*)- \[([ x])\](\*)?\s+(.+)$");
            if (taskMatch.Success)
            {
                var indent = taskMatch.Groups[1].Value.Length;
                var isCompleted = taskMatch.Groups[2].Value == "x";
                var isOptional = taskMatch.Groups[3].Success;
                var text = taskMatch.Groups[4].Value.Trim();
                
                var task = new TaskItem
                {
                    LineNumber = i,
                    IndentLevel = indent / 2, // 2 пробела = 1 уровень
                    IsCompleted = isCompleted,
                    IsOptional = isOptional,
                    Text = text,
                    SubTasks = new List<TaskItem>()
                };
                
                // Извлекаем требования из следующих строк
                task.Requirements = ExtractRequirements(lines, i + 1);
                
                // Определяем иерархию
                if (task.IndentLevel == 0)
                {
                    tasks.Add(task);
                    currentParentTask = task;
                }
                else if (currentParentTask != null)
                {
                    currentParentTask.SubTasks.Add(task);
                }
            }
        }
        
        return tasks;
    }
    
    /// <summary>
    /// Извлекает требования из строк после задачи
    /// </summary>
    /// <param name="lines">Массив строк файла</param>
    /// <param name="startIndex">Индекс начала поиска</param>
    /// <returns>Список требований</returns>
    private List<string> ExtractRequirements(string[] lines, int startIndex)
    {
        var requirements = new List<string>();
        
        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // Прекращаем поиск при встрече новой задачи
            if (line.StartsWith("- [")) break;
            
            // Ищем строку с требованиями
            var reqMatch = Regex.Match(line, @"_Требования:\s*(.+)_");
            if (reqMatch.Success)
            {
                var reqText = reqMatch.Groups[1].Value;
                requirements.AddRange(reqText.Split(',').Select(r => r.Trim()));
                break;
            }
        }
        
        return requirements;
    }
}

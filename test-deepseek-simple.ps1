# Тест DeepSeek-R1 с tool calling
$body = @{
    model = "deepseek-r1:7b"
    messages = @(
        @{
            role = "user"
            content = "Создай файл test.txt"
        }
    )
    stream = $false
    tools = @(
        @{
            type = "function"
            function = @{
                name = "execute_shell_command"
                description = "Execute a shell command"
                parameters = @{
                    type = "object"
                    properties = @{
                        command = @{
                            type = "string"
                            description = "The command to execute"
                        }
                    }
                    required = @("command")
                }
            }
        }
    )
} | ConvertTo-Json -Depth 10

Write-Host "Sending request to Ollama..."
Write-Host "Body: $body"

try {
    $response = Invoke-RestMethod -Uri 'http://localhost:11434/api/chat' -Method Post -Body $body -ContentType 'application/json; charset=utf-8'
    Write-Host "`nResponse:"
    $response | ConvertTo-Json -Depth 10
} catch {
    Write-Host "`nError:"
    Write-Host $_.Exception.Message
    if ($_.ErrorDetails) {
        Write-Host $_.ErrorDetails.Message
    }
}

# Тест оркестратора DeepSeek
$baseUrl = "http://localhost:5111"

Write-Host "=== Тест оркестратора DeepSeek ===" -ForegroundColor Cyan

# 1. Создаем диалог
Write-Host "`n1. Создание диалога..." -ForegroundColor Yellow
$createDialogueBody = @{
    name = "Тест оркестратора"
    projectPath = (Get-Location).Path
} | ConvertTo-Json

$dialogue = Invoke-RestMethod -Uri "$baseUrl/api/dialogues" -Method Post -Body $createDialogueBody -ContentType "application/json"
$dialogueId = $dialogue.id
Write-Host "Диалог создан с ID: $dialogueId" -ForegroundColor Green

# 2. Отправляем команду
Write-Host "`n2. Отправка команды: 'Прочитай файл hello.txt и добавь в конец строку 'Это вторая строка''" -ForegroundColor Yellow
$sendMessageBody = @{
    prompt = "Прочитай файл hello.txt и добавь в конец строку 'Это вторая строка'"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/dialogues/$dialogueId/messages" -Method Post -Body $sendMessageBody -ContentType "application/json"
    Write-Host "Ответ получен:" -ForegroundColor Green
    Write-Host $response.content -ForegroundColor White
} catch {
    Write-Host "Ошибка: $_" -ForegroundColor Red
}

# 3. Проверяем содержимое файла
Write-Host "`n3. Проверка содержимого файла hello.txt:" -ForegroundColor Yellow
$content = Get-Content hello.txt -Raw
Write-Host $content -ForegroundColor White

# 4. Проверяем историю сообщений
Write-Host "`n4. История сообщений:" -ForegroundColor Yellow
$messages = Invoke-RestMethod -Uri "$baseUrl/api/dialogues/$dialogueId/messages" -Method Get
foreach ($msg in $messages) {
    Write-Host "[$($msg.role)]: $($msg.content.Substring(0, [Math]::Min(100, $msg.content.Length)))..." -ForegroundColor Cyan
}

Write-Host "`n=== Тест завершен ===" -ForegroundColor Cyan

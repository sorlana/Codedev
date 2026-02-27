# Скрипт для ручного тестирования WebSocket функциональности

Write-Host "=== Тестирование WebSocket функциональности ===" -ForegroundColor Cyan
Write-Host ""

# Запускаем приложение в фоновом режиме
Write-Host "Запуск приложения..." -ForegroundColor Yellow
$app = Start-Process -FilePath "dotnet" -ArgumentList "run" -PassThru -NoNewWindow
Start-Sleep -Seconds 8

Write-Host "Приложение запущено (PID: $($app.Id))" -ForegroundColor Green
Write-Host ""

try {
    # Запускаем тесты
    Write-Host "Запуск тестов WebSocket инфраструктуры..." -ForegroundColor Yellow
    Write-Host ""
    
    # Используем отдельный процесс dotnet для запуска тестов
    $testProcess = Start-Process -FilePath "dotnet" -ArgumentList "build" -Wait -PassThru -NoNewWindow
    
    if ($testProcess.ExitCode -eq 0) {
        Write-Host "Сборка успешна, запускаем тесты..." -ForegroundColor Green
        
        # Запускаем тесты через рефлексию
        $dllPath = "bin\Debug\net10.0\CSharpRefactoringAssistant.dll"
        dotnet $dllPath test-websocket
    }
}
finally {
    # Останавливаем приложение
    Write-Host ""
    Write-Host "Остановка приложения..." -ForegroundColor Yellow
    Stop-Process -Id $app.Id -Force
    Write-Host "Приложение остановлено" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Тестирование завершено ===" -ForegroundColor Cyan

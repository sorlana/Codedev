# Скрипт для исправления зависшего выполнения задач

Write-Host "=== ИСПРАВЛЕНИЕ ЗАВИСШЕГО ВЫПОЛНЕНИЯ ЗАДАЧ ===" -ForegroundColor Cyan
Write-Host ""

# Шаг 1: Остановка сервера
Write-Host "Шаг 1: Остановка сервера..." -ForegroundColor Yellow
$processes = Get-Process | Where-Object {$_.ProcessName -like "*CSharpRefactoringAssistant*"}
if ($processes) {
    foreach ($proc in $processes) {
        Write-Host "Останавливаем процесс $($proc.ProcessName) (ID: $($proc.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $proc.Id -Force
        Start-Sleep -Seconds 2
    }
    Write-Host "Сервер остановлен" -ForegroundColor Green
} else {
    Write-Host "Сервер не запущен" -ForegroundColor Gray
}

Write-Host ""

# Шаг 2: Удаление файлов базы данных
Write-Host "Шаг 2: Очистка базы данных..." -ForegroundColor Yellow

$dbFiles = @("refactoring.db", "refactoring.db-shm", "refactoring.db-wal")
foreach ($file in $dbFiles) {
    if (Test-Path $file) {
        Write-Host "Удаляем $file..." -ForegroundColor Gray
        Remove-Item $file -Force
    }
}

Write-Host "База данных очищена" -ForegroundColor Green
Write-Host ""

# Шаг 3: Запуск сервера
Write-Host "Шаг 3: Запуск сервера..." -ForegroundColor Yellow
Write-Host "Выполните команду: dotnet run" -ForegroundColor Cyan
Write-Host ""
Write-Host "После запуска сервера:" -ForegroundColor Cyan
Write-Host "1. Откройте http://localhost:5000" -ForegroundColor White
Write-Host "2. Создайте новый диалог" -ForegroundColor White
Write-Host "3. Попробуйте выполнить задачу снова" -ForegroundColor White
Write-Host ""
Write-Host "=== ГОТОВО ===" -ForegroundColor Green

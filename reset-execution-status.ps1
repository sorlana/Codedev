# Скрипт для сброса статуса выполнения задач в базе данных

$dbPath = "refactoring.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "База данных не найдена: $dbPath" -ForegroundColor Red
    exit 1
}

Write-Host "Останавливаем сервер (если запущен)..." -ForegroundColor Yellow
Write-Host "Нажмите Ctrl+C в окне сервера, если он запущен" -ForegroundColor Yellow
Write-Host ""
Write-Host "Нажмите Enter когда сервер остановлен..." -ForegroundColor Cyan
Read-Host

# SQL команда для обновления статуса
$sql = @"
UPDATE ExecutionSessions 
SET Status='stopped', CompletedAt=datetime('now') 
WHERE Status='running';
"@

# Сохраняем SQL в временный файл
$sqlFile = "temp_reset.sql"
$sql | Out-File -FilePath $sqlFile -Encoding ASCII

Write-Host "Выполняем SQL команду..." -ForegroundColor Yellow

# Пытаемся найти sqlite3
$sqlite3Paths = @(
    "sqlite3.exe",
    "C:\Program Files\SQLite\sqlite3.exe",
    "C:\sqlite\sqlite3.exe"
)

$sqlite3 = $null
foreach ($path in $sqlite3Paths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) {
        $sqlite3 = $path
        break
    }
}

if ($null -eq $sqlite3) {
    Write-Host "sqlite3 не найден. Используем альтернативный метод..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Выполните следующую команду вручную:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "UPDATE ExecutionSessions SET Status='stopped', CompletedAt=datetime('now') WHERE Status='running';" -ForegroundColor Green
    Write-Host ""
    Write-Host "Или удалите файл refactoring.db и перезапустите сервер" -ForegroundColor Yellow
} else {
    # Выполняем SQL
    & $sqlite3 $dbPath ".read $sqlFile"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Статус успешно сброшен!" -ForegroundColor Green
    } else {
        Write-Host "Ошибка при выполнении SQL" -ForegroundColor Red
    }
    
    # Удаляем временный файл
    Remove-Item $sqlFile -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Теперь можно запустить сервер: dotnet run" -ForegroundColor Cyan

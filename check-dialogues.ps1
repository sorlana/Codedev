# Скрипт для проверки диалогов в базе данных
Write-Host "=== Проверка диалогов в базе данных ===" -ForegroundColor Cyan
Write-Host ""

# Проверка наличия sqlite3
$sqliteExists = Get-Command sqlite3 -ErrorAction SilentlyContinue
if (-not $sqliteExists) {
    Write-Host "❌ sqlite3 не найден. Установите SQLite для использования этого скрипта." -ForegroundColor Red
    Write-Host "   Скачать: https://www.sqlite.org/download.html" -ForegroundColor Yellow
    exit 1
}

# Путь к базе данных
$dbPath = "refactoring.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "❌ База данных не найдена: $dbPath" -ForegroundColor Red
    Write-Host "   Запустите приложение сначала для создания базы данных." -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ База данных найдена: $dbPath" -ForegroundColor Green
Write-Host ""

# Получение списка диалогов
Write-Host "📋 Список диалогов:" -ForegroundColor Cyan
$dialogues = sqlite3 $dbPath "SELECT Id, ProjectPath, CreatedAt FROM Dialogues ORDER BY Id;"

if ([string]::IsNullOrWhiteSpace($dialogues)) {
    Write-Host "   Нет диалогов в базе данных" -ForegroundColor Yellow
    Write-Host ""
    
    # Предложение создать тестовый диалог
    $create = Read-Host "Создать тестовый диалог? (y/n)"
    if ($create -eq 'y' -or $create -eq 'Y') {
        $testPath = "D:\SITES\My\test_CodeDev"
        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        
        sqlite3 $dbPath "INSERT INTO Dialogues (ProjectPath, CreatedAt) VALUES ('$testPath', '$timestamp');"
        
        Write-Host "✅ Тестовый диалог создан" -ForegroundColor Green
        Write-Host "   ID: 1" -ForegroundColor White
        Write-Host "   ProjectPath: $testPath" -ForegroundColor White
        Write-Host "   CreatedAt: $timestamp" -ForegroundColor White
    }
} else {
    $dialogues -split "`n" | ForEach-Object {
        if ($_ -match '(\d+)\|(.+)\|(.+)') {
            $id = $matches[1]
            $path = $matches[2]
            $created = $matches[3]
            
            Write-Host "   ID: $id" -ForegroundColor White
            Write-Host "      Path: $path" -ForegroundColor Gray
            Write-Host "      Created: $created" -ForegroundColor Gray
            Write-Host ""
        }
    }
}

# Получение количества сообщений
Write-Host "📨 Статистика сообщений:" -ForegroundColor Cyan
$messageStats = sqlite3 $dbPath "SELECT DialogueId, COUNT(*) as Count FROM Messages GROUP BY DialogueId;"

if ([string]::IsNullOrWhiteSpace($messageStats)) {
    Write-Host "   Нет сообщений в базе данных" -ForegroundColor Yellow
} else {
    $messageStats -split "`n" | ForEach-Object {
        if ($_ -match '(\d+)\|(\d+)') {
            $dialogueId = $matches[1]
            $count = $matches[2]
            Write-Host "   Диалог $dialogueId : $count сообщений" -ForegroundColor White
        }
    }
}

Write-Host ""
Write-Host "✅ Проверка завершена" -ForegroundColor Green

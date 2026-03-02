# Скрипт для удаления планов задач из БД

$dbPath = "refactoring.db"

# Загружаем System.Data.SQLite
Add-Type -Path "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Data.SQLite\v4.0_1.0.118.0__db937bc2d44ff139\System.Data.SQLite.dll" -ErrorAction SilentlyContinue

# Создаем подключение
$connectionString = "Data Source=$dbPath;Version=3;"
$connection = New-Object System.Data.SQLite.SQLiteConnection($connectionString)

try {
    $connection.Open()
    
    # Удаляем все планы задач
    $command = $connection.CreateCommand()
    $command.CommandText = "DELETE FROM TaskPlans;"
    $rowsAffected = $command.ExecuteNonQuery()
    
    Write-Host "Удалено планов задач: $rowsAffected"
    
    # Проверяем что удалено
    $command.CommandText = "SELECT COUNT(*) FROM TaskPlans;"
    $count = $command.ExecuteScalar()
    Write-Host "Осталось планов в БД: $count"
    
} catch {
    Write-Host "Ошибка: $_"
} finally {
    $connection.Close()
}

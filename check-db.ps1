# Проверка состояния БД
Add-Type -Path "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Data.SQLite\v4.0_1.0.118.0__db937bc2d44ff139\System.Data.SQLite.dll" -ErrorAction SilentlyContinue

$connectionString = "Data Source=refactoring.db"
$connection = New-Object System.Data.SQLite.SQLiteConnection($connectionString)

try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT Id, Name, ProjectPath, IsSelected FROM Projects"
    
    $reader = $command.ExecuteReader()
    
    Write-Host "=== Проекты в БД ===" -ForegroundColor Cyan
    while ($reader.Read()) {
        $id = $reader["Id"]
        $name = $reader["Name"]
        $path = $reader["ProjectPath"]
        $selected = $reader["IsSelected"]
        
        $marker = if ($selected -eq 1) { "[ВЫБРАН]" } else { "" }
        Write-Host "$id. $name - $path $marker" -ForegroundColor $(if ($selected -eq 1) { "Green" } else { "White" })
    }
    
    $reader.Close()
} catch {
    Write-Host "Ошибка: $_" -ForegroundColor Red
} finally {
    $connection.Close()
}

@echo off
chcp 1251 > nul
setlocal enabledelayedexpansion

:: Получаем путь к текущей папке
set "current_dir=%~dp0"
cd /d "%current_dir%"

:: Создаем итоговый файл и сразу записываем заголовок
> context.txt echo.
echo Структура папки: >> context.txt
echo ================= >> context.txt

:: Используем tree с ASCII-символами (/A) для лучшей совместимости с фильтрацией
tree /F /A > temp_full.txt 2>&1

:: Пропускаем первые 5 строк (заголовок тома и лишние пустые) и сохраняем остальное
more +5 temp_full.txt > temp_tree.txt 2>nul
if errorlevel 1 copy /y temp_full.txt temp_tree.txt >nul

:: Фильтруем дерево, исключая строки, содержащие игнорируемые папки или файлы
findstr /v /i /c:"context.bat" /c:"context.txt" /c:".git" /c:"node_modules" /c:".kiro" /c:".serena" /c:".github" /c:".vscode" /c:"_Readme" /c:"bin" /c:"obj" /c:"Debug" temp_tree.txt >> context.txt

:: Удаляем временные файлы дерева
del temp_full.txt >nul 2>&1
del temp_tree.txt >nul 2>&1

echo. >> context.txt
echo ================= >> context.txt
echo. >> context.txt

:: --- Сбор всех файлов, исключая игнорируемые папки ---
dir /s /b /a-d > temp_all_files.txt 2>nul
if not exist temp_all_files.txt (
    echo Ошибка: не удалось получить список файлов.
    pause
    exit /b 1
)

:: Фильтруем список файлов, исключая пути, содержащие игнорируемые папки (с любым регистром)
:: Используем findstr с несколькими условиями /v /i и точными подстроками вида "\папка\"
findstr /v /i /c:"\\.git\\" /c:"\\.kiro\\" /c:"\\.serena\\" /c:"\\.github\\" /c:"\\.vscode\\" /c:"\\_Readme\\" /c:"\\bin\\" /c:"\\obj\\" /c:"\\Debug\\" temp_all_files.txt > temp_filtered_files.txt

:: Если все файлы оказались в исключенных папках, просто копируем полный список
if errorlevel 1 copy /y temp_all_files.txt temp_filtered_files.txt >nul

:: --- Обработка каждого файла из отфильтрованного списка ---
for /f "usebackq delims=" %%f in (temp_filtered_files.txt) do (
    set "file_name=%%~nxf"
    set "file_ext=%%~xf"
    
    :: Приводим расширение к нижнему регистру (упрощённо)
    set "ext=!file_ext!"
    if defined ext (
        set "ext=!ext:.=!"
        set "ext=!ext:,=!"
        set "ext=!ext: =!"
        set "ext=!ext:~0,4!"
        for %%c in (A B C D E F G H I J K L M N O P Q R S T U V W X Y Z) do (
            set "ext=!ext:%%c=%%c!"
        )
    )
    
    :: Проверка на игнорирование по типу файла
    set "ignore=0"
    if /i "!file_name!"=="context.bat" set "ignore=1"
    if /i "!file_name!"=="context.txt" set "ignore=1"
    if /i "!ext!"=="jpg" set "ignore=1"
    if /i "!ext!"=="jpeg" set "ignore=1"
    if /i "!ext!"=="png" set "ignore=1"
    if /i "!ext!"=="svg" set "ignore=1"
    if /i "!ext!"=="gif" set "ignore=1"
    if /i "!ext!"=="bmp" set "ignore=1"
    if /i "!ext!"=="webp" set "ignore=1"
    if /i "!ext!"=="ico" set "ignore=1"
    if /i "!ext!"=="exe" set "ignore=1"
    if /i "!ext!"=="dll" set "ignore=1"
    if /i "!ext!"=="zip" set "ignore=1"
    if /i "!ext!"=="rar" set "ignore=1"
    if /i "!ext!"=="7z" set "ignore=1"
    if /i "!ext!"=="pdf" set "ignore=1"
    if /i "!ext!"=="doc" set "ignore=1"
    if /i "!ext!"=="docx" set "ignore=1"
    if /i "!ext!"=="xls" set "ignore=1"
    if /i "!ext!"=="xlsx" set "ignore=1"
    if /i "!ext!"=="class" set "ignore=1"
    if /i "!ext!"=="jar" set "ignore=1"
    
    if "!ignore!"=="1" (
        echo Игнорируем (тип): %%f
    ) else (
        echo Обработка: %%f
        
        :: Относительный путь
        set "rel=%%f"
        set "rel=!rel:%current_dir%=!"
        if "!rel:~0,1!"=="\" set "rel=!rel:~1!"
        
        :: Заголовок файла в context.txt
        echo /!rel!: >> context.txt
        
        :: Чтение содержимого через PowerShell (с определением кодировки)
        powershell -NoProfile -Command ^
            "$f=[System.IO.Path]::GetFullPath('%%f');" ^
            "$enc=@([System.Text.Encoding]::UTF8,[System.Text.Encoding]::GetEncoding(1251),[System.Text.Encoding]::Default);" ^
            "$c=$null; foreach($e in $enc){try{$c=[System.IO.File]::ReadAllText($f,$e); if($c.Length-gt0){break}}catch{}};" ^
            "if($c-eq$null){try{$b=[System.IO.File]::ReadAllBytes($f); $c=[System.Text.Encoding]::UTF8.GetString($b)}catch{$c='[Ошибка чтения]'}};" ^
            "[System.Console]::OutputEncoding=[System.Text.Encoding]::GetEncoding(1251); [System.Console]::Write($c);" >> context.txt 2>nul
        
        :: Разделители
        echo. >> context.txt
        echo. >> context.txt
    )
)

:: Очистка временных файлов
del temp_all_files.txt >nul 2>&1
del temp_filtered_files.txt >nul 2>&1

echo.
echo =====================================================
echo Файл context.txt успешно создан!
echo Путь: %current_dir%context.txt
echo.
echo Исключены папки: .git, .kiro, .serena, .github, .vscode, _Readme, bin, obj, Debug
echo Исключены типы: изображения, бинарники, офисные файлы, class, jar
echo =====================================================
echo.

pause
endlocal
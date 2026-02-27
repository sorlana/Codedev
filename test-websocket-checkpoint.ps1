# Скрипт для тестирования WebSocket и streaming функциональности
# Checkpoint задача 10

Write-Host "=== Checkpoint: Тестирование WebSocket и streaming ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "Этот скрипт поможет вам протестировать следующие аспекты:" -ForegroundColor Yellow
Write-Host "1. WebSocket соединение устанавливается при загрузке" -ForegroundColor White
Write-Host "2. Отправка сообщений через WebSocket" -ForegroundColor White
Write-Host "3. Отображение streaming ответа по частям" -ForegroundColor White
Write-Host "4. Работа кнопки 'Остановить генерацию'" -ForegroundColor White
Write-Host "5. Автоматическое переподключение при разрыве" -ForegroundColor White
Write-Host ""

Write-Host "=== Шаг 1: Запуск приложения ===" -ForegroundColor Green
Write-Host "Запускаем сервер..." -ForegroundColor Yellow

# Проверка, запущен ли уже сервер
$process = Get-Process -Name "CSharpRefactoringAssistant" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Сервер уже запущен (PID: $($process.Id))" -ForegroundColor Green
} else {
    Write-Host "Запуск сервера в фоновом режиме..." -ForegroundColor Yellow
    Start-Process -FilePath "dotnet" -ArgumentList "run" -NoNewWindow -PassThru
    Write-Host "Ожидание запуска сервера (10 секунд)..." -ForegroundColor Yellow
    Start-Sleep -Seconds 10
}

Write-Host ""
Write-Host "=== Шаг 2: Открытие браузера ===" -ForegroundColor Green
Write-Host "Откройте браузер и перейдите по адресу: http://localhost:5000" -ForegroundColor Cyan
Write-Host ""

Write-Host "=== Инструкции по тестированию ===" -ForegroundColor Green
Write-Host ""

Write-Host "ТЕСТ 1: WebSocket соединение устанавливается при загрузке" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------" -ForegroundColor Gray
Write-Host "1. Откройте DevTools (F12) и перейдите на вкладку Console" -ForegroundColor White
Write-Host "2. Создайте или выберите существующий диалог" -ForegroundColor White
Write-Host "3. Проверьте в консоли сообщения:" -ForegroundColor White
Write-Host "   - '[WebSocket] Подключение к ws://localhost:5000/ws?dialogueId=X...'" -ForegroundColor Gray
Write-Host "   - '[WebSocket] Соединение установлено'" -ForegroundColor Gray
Write-Host "4. Перейдите на вкладку Network -> WS (WebSocket)" -ForegroundColor White
Write-Host "5. Убедитесь, что есть активное WebSocket соединение" -ForegroundColor White
Write-Host ""
$test1 = Read-Host "Тест 1 пройден? (y/n)"
Write-Host ""

Write-Host "ТЕСТ 2: Отправка сообщений через WebSocket" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------" -ForegroundColor Gray
Write-Host "1. В поле ввода введите простое сообщение: 'Привет'" -ForegroundColor White
Write-Host "2. Нажмите 'Отправить' или Enter" -ForegroundColor White
Write-Host "3. В консоли проверьте сообщения:" -ForegroundColor White
Write-Host "   - '[UI] Отправка сообщения через WebSocket'" -ForegroundColor Gray
Write-Host "   - '[WebSocket] Сообщение отправлено: {type: user_message, ...}'" -ForegroundColor Gray
Write-Host "4. На вкладке Network -> WS проверьте отправленное сообщение" -ForegroundColor White
Write-Host "5. Убедитесь, что сообщение пользователя отображается мгновенно" -ForegroundColor White
Write-Host ""
$test2 = Read-Host "Тест 2 пройден? (y/n)"
Write-Host ""

Write-Host "ТЕСТ 3: Отображение streaming ответа по частям" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------" -ForegroundColor Gray
Write-Host "1. Отправьте сообщение, которое вызовет ответ LLM:" -ForegroundColor White
Write-Host "   Например: 'Объясни что такое рефакторинг'" -ForegroundColor Gray
Write-Host "2. Наблюдайте за консолью:" -ForegroundColor White
Write-Host "   - '[UI] Получено assistant_message_start'" -ForegroundColor Gray
Write-Host "   - '[UI] Получен assistant_message_chunk' (несколько раз)" -ForegroundColor Gray
Write-Host "   - '[UI] Получено assistant_message_end'" -ForegroundColor Gray
Write-Host "3. Убедитесь, что ответ появляется постепенно, по частям" -ForegroundColor White
Write-Host "4. Проверьте, что текст добавляется плавно без мерцания" -ForegroundColor White
Write-Host "5. Проверьте логи времени рендеринга в консоли" -ForegroundColor White
Write-Host ""
$test3 = Read-Host "Тест 3 пройден? (y/n)"
Write-Host ""

Write-Host "ТЕСТ 4: Работа кнопки 'Остановить генерацию'" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------" -ForegroundColor Gray
Write-Host "1. Отправьте сообщение, которое вызовет длинный ответ:" -ForegroundColor White
Write-Host "   Например: 'Расскажи подробно о паттернах проектирования'" -ForegroundColor Gray
Write-Host "2. Как только начнется streaming, нажмите кнопку '⏹ Остановить генерацию'" -ForegroundColor White
Write-Host "3. Проверьте в консоли:" -ForegroundColor White
Write-Host "   - '[UI] Отправка команды отмены генерации'" -ForegroundColor Gray
Write-Host "   - '[UI] Команда отмены отправлена успешно'" -ForegroundColor Gray
Write-Host "4. Убедитесь, что генерация остановилась в течение 1 секунды" -ForegroundColor White
Write-Host "5. Убедитесь, что кнопка 'Остановить генерацию' исчезла" -ForegroundColor White
Write-Host "6. Проверьте, что частичный ответ сохранен и отображается" -ForegroundColor White
Write-Host ""
$test4 = Read-Host "Тест 4 пройден? (y/n)"
Write-Host ""

Write-Host "ТЕСТ 5: Автоматическое переподключение при разрыве" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------" -ForegroundColor Gray
Write-Host "1. В консоли DevTools выполните команду для закрытия WebSocket:" -ForegroundColor White
Write-Host "   wsClient.ws.close()" -ForegroundColor Gray
Write-Host "2. Наблюдайте за консолью:" -ForegroundColor White
Write-Host "   - '[WebSocket] Соединение закрыто'" -ForegroundColor Gray
Write-Host "   - '[WebSocket] Попытка переподключения 1/5 через Xms...'" -ForegroundColor Gray
Write-Host "   - '[WebSocket] Подключение к ws://...'" -ForegroundColor Gray
Write-Host "   - '[WebSocket] Соединение установлено'" -ForegroundColor Gray
Write-Host "3. Убедитесь, что переподключение произошло автоматически" -ForegroundColor White
Write-Host "4. Проверьте, что задержка увеличивается экспоненциально:" -ForegroundColor White
Write-Host "   - Попытка 1: ~1000ms" -ForegroundColor Gray
Write-Host "   - Попытка 2: ~2000ms" -ForegroundColor Gray
Write-Host "   - Попытка 3: ~4000ms" -ForegroundColor Gray
Write-Host "5. Отправьте тестовое сообщение после переподключения" -ForegroundColor White
Write-Host "6. Убедитесь, что сообщение отправляется успешно" -ForegroundColor White
Write-Host ""
$test5 = Read-Host "Тест 5 пройден? (y/n)"
Write-Host ""

Write-Host "=== Дополнительные проверки ===" -ForegroundColor Green
Write-Host ""

Write-Host "ДОПОЛНИТЕЛЬНО: Проверка логирования" -ForegroundColor Cyan
Write-Host "-------------------------------------------------------" -ForegroundColor Gray
Write-Host "1. Проверьте, что все события WebSocket логируются с временными метками" -ForegroundColor White
Write-Host "2. Проверьте логи мониторинга производительности:" -ForegroundColor White
Write-Host "   - '[Monitoring] Streaming начат'" -ForegroundColor Gray
Write-Host "   - '[Monitoring] Общее время streaming: Xms'" -ForegroundColor Gray
Write-Host "   - Предупреждения о задержках > 100ms" -ForegroundColor Gray
Write-Host ""
$testExtra = Read-Host "Дополнительные проверки пройдены? (y/n)"
Write-Host ""

# Подсчет результатов
$passed = 0
$total = 6

if ($test1 -eq "y") { $passed++ }
if ($test2 -eq "y") { $passed++ }
if ($test3 -eq "y") { $passed++ }
if ($test4 -eq "y") { $passed++ }
if ($test5 -eq "y") { $passed++ }
if ($testExtra -eq "y") { $passed++ }

Write-Host "=== Результаты тестирования ===" -ForegroundColor Green
Write-Host "Пройдено тестов: $passed из $total" -ForegroundColor $(if ($passed -eq $total) { "Green" } else { "Yellow" })
Write-Host ""

if ($passed -eq $total) {
    Write-Host "✅ Все тесты пройдены успешно!" -ForegroundColor Green
    Write-Host "WebSocket и streaming функциональность работает корректно." -ForegroundColor Green
} elseif ($passed -ge 4) {
    Write-Host "⚠️ Большинство тестов пройдено, но есть проблемы." -ForegroundColor Yellow
    Write-Host "Рекомендуется проверить непройденные тесты." -ForegroundColor Yellow
} else {
    Write-Host "❌ Обнаружены серьезные проблемы." -ForegroundColor Red
    Write-Host "Требуется дополнительная отладка." -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Вопросы пользователю ===" -ForegroundColor Green
Write-Host ""
Write-Host "Есть ли у вас вопросы по результатам тестирования?" -ForegroundColor Cyan
Write-Host "Обнаружили ли вы какие-либо проблемы или неожиданное поведение?" -ForegroundColor Cyan
Write-Host "Требуется ли дополнительная отладка или улучшения?" -ForegroundColor Cyan
Write-Host ""

$userFeedback = Read-Host "Введите ваши комментарии (или нажмите Enter для продолжения)"

if ($userFeedback) {
    Write-Host ""
    Write-Host "Комментарии пользователя:" -ForegroundColor Yellow
    Write-Host $userFeedback -ForegroundColor White
}

Write-Host ""
Write-Host "=== Checkpoint завершен ===" -ForegroundColor Green
Write-Host "Тестирование WebSocket и streaming функциональности завершено." -ForegroundColor White
Write-Host ""

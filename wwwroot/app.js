const API_BASE = '';

let currentDialogueId = null;
let isProcessing = false;
let projects = [];
let wsClient = null; // Глобальный экземпляр WebSocketClient
let messageCache = null; // Глобальный экземпляр MessageCache
let virtualList = null; // Глобальный экземпляр VirtualList
let draftManager = null; // Глобальный экземпляр DraftManager

// Режим отладки для отображения метрик производительности
window.DEBUG_MODE = false; // Установить в true для включения режима отладки

// Объект для хранения метрик производительности
window.performanceMetrics = {
    ttfb: null,
    fps: null,
    cacheSize: 0,
    cacheMessages: 0,
    connectionStatus: 'disconnected',
    lastStreamingTime: null
};

// WebSocketClient для real-time коммуникации
class WebSocketClient {
    constructor(dialogueId) {
        this.dialogueId = dialogueId;
        this.ws = null;
        this.connectionId = null;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 5;
        this.reconnectDelay = 1000; // Начальная задержка 1s
        this.maxReconnectDelay = 30000; // Максимальная задержка 30s
        this.isConnected = false;
        this.isUsingHttp = false;
        this.messageHandlers = new Map();
        this.onConnectionChange = null;
    }
    
    // Установка WebSocket соединения
    async connect() {
        try {
            // Определение WebSocket URL (ws:// или wss:// в зависимости от протокола страницы)
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const host = window.location.host;
            const wsUrl = `${protocol}//${host}/ws?dialogueId=${this.dialogueId}`;
            
            console.log(`[WebSocket] Подключение к ${wsUrl}...`);
            
            // Создание WebSocket соединения
            this.ws = new WebSocket(wsUrl);
            
            // Обработчик успешного подключения
            this.ws.onopen = (event) => {
                const timestamp = new Date().toISOString();
                console.log(`[WebSocket] [${timestamp}] Соединение установлено`, event);
                console.log(`[Monitoring] [${timestamp}] WebSocket connection established for dialogue ${this.dialogueId}`);
                
                this.isConnected = true;
                this.reconnectAttempts = 0; // Сброс счетчика попыток
                
                // Уведомление о изменении статуса соединения
                if (this.onConnectionChange) {
                    this.onConnectionChange('connected');
                }
            };
            
            // Обработчик входящих сообщений
            this.ws.onmessage = (event) => {
                try {
                    const message = JSON.parse(event.data);
                    
                    // Поддержка как camelCase, так и PascalCase для совместимости
                    const type = message.type || message.Type;
                    const payload = message.payload || message.Payload;
                    
                    console.log('[WebSocket] Получено сообщение:', message);
                    console.log('[WebSocket] Тип сообщения:', type);
                    
                    // Вызов зарегистрированных обработчиков для типа сообщения
                    const handlers = this.messageHandlers.get(type);
                    if (handlers) {
                        console.log(`[WebSocket] Найдено ${handlers.length} обработчиков для типа ${type}`);
                        handlers.forEach(handler => {
                            try {
                                handler(payload);
                            } catch (error) {
                                console.error('[WebSocket] Ошибка в обработчике сообщения:', error);
                            }
                        });
                    } else {
                        console.warn(`[WebSocket] Нет обработчиков для типа сообщения: ${type}`);
                    }
                } catch (error) {
                    console.error('[WebSocket] Ошибка парсинга сообщения:', error);
                }
            };
            
            // Обработчик ошибок
            this.ws.onerror = (error) => {
                const timestamp = new Date().toISOString();
                console.error(`[WebSocket] [${timestamp}] Ошибка соединения:`, error);
                console.error(`[Monitoring] [${timestamp}] WebSocket error for dialogue ${this.dialogueId}:`, error);
                
                this.isConnected = false;
                
                // Уведомление о изменении статуса соединения
                if (this.onConnectionChange) {
                    this.onConnectionChange('error');
                }
            };
            
            // Обработчик закрытия соединения
            this.ws.onclose = (event) => {
                const timestamp = new Date().toISOString();
                const reason = event.reason || 'No reason provided';
                console.log(`[WebSocket] [${timestamp}] Соединение закрыто: код ${event.code}, причина: ${reason}`);
                console.log(`[Monitoring] [${timestamp}] WebSocket disconnected for dialogue ${this.dialogueId}, code: ${event.code}, reason: ${reason}`);
                
                this.isConnected = false;
                
                // Уведомление о изменении статуса соединения
                if (this.onConnectionChange) {
                    this.onConnectionChange('disconnected');
                }
                
                // Автоматическое переподключение если не достигнут лимит попыток
                if (this.reconnectAttempts < this.maxReconnectAttempts && !this.isUsingHttp) {
                    this.reconnect();
                } else if (this.reconnectAttempts >= this.maxReconnectAttempts && !this.isUsingHttp) {
                    // Переключение на HTTP режим после исчерпания попыток
                    this.fallbackToHttp();
                }
            };
            
        } catch (error) {
            console.error('[WebSocket] Ошибка создания соединения:', error);
            this.isConnected = false;
            
            // Попытка переподключения
            if (this.reconnectAttempts < this.maxReconnectAttempts) {
                this.reconnect();
            } else {
                this.fallbackToHttp();
            }
        }
    }
    
    // Отправка сообщения через WebSocket
    async sendMessage(type, payload) {
        if (!this.isConnected || !this.ws || this.ws.readyState !== WebSocket.OPEN) {
            console.warn('[WebSocket] Соединение не активно, сообщение не отправлено');
            return false;
        }
        
        try {
            const message = {
                type: type,
                payload: payload,
                timestamp: new Date().toISOString()
            };
            
            this.ws.send(JSON.stringify(message));
            console.log('[WebSocket] Сообщение отправлено:', message);
            return true;
        } catch (error) {
            console.error('[WebSocket] Ошибка отправки сообщения:', error);
            return false;
        }
    }
    
    // Регистрация обработчика для типа сообщения
    on(messageType, handler) {
        if (!this.messageHandlers.has(messageType)) {
            this.messageHandlers.set(messageType, []);
        }
        
        this.messageHandlers.get(messageType).push(handler);
        console.log(`[WebSocket] Зарегистрирован обработчик для типа: ${messageType}`);
    }
    
    // Отключение WebSocket соединения
    disconnect() {
        if (this.ws) {
            console.log('[WebSocket] Закрытие соединения...');
            this.ws.close();
            this.ws = null;
        }
        
        this.isConnected = false;
        this.messageHandlers.clear();
    }
    
    // Переподключение с экспоненциальной задержкой
    async reconnect() {
        this.reconnectAttempts++;
        
        const timestamp = new Date().toISOString();
        
        // Вычисление задержки с экспоненциальным ростом
        const delay = Math.min(
            this.reconnectDelay * Math.pow(2, this.reconnectAttempts - 1),
            this.maxReconnectDelay
        );
        
        console.log(`[WebSocket] [${timestamp}] Попытка переподключения ${this.reconnectAttempts}/${this.maxReconnectAttempts} через ${delay}ms...`);
        console.log(`[Monitoring] [${timestamp}] Reconnection attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts} for dialogue ${this.dialogueId}, delay: ${delay}ms`);
        
        // Уведомление о попытке переподключения
        if (this.onConnectionChange) {
            this.onConnectionChange('reconnecting', { attempt: this.reconnectAttempts, delay });
        }
        
        // Задержка перед переподключением
        await new Promise(resolve => setTimeout(resolve, delay));
        
        // Попытка подключения
        await this.connect();
    }
    
    // Переключение на HTTP режим
    async fallbackToHttp() {
        console.warn('[WebSocket] Переключение на HTTP режим после неудачных попыток переподключения');
        
        this.isUsingHttp = true;
        this.isConnected = false;
        
        // Уведомление пользователя
        if (this.onConnectionChange) {
            this.onConnectionChange('http_fallback');
        }
        
        // Отображение уведомления в UI
        showStatusMessage(
            'WebSocket недоступен. Переключено на HTTP режим. Некоторые функции могут быть ограничены.',
            'error'
        );
    }
}

// MessageCache для кэширования сообщений в localStorage
class MessageCache {
    constructor() {
        this.cachePrefix = 'msg_cache_';
        this.maxMessagesPerDialogue = 100;
        this.maxCacheSize = 5 * 1024 * 1024; // 5MB
        this.ttl = 24 * 60 * 60 * 1000; // 24 часа в миллисекундах
        this.isAvailable = this.checkLocalStorageAvailability();
        
        if (!this.isAvailable) {
            console.warn('[MessageCache] localStorage недоступен, кэширование отключено');
        }
    }
    
    // Проверка доступности localStorage
    checkLocalStorageAvailability() {
        try {
            const test = '__localStorage_test__';
            localStorage.setItem(test, test);
            localStorage.removeItem(test);
            return true;
        } catch (e) {
            return false;
        }
    }
    
    // Получение кэшированных сообщений для диалога
    getCachedMessages(dialogueId) {
        if (!this.isAvailable) {
            return null;
        }
        
        try {
            const key = this.cachePrefix + dialogueId;
            const cached = localStorage.getItem(key);
            
            if (!cached) {
                return null;
            }
            
            const data = JSON.parse(cached);
            
            // Проверка срока действия кэша
            const lastUpdated = new Date(data.lastUpdated);
            const now = new Date();
            const age = now - lastUpdated;
            
            if (age > this.ttl) {
                console.log(`[MessageCache] Кэш для диалога ${dialogueId} устарел, удаляем`);
                localStorage.removeItem(key);
                return null;
            }
            
            console.log(`[MessageCache] Загружено ${data.messages.length} сообщений из кэша для диалога ${dialogueId}`);
            return data.messages;
            
        } catch (error) {
            console.error('[MessageCache] Ошибка чтения кэша:', error);
            return null;
        }
    }
    
    // Сохранение сообщений в кэш
    cacheMessages(dialogueId, messages) {
        if (!this.isAvailable) {
            return;
        }
        
        try {
            const key = this.cachePrefix + dialogueId;
            const data = {
                dialogueId: dialogueId,
                messages: messages,
                lastUpdated: new Date().toISOString(),
                version: 1
            };
            
            const jsonData = JSON.stringify(data);
            const dataSize = new Blob([jsonData]).size;
            
            localStorage.setItem(key, jsonData);
            console.log(`[MessageCache] Сохранено ${messages.length} сообщений в кэш для диалога ${dialogueId}`);
            console.log(`[Monitoring] Cache operation: saved ${messages.length} messages, size: ${(dataSize / 1024).toFixed(2)} KB for dialogue ${dialogueId}`);
            
            // Проверка размера кэша после сохранения
            this.checkCacheSize();
            
        } catch (error) {
            if (error.name === 'QuotaExceededError') {
                console.warn('[MessageCache] Превышена квота localStorage, очищаем кэш');
                console.warn('[Monitoring] Cache quota exceeded, cleaning expired cache');
                this.cleanExpiredCache();
                
                // Повторная попытка после очистки
                try {
                    const jsonData = JSON.stringify(data);
                    localStorage.setItem(key, jsonData);
                    console.log(`[MessageCache] Повторное сохранение успешно после очистки`);
                } catch (e2) {
                    console.error('[MessageCache] Не удалось сохранить после очистки:', e2);
                }
            } else {
                console.error('[MessageCache] Ошибка сохранения в кэш:', error);
            }
        }
    }
    
    // Добавление одного сообщения в кэш
    addMessage(dialogueId, message) {
        if (!this.isAvailable) {
            return;
        }
        
        try {
            // Получение текущего кэша
            const cached = this.getCachedMessages(dialogueId);
            let messages = cached || [];
            
            // Добавление нового сообщения
            messages.push(message);
            
            // Обрезка до максимального количества
            this.trimCache(dialogueId, messages);
            
            // Сохранение обновленного кэша
            this.cacheMessages(dialogueId, messages);
            
            console.log(`[Monitoring] Cache operation: added 1 message, total messages: ${messages.length} for dialogue ${dialogueId}`);
            
        } catch (error) {
            console.error('[MessageCache] Ошибка добавления сообщения:', error);
        }
    }
    
    // Обрезка кэша до максимального количества сообщений
    trimCache(dialogueId, messages) {
        if (messages.length > this.maxMessagesPerDialogue) {
            const removed = messages.length - this.maxMessagesPerDialogue;
            messages.splice(0, removed);
            console.log(`[MessageCache] Удалено ${removed} старых сообщений из кэша диалога ${dialogueId}`);
        }
    }
    
    // Очистка устаревших сообщений (старше 24 часов)
    cleanExpiredCache() {
        if (!this.isAvailable) {
            return;
        }
        
        try {
            const now = new Date();
            let cleanedCount = 0;
            let totalSizeFreed = 0;
            
            // Перебор всех ключей в localStorage
            for (let i = 0; i < localStorage.length; i++) {
                const key = localStorage.key(i);
                
                // Проверка, что это ключ кэша сообщений
                if (key && key.startsWith(this.cachePrefix)) {
                    try {
                        const cached = localStorage.getItem(key);
                        if (cached) {
                            const dataSize = new Blob([cached]).size;
                            const data = JSON.parse(cached);
                            const lastUpdated = new Date(data.lastUpdated);
                            const age = now - lastUpdated;
                            
                            // Удаление если старше TTL
                            if (age > this.ttl) {
                                localStorage.removeItem(key);
                                cleanedCount++;
                                totalSizeFreed += dataSize;
                            }
                        }
                    } catch (error) {
                        // Удаление поврежденных записей
                        console.warn(`[MessageCache] Удаление поврежденной записи: ${key}`);
                        const cached = localStorage.getItem(key);
                        if (cached) {
                            totalSizeFreed += new Blob([cached]).size;
                        }
                        localStorage.removeItem(key);
                        cleanedCount++;
                    }
                }
            }
            
            if (cleanedCount > 0) {
                console.log(`[MessageCache] Очищено ${cleanedCount} устаревших записей кэша`);
                console.log(`[Monitoring] Cache cleanup: removed ${cleanedCount} entries, freed ${(totalSizeFreed / 1024).toFixed(2)} KB`);
            }
            
        } catch (error) {
            console.error('[MessageCache] Ошибка очистки устаревшего кэша:', error);
        }
    }
    
    // Проверка общего размера кэша и очистка при превышении лимита
    checkCacheSize() {
        if (!this.isAvailable) {
            return;
        }
        
        try {
            // Подсчет общего размера кэша сообщений
            let totalSize = 0;
            let totalMessages = 0;
            const cacheEntries = [];
            
            for (let i = 0; i < localStorage.length; i++) {
                const key = localStorage.key(i);
                
                if (key && key.startsWith(this.cachePrefix)) {
                    const value = localStorage.getItem(key);
                    if (value) {
                        const size = new Blob([value]).size;
                        totalSize += size;
                        
                        try {
                            const data = JSON.parse(value);
                            totalMessages += data.messages ? data.messages.length : 0;
                            cacheEntries.push({
                                key: key,
                                size: size,
                                messageCount: data.messages ? data.messages.length : 0,
                                lastUpdated: new Date(data.lastUpdated)
                            });
                        } catch (e) {
                            // Игнорируем поврежденные записи
                        }
                    }
                }
            }
            
            console.log(`[MessageCache] Общий размер кэша: ${(totalSize / 1024 / 1024).toFixed(2)} MB`);
            console.log(`[Monitoring] Cache status: ${cacheEntries.length} dialogues, ${totalMessages} total messages, ${(totalSize / 1024 / 1024).toFixed(2)} MB`);
            
            // Обновление метрик кэша
            updatePerformanceMetric('cacheSize', totalSize);
            updatePerformanceMetric('cacheMessages', totalMessages);
            
            // Если превышен лимит 5MB, удаляем самые старые до достижения 4MB
            if (totalSize > this.maxCacheSize) {
                console.warn(`[MessageCache] Превышен лимит размера кэша (${(totalSize / 1024 / 1024).toFixed(2)} MB), начинаем очистку`);
                console.warn(`[Monitoring] Cache size limit exceeded: ${(totalSize / 1024 / 1024).toFixed(2)} MB > ${(this.maxCacheSize / 1024 / 1024).toFixed(2)} MB`);
                
                // Сортировка по дате (самые старые первыми)
                cacheEntries.sort((a, b) => a.lastUpdated - b.lastUpdated);
                
                // Удаление самых старых записей до достижения 4MB
                const targetSize = 4 * 1024 * 1024; // 4MB
                let currentSize = totalSize;
                let removedCount = 0;
                let removedMessages = 0;
                
                for (const entry of cacheEntries) {
                    if (currentSize <= targetSize) {
                        break;
                    }
                    
                    localStorage.removeItem(entry.key);
                    currentSize -= entry.size;
                    removedCount++;
                    removedMessages += entry.messageCount;
                }
                
                console.log(`[MessageCache] Удалено ${removedCount} записей, новый размер: ${(currentSize / 1024 / 1024).toFixed(2)} MB`);
                console.log(`[Monitoring] Cache cleanup: removed ${removedCount} dialogues (${removedMessages} messages), new size: ${(currentSize / 1024 / 1024).toFixed(2)} MB`);
            }
            
        } catch (error) {
            console.error('[MessageCache] Ошибка проверки размера кэша:', error);
        }
    }
}

// VirtualList для виртуализации списка сообщений
class VirtualList {
    constructor(containerElement, itemHeight) {
        this.container = containerElement;
        this.itemHeight = itemHeight;
        this.items = [];
        this.visibleRange = { start: 0, end: 0 };
        this.scrollTop = 0;
        this.viewportHeight = 0;
        this.bufferSize = 10; // Дополнительные элементы сверху/снизу
        this.isSupported = this.checkSupport();
        
        // Переменные для мониторинга FPS
        this.fpsFrameTimes = [];
        this.fpsLastFrameTime = null;
        this.fpsWarningThreshold = 30; // Порог предупреждения FPS
        
        if (!this.isSupported) {
            console.warn('[VirtualList] Виртуализация не поддерживается, используется полный рендеринг');
        }
    }
    
    // Проверка поддержки необходимых API
    checkSupport() {
        return 'IntersectionObserver' in window && 'ResizeObserver' in window;
    }
    
    // Установка данных для виртуализации
    setItems(items) {
        this.items = items;
        this.calculateVisibleRange();
        this.render();
    }
    
    // Вычисление видимого диапазона элементов
    calculateVisibleRange() {
        if (!this.isSupported || this.items.length <= 50) {
            // Если виртуализация не поддерживается или элементов мало, рендерим все
            this.visibleRange = { start: 0, end: this.items.length };
            return;
        }
        
        // Получение текущей позиции прокрутки и высоты viewport
        this.scrollTop = this.container.scrollTop;
        this.viewportHeight = this.container.clientHeight;
        
        // Вычисление индексов видимых элементов
        const startIndex = Math.floor(this.scrollTop / this.itemHeight);
        const endIndex = Math.ceil((this.scrollTop + this.viewportHeight) / this.itemHeight);
        
        // Добавление буфера сверху и снизу
        this.visibleRange = {
            start: Math.max(0, startIndex - this.bufferSize),
            end: Math.min(this.items.length, endIndex + this.bufferSize)
        };
        
        console.log(`[VirtualList] Видимый диапазон: ${this.visibleRange.start} - ${this.visibleRange.end} из ${this.items.length}`);
    }
    
    // Рендеринг видимых элементов
    render() {
        if (!this.isSupported || this.items.length <= 50) {
            // Полный рендеринг для старых браузеров или малого количества элементов
            this.renderAll();
            return;
        }
        
        // Очистка контейнера
        this.container.innerHTML = '';
        
        // Создание spacer элементов для сохранения высоты прокрутки
        const topSpacer = document.createElement('div');
        topSpacer.style.height = `${this.visibleRange.start * this.itemHeight}px`;
        this.container.appendChild(topSpacer);
        
        // Рендеринг видимых элементов
        for (let i = this.visibleRange.start; i < this.visibleRange.end; i++) {
            const item = this.items[i];
            const element = this.createItemElement(item, i);
            this.container.appendChild(element);
        }
        
        // Нижний spacer
        const bottomSpacer = document.createElement('div');
        const remainingItems = this.items.length - this.visibleRange.end;
        bottomSpacer.style.height = `${remainingItems * this.itemHeight}px`;
        this.container.appendChild(bottomSpacer);
        
        console.log(`[VirtualList] Отрендерено ${this.visibleRange.end - this.visibleRange.start} элементов`);
    }
    
    // Полный рендеринг всех элементов (fallback)
    renderAll() {
        this.container.innerHTML = '';
        
        for (let i = 0; i < this.items.length; i++) {
            const item = this.items[i];
            const element = this.createItemElement(item, i);
            this.container.appendChild(element);
        }
        
        // Показать предупреждение для больших списков
        if (this.items.length > 500 && !this.isSupported) {
            this.showPerformanceWarning();
        }
    }
    
    // Создание DOM элемента для сообщения
    createItemElement(item, index) {
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${item.role}`;
        messageDiv.dataset.index = index;
        
        const roleDiv = document.createElement('div');
        roleDiv.className = 'message-role';
        roleDiv.textContent = item.role === 'user' ? 'Вы' : 'Ассистент';
        
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        contentDiv.textContent = item.content;
        
        messageDiv.appendChild(roleDiv);
        messageDiv.appendChild(contentDiv);
        
        return messageDiv;
    }
    
    // Показать предупреждение о производительности
    showPerformanceWarning() {
        // Проверка, не показано ли уже предупреждение
        if (this.container.querySelector('.performance-warning')) {
            return;
        }
        
        const warning = document.createElement('div');
        warning.className = 'performance-warning';
        warning.style.padding = '10px';
        warning.style.background = '#fff3cd';
        warning.style.border = '1px solid #ffc107';
        warning.style.borderRadius = '4px';
        warning.style.marginBottom = '10px';
        warning.style.fontSize = '14px';
        warning.style.color = '#856404';
        warning.textContent = '⚠️ Внимание: большое количество сообщений может снизить производительность';
        
        this.container.insertBefore(warning, this.container.firstChild);
    }
    
    // Обработка прокрутки с debouncing и измерением FPS
    handleScroll() {
        if (!this.isSupported || this.items.length <= 50) {
            return;
        }
        
        // Использование requestAnimationFrame для плавности
        if (this.scrollRAF) {
            return; // Уже запланирован рендеринг
        }
        
        this.scrollRAF = requestAnimationFrame(() => {
            // Измерение FPS для мониторинга
            const frameStart = performance.now();
            
            // Вычисление FPS на основе времени между кадрами
            if (this.fpsLastFrameTime) {
                const frameDelta = frameStart - this.fpsLastFrameTime;
                this.fpsFrameTimes.push(frameDelta);
                
                // Хранение только последних 60 измерений (примерно 1 секунда при 60 FPS)
                if (this.fpsFrameTimes.length > 60) {
                    this.fpsFrameTimes.shift();
                }
                
                // Вычисление среднего FPS
                if (this.fpsFrameTimes.length >= 10) {
                    const avgFrameDelta = this.fpsFrameTimes.reduce((a, b) => a + b, 0) / this.fpsFrameTimes.length;
                    const avgFps = 1000 / avgFrameDelta;
                    
                    // Логирование если FPS падает ниже порога
                    if (avgFps < this.fpsWarningThreshold) {
                        console.warn(`[Monitoring] Низкий FPS при прокрутке: ${avgFps.toFixed(2)} (порог: ${this.fpsWarningThreshold})`);
                    }
                    
                    // Отображение FPS в режиме отладки (если включен)
                    if (window.DEBUG_MODE) {
                        this.displayFPS(avgFps);
                    }
                }
            }
            
            this.fpsLastFrameTime = frameStart;
            
            // Пересчет видимого диапазона
            const oldRange = { ...this.visibleRange };
            this.calculateVisibleRange();
            
            // Рендеринг только если диапазон изменился
            if (oldRange.start !== this.visibleRange.start || 
                oldRange.end !== this.visibleRange.end) {
                this.render();
            }
            
            // Измерение времени рендеринга
            const frameTime = performance.now() - frameStart;
            const instantFps = 1000 / frameTime;
            
            // Логирование если время рендеринга превышает 16.67ms (60 FPS)
            if (frameTime > 16.67) {
                console.warn(`[Monitoring] Медленный рендеринг при прокрутке: ${frameTime.toFixed(2)}ms (FPS: ${instantFps.toFixed(2)})`);
            }
            
            // Сброс флага
            this.scrollRAF = null;
        });
    }
    
    // Отображение FPS в режиме отладки
    displayFPS(fps) {
        // Обновление метрики FPS
        updatePerformanceMetric('fps', fps);
        
        let fpsDisplay = document.getElementById('fps-display');
        
        if (!fpsDisplay) {
            fpsDisplay = document.createElement('div');
            fpsDisplay.id = 'fps-display';
            fpsDisplay.style.position = 'fixed';
            fpsDisplay.style.top = '10px';
            fpsDisplay.style.right = '10px';
            fpsDisplay.style.background = 'rgba(0, 0, 0, 0.7)';
            fpsDisplay.style.color = '#fff';
            fpsDisplay.style.padding = '5px 10px';
            fpsDisplay.style.borderRadius = '4px';
            fpsDisplay.style.fontSize = '12px';
            fpsDisplay.style.fontFamily = 'monospace';
            fpsDisplay.style.zIndex = '10000';
            document.body.appendChild(fpsDisplay);
        }
        
        // Цветовая индикация FPS
        let color = '#00ff00'; // Зеленый для хорошего FPS
        if (fps < 30) {
            color = '#ff0000'; // Красный для низкого FPS
        } else if (fps < 50) {
            color = '#ffaa00'; // Оранжевый для среднего FPS
        }
        
        fpsDisplay.style.color = color;
        fpsDisplay.textContent = `FPS: ${fps.toFixed(1)}`;
    }
    
    // Инициализация обработчика прокрутки
    initScrollHandler() {
        if (!this.isSupported || this.items.length <= 50) {
            return;
        }
        
        // Привязка обработчика прокрутки
        this.boundScrollHandler = this.handleScroll.bind(this);
        this.container.addEventListener('scroll', this.boundScrollHandler);
        
        console.log('[VirtualList] Обработчик прокрутки инициализирован');
    }
    
    // Удаление обработчика прокрутки
    destroyScrollHandler() {
        if (this.boundScrollHandler) {
            this.container.removeEventListener('scroll', this.boundScrollHandler);
            this.boundScrollHandler = null;
        }
        
        if (this.scrollRAF) {
            cancelAnimationFrame(this.scrollRAF);
            this.scrollRAF = null;
        }
    }
    
    // Подгрузка истории при прокрутке к началу
    async loadMoreHistory(dialogueId, loadHistoryCallback) {
        if (!this.isSupported || this.items.length <= 50) {
            return;
        }
        
        // Проверка позиции прокрутки относительно начала
        const scrollPosition = this.container.scrollTop;
        const threshold = 20 * this.itemHeight; // Порог в 20 сообщений
        
        if (scrollPosition < threshold && !this.isLoadingHistory) {
            console.log('[VirtualList] Достигнут порог подгрузки истории');
            
            this.isLoadingHistory = true;
            
            try {
                // Сохранение текущей позиции прокрутки
                const oldScrollHeight = this.container.scrollHeight;
                const oldScrollTop = this.container.scrollTop;
                
                // Вызов callback для загрузки предыдущих сообщений
                const newMessages = await loadHistoryCallback(dialogueId, this.items.length);
                
                if (newMessages && newMessages.length > 0) {
                    // Добавление новых сообщений в начало списка
                    this.items = [...newMessages, ...this.items];
                    
                    // Рендеринг с новыми данными
                    this.render();
                    
                    // Восстановление позиции прокрутки
                    const newScrollHeight = this.container.scrollHeight;
                    const scrollDiff = newScrollHeight - oldScrollHeight;
                    this.container.scrollTop = oldScrollTop + scrollDiff;
                    
                    console.log(`[VirtualList] Загружено ${newMessages.length} предыдущих сообщений`);
                }
            } catch (error) {
                console.error('[VirtualList] Ошибка подгрузки истории:', error);
            } finally {
                this.isLoadingHistory = false;
            }
        }
    }
    
    // Инициализация обработчика подгрузки истории
    initHistoryLoader(dialogueId, loadHistoryCallback) {
        this.dialogueId = dialogueId;
        this.loadHistoryCallback = loadHistoryCallback;
        
        // Добавление обработчика прокрутки для подгрузки
        this.boundHistoryLoader = () => {
            if (this.loadHistoryCallback) {
                this.loadMoreHistory(this.dialogueId, this.loadHistoryCallback);
            }
        };
        
        this.container.addEventListener('scroll', this.boundHistoryLoader);
        console.log('[VirtualList] Обработчик подгрузки истории инициализирован');
    }
    
    // Удаление обработчика подгрузки истории
    destroyHistoryLoader() {
        if (this.boundHistoryLoader) {
            this.container.removeEventListener('scroll', this.boundHistoryLoader);
            this.boundHistoryLoader = null;
        }
        
        this.dialogueId = null;
        this.loadHistoryCallback = null;
    }
    
    // Прокрутка к конкретному элементу по индексу
    scrollToItem(index, highlight = true) {
        if (index < 0 || index >= this.items.length) {
            console.warn(`[VirtualList] Индекс ${index} вне диапазона`);
            return;
        }
        
        // Вычисление позиции элемента
        const targetScrollTop = index * this.itemHeight;
        
        // Плавная прокрутка к элементу
        this.container.scrollTo({
            top: targetScrollTop,
            behavior: 'smooth'
        });
        
        // Подсветка элемента на 2 секунды
        if (highlight) {
            setTimeout(() => {
                const element = this.container.querySelector(`[data-index="${index}"]`);
                if (element) {
                    element.style.transition = 'background-color 0.3s';
                    element.style.backgroundColor = '#fff3cd';
                    
                    setTimeout(() => {
                        element.style.backgroundColor = '';
                        setTimeout(() => {
                            element.style.transition = '';
                        }, 300);
                    }, 2000);
                }
            }, 500); // Задержка для завершения прокрутки
        }
        
        console.log(`[VirtualList] Прокрутка к элементу ${index}`);
    }
    
    // Поиск по всем сообщениям (включая не отрендеренные)
    searchMessages(query) {
        if (!query || query.trim() === '') {
            return [];
        }
        
        const lowerQuery = query.toLowerCase();
        const results = [];
        
        // Поиск по всем элементам
        for (let i = 0; i < this.items.length; i++) {
            const item = this.items[i];
            if (item.content && item.content.toLowerCase().includes(lowerQuery)) {
                results.push({
                    index: i,
                    item: item,
                    // Контекст вокруг найденного текста
                    context: this.getSearchContext(item.content, lowerQuery)
                });
            }
        }
        
        console.log(`[VirtualList] Найдено ${results.length} совпадений для "${query}"`);
        return results;
    }
    
    // Получение контекста вокруг найденного текста
    getSearchContext(content, query, contextLength = 50) {
        const index = content.toLowerCase().indexOf(query);
        if (index === -1) {
            return content.substring(0, 100);
        }
        
        const start = Math.max(0, index - contextLength);
        const end = Math.min(content.length, index + query.length + contextLength);
        
        let context = content.substring(start, end);
        
        if (start > 0) {
            context = '...' + context;
        }
        if (end < content.length) {
            context = context + '...';
        }
        
        return context;
    }
    
    // Прокрутка к первому результату поиска
    scrollToSearchResult(query) {
        const results = this.searchMessages(query);
        
        if (results.length > 0) {
            this.scrollToItem(results[0].index, true);
            return results;
        }
        
        console.log('[VirtualList] Совпадений не найдено');
        return [];
    }
    
    // Проверка, находится ли пользователь в конце списка
    isNearBottom(threshold = 100) {
        const scrollTop = this.container.scrollTop;
        const scrollHeight = this.container.scrollHeight;
        const clientHeight = this.container.clientHeight;
        
        const distanceFromBottom = scrollHeight - (scrollTop + clientHeight);
        return distanceFromBottom <= threshold;
    }
    
    // Добавление нового элемента с автопрокруткой
    appendItem(item, autoScroll = true) {
        // Проверка позиции прокрутки перед добавлением
        const shouldScroll = autoScroll && this.isNearBottom(100);
        
        // Добавление элемента в список
        this.items.push(item);
        
        // Обновление видимого диапазона и рендеринг
        this.calculateVisibleRange();
        this.render();
        
        // Автопрокрутка к новому сообщению если пользователь был в конце
        if (shouldScroll) {
            this.scrollToBottom();
            console.log('[VirtualList] Автопрокрутка к новому сообщению');
        } else {
            console.log('[VirtualList] Автопрокрутка пропущена - пользователь читает старые сообщения');
        }
    }
    
    // Прокрутка к концу списка
    scrollToBottom(smooth = true) {
        this.container.scrollTo({
            top: this.container.scrollHeight,
            behavior: smooth ? 'smooth' : 'auto'
        });
    }
    
    // Добавление нескольких элементов
    appendItems(items, autoScroll = true) {
        if (!items || items.length === 0) {
            return;
        }
        
        // Проверка позиции прокрутки перед добавлением
        const shouldScroll = autoScroll && this.isNearBottom(100);
        
        // Добавление элементов в список
        this.items.push(...items);
        
        // Обновление видимого диапазона и рендеринг
        this.calculateVisibleRange();
        this.render();
        
        // Автопрокрутка если пользователь был в конце
        if (shouldScroll) {
            this.scrollToBottom();
            console.log(`[VirtualList] Автопрокрутка после добавления ${items.length} сообщений`);
        }
    }
}

// DraftManager для автосохранения черновиков сообщений
class DraftManager {
    constructor() {
        this.draftPrefix = 'draft_';
        this.saveDelay = 2000; // 2 секунды
        this.saveTimeout = null;
        this.ttl = 7 * 24 * 60 * 60 * 1000; // 7 дней в миллисекундах
        this.isAvailable = this.checkLocalStorageAvailability();
        
        if (!this.isAvailable) {
            console.warn('[DraftManager] localStorage недоступен, автосохранение черновиков отключено');
        }
    }
    
    // Проверка доступности localStorage
    checkLocalStorageAvailability() {
        try {
            const test = '__localStorage_test__';
            localStorage.setItem(test, test);
            localStorage.removeItem(test);
            return true;
        } catch (e) {
            return false;
        }
    }
    
    // Сохранение черновика с debouncing (2 секунды после последнего изменения)
    saveDraft(dialogueId, content) {
        if (!this.isAvailable) {
            return;
        }
        
        // Проверка на пустой контент (не сохраняем пустые черновики)
        if (!content || content.trim() === '') {
            console.log('[DraftManager] Пустой контент, черновик не сохраняется');
            return;
        }
        
        // Отмена предыдущего таймера если он есть
        if (this.saveTimeout) {
            clearTimeout(this.saveTimeout);
        }
        
        // Установка нового таймера на 2 секунды
        this.saveTimeout = setTimeout(() => {
            this.saveDraftImmediate(dialogueId, content);
        }, this.saveDelay);
        
        console.log(`[DraftManager] Запланировано сохранение черновика для диалога ${dialogueId} через ${this.saveDelay}ms`);
    }
    
    // Немедленное сохранение черновика (без debouncing)
    saveDraftImmediate(dialogueId, content) {
        if (!this.isAvailable) {
            return;
        }
        
        // Проверка на пустой контент
        if (!content || content.trim() === '') {
            console.log('[DraftManager] Пустой контент, черновик не сохраняется');
            return;
        }
        
        try {
            const key = this.draftPrefix + dialogueId;
            const data = {
                dialogueId: dialogueId,
                content: content,
                savedAt: new Date().toISOString()
            };
            
            localStorage.setItem(key, JSON.stringify(data));
            console.log(`[DraftManager] Черновик сохранен для диалога ${dialogueId} (${content.length} символов)`);
            
        } catch (error) {
            if (error.name === 'QuotaExceededError') {
                console.warn('[DraftManager] Превышена квота localStorage, очищаем устаревшие черновики');
                this.cleanExpiredDrafts();
                
                // Повторная попытка после очистки
                try {
                    localStorage.setItem(key, JSON.stringify(data));
                    console.log('[DraftManager] Повторное сохранение черновика успешно после очистки');
                } catch (e2) {
                    console.error('[DraftManager] Не удалось сохранить черновик после очистки:', e2);
                }
            } else {
                console.error('[DraftManager] Ошибка сохранения черновика:', error);
            }
        }
    }
    
    // Загрузка черновика для диалога
    loadDraft(dialogueId) {
        if (!this.isAvailable) {
            return null;
        }
        
        try {
            const key = this.draftPrefix + dialogueId;
            const cached = localStorage.getItem(key);
            
            if (!cached) {
                return null;
            }
            
            const data = JSON.parse(cached);
            
            // Проверка срока действия черновика (7 дней)
            const savedAt = new Date(data.savedAt);
            const now = new Date();
            const age = now - savedAt;
            
            if (age > this.ttl) {
                console.log(`[DraftManager] Черновик для диалога ${dialogueId} устарел, удаляем`);
                localStorage.removeItem(key);
                return null;
            }
            
            console.log(`[DraftManager] Загружен черновик для диалога ${dialogueId} (${data.content.length} символов)`);
            return data.content;
            
        } catch (error) {
            console.error('[DraftManager] Ошибка загрузки черновика:', error);
            return null;
        }
    }
    
    // Удаление черновика для диалога
    clearDraft(dialogueId) {
        if (!this.isAvailable) {
            return;
        }
        
        try {
            const key = this.draftPrefix + dialogueId;
            localStorage.removeItem(key);
            console.log(`[DraftManager] Черновик удален для диалога ${dialogueId}`);
            
        } catch (error) {
            console.error('[DraftManager] Ошибка удаления черновика:', error);
        }
    }
    
    // Очистка устаревших черновиков (старше 7 дней)
    cleanExpiredDrafts() {
        if (!this.isAvailable) {
            return;
        }
        
        try {
            const now = new Date();
            let cleanedCount = 0;
            
            // Перебор всех ключей в localStorage
            for (let i = localStorage.length - 1; i >= 0; i--) {
                const key = localStorage.key(i);
                
                // Проверка, что это ключ черновика
                if (key && key.startsWith(this.draftPrefix)) {
                    try {
                        const cached = localStorage.getItem(key);
                        if (cached) {
                            const data = JSON.parse(cached);
                            const savedAt = new Date(data.savedAt);
                            const age = now - savedAt;
                            
                            // Удаление если старше TTL (7 дней)
                            if (age > this.ttl) {
                                localStorage.removeItem(key);
                                cleanedCount++;
                            }
                        }
                    } catch (error) {
                        // Удаление поврежденных записей
                        console.warn(`[DraftManager] Удаление поврежденной записи: ${key}`);
                        localStorage.removeItem(key);
                        cleanedCount++;
                    }
                }
            }
            
            if (cleanedCount > 0) {
                console.log(`[DraftManager] Очищено ${cleanedCount} устаревших черновиков`);
            }
            
        } catch (error) {
            console.error('[DraftManager] Ошибка очистки устаревших черновиков:', error);
        }
    }
}

// Глобальные переменные для управления агентским режимом выполнения
let executionPollingInterval = null;
let currentExecutionStatus = 'none';
let currentSearchResults = []; // Результаты поиска
let currentSearchIndex = 0; // Текущий индекс в результатах поиска

// ============================================
// Функции режима отладки и мониторинга
// ============================================

// Создание панели метрик для режима отладки
function createDebugMetricsPanel() {
    // Проверка, не создана ли уже панель
    if (document.getElementById('debug-metrics-panel')) {
        return;
    }
    
    const panel = document.createElement('div');
    panel.id = 'debug-metrics-panel';
    panel.style.position = 'fixed';
    panel.style.bottom = '10px';
    panel.style.right = '10px';
    panel.style.background = 'rgba(0, 0, 0, 0.85)';
    panel.style.color = '#fff';
    panel.style.padding = '15px';
    panel.style.borderRadius = '8px';
    panel.style.fontSize = '12px';
    panel.style.fontFamily = 'monospace';
    panel.style.zIndex = '10000';
    panel.style.minWidth = '250px';
    panel.style.boxShadow = '0 4px 6px rgba(0, 0, 0, 0.3)';
    
    panel.innerHTML = `
        <div style="font-weight: bold; margin-bottom: 10px; font-size: 14px; border-bottom: 1px solid #444; padding-bottom: 5px;">
            📊 Метрики производительности
        </div>
        <div id="debug-metrics-content">
            <div>Загрузка метрик...</div>
        </div>
        <div style="margin-top: 10px; padding-top: 10px; border-top: 1px solid #444;">
            <button id="toggle-debug-mode" style="width: 100%; padding: 5px; background: #444; color: #fff; border: none; border-radius: 4px; cursor: pointer;">
                Отключить режим отладки
            </button>
        </div>
    `;
    
    document.body.appendChild(panel);
    
    // Обработчик кнопки переключения режима отладки
    document.getElementById('toggle-debug-mode').addEventListener('click', toggleDebugMode);
    
    console.log('[Debug] Панель метрик создана');
}

// Удаление панели метрик
function removeDebugMetricsPanel() {
    const panel = document.getElementById('debug-metrics-panel');
    if (panel) {
        panel.remove();
        console.log('[Debug] Панель метрик удалена');
    }
    
    // Удаление FPS дисплея
    const fpsDisplay = document.getElementById('fps-display');
    if (fpsDisplay) {
        fpsDisplay.remove();
    }
}

// Обновление панели метрик
function updateDebugMetricsPanel() {
    if (!window.DEBUG_MODE) {
        return;
    }
    
    const content = document.getElementById('debug-metrics-content');
    if (!content) {
        return;
    }
    
    const metrics = window.performanceMetrics;
    
    // Форматирование метрик
    const ttfbText = metrics.ttfb !== null ? `${metrics.ttfb.toFixed(2)} ms` : 'N/A';
    const fpsText = metrics.fps !== null ? `${metrics.fps.toFixed(1)} FPS` : 'N/A';
    const cacheSizeText = `${(metrics.cacheSize / 1024 / 1024).toFixed(2)} MB`;
    const cacheMessagesText = `${metrics.cacheMessages} сообщений`;
    const streamingTimeText = metrics.lastStreamingTime !== null ? `${metrics.lastStreamingTime.toFixed(2)} ms` : 'N/A';
    
    // Цветовая индикация статуса соединения
    let connectionColor = '#888';
    let connectionText = metrics.connectionStatus;
    switch (metrics.connectionStatus) {
        case 'connected':
            connectionColor = '#00ff00';
            connectionText = 'WebSocket подключен';
            break;
        case 'disconnected':
            connectionColor = '#ff0000';
            connectionText = 'Отключено';
            break;
        case 'reconnecting':
            connectionColor = '#ffaa00';
            connectionText = 'Переподключение...';
            break;
        case 'http_fallback':
            connectionColor = '#ffaa00';
            connectionText = 'HTTP режим';
            break;
    }
    
    // Цветовая индикация FPS
    let fpsColor = '#00ff00';
    if (metrics.fps !== null) {
        if (metrics.fps < 30) {
            fpsColor = '#ff0000';
        } else if (metrics.fps < 50) {
            fpsColor = '#ffaa00';
        }
    }
    
    // Цветовая индикация TTFB
    let ttfbColor = '#00ff00';
    if (metrics.ttfb !== null) {
        if (metrics.ttfb > 1000) {
            ttfbColor = '#ff0000';
        } else if (metrics.ttfb > 500) {
            ttfbColor = '#ffaa00';
        }
    }
    
    content.innerHTML = `
        <div style="margin-bottom: 8px;">
            <span style="color: #aaa;">Статус соединения:</span><br>
            <span style="color: ${connectionColor}; font-weight: bold;">● ${connectionText}</span>
        </div>
        <div style="margin-bottom: 8px;">
            <span style="color: #aaa;">TTFB (Time To First Byte):</span><br>
            <span style="color: ${ttfbColor}; font-weight: bold;">${ttfbText}</span>
        </div>
        <div style="margin-bottom: 8px;">
            <span style="color: #aaa;">FPS прокрутки:</span><br>
            <span style="color: ${fpsColor}; font-weight: bold;">${fpsText}</span>
        </div>
        <div style="margin-bottom: 8px;">
            <span style="color: #aaa;">Размер кэша:</span><br>
            <span style="color: #fff; font-weight: bold;">${cacheSizeText}</span>
        </div>
        <div style="margin-bottom: 8px;">
            <span style="color: #aaa;">Сообщений в кэше:</span><br>
            <span style="color: #fff; font-weight: bold;">${cacheMessagesText}</span>
        </div>
        <div style="margin-bottom: 8px;">
            <span style="color: #aaa;">Время последнего streaming:</span><br>
            <span style="color: #fff; font-weight: bold;">${streamingTimeText}</span>
        </div>
    `;
}

// Переключение режима отладки
function toggleDebugMode() {
    window.DEBUG_MODE = !window.DEBUG_MODE;
    
    if (window.DEBUG_MODE) {
        console.log('[Debug] Режим отладки включен');
        createDebugMetricsPanel();
        updateDebugMetricsPanel();
        
        // Запуск периодического обновления метрик
        if (!window.debugMetricsInterval) {
            window.debugMetricsInterval = setInterval(updateDebugMetricsPanel, 1000);
        }
    } else {
        console.log('[Debug] Режим отладки отключен');
        removeDebugMetricsPanel();
        
        // Остановка периодического обновления
        if (window.debugMetricsInterval) {
            clearInterval(window.debugMetricsInterval);
            window.debugMetricsInterval = null;
        }
    }
}

// Обновление метрик производительности
function updatePerformanceMetric(metricName, value) {
    if (window.performanceMetrics) {
        window.performanceMetrics[metricName] = value;
        
        // Обновление панели если режим отладки включен
        if (window.DEBUG_MODE) {
            updateDebugMetricsPanel();
        }
    }
}

// Initialize app
document.addEventListener('DOMContentLoaded', () => {
    // Инициализация MessageCache
    messageCache = new MessageCache();
    messageCache.cleanExpiredCache();
    console.log('[App] MessageCache инициализирован');
    
    // Инициализация DraftManager
    draftManager = new DraftManager();
    draftManager.cleanExpiredDrafts();
    console.log('[App] DraftManager инициализирован');
    
    // Инициализация обработчиков поиска
    initializeSearchHandlers();
    
    // Инициализация индикатора статуса соединения
    updateConnectionStatus('disconnected');
    console.log('[App] Индикатор статуса соединения инициализирован');
    
    validateModelConnection();
    loadProjects();
    loadDialogues();
    setupEventListeners();
});

// Project Management Functions

async function loadProjects() {
    try {
        const response = await fetch(`${API_BASE}/api/projects`);
        projects = await response.json();
        
        updateProjectSelector();
    } catch (error) {
        console.error('Error loading projects:', error);
    }
}

function updateProjectSelector() {
    const selector = document.getElementById('project-selector');
    
    if (!selector) {
        console.warn('[Projects] Селектор проектов не найден');
        return;
    }
    
    if (projects.length === 0) {
        selector.innerHTML = '<option value="">Нет проектов</option>';
        return;
    }
    
    // Находим выбранный проект
    const selectedProject = projects.find(p => p.isSelected);
    
    selector.innerHTML = projects.map(p => `
        <option value="${p.id}" ${p.isSelected ? 'selected' : ''}>
            ${escapeHtml(p.name)}
        </option>
    `).join('');
    
    console.log(`[Projects] Селектор обновлен. Выбран проект: ${selectedProject ? selectedProject.name : 'нет'}`);
}

async function selectProject(projectId) {
    if (!projectId) return;
    
    try {
        const response = await fetch(`${API_BASE}/api/projects/${projectId}/select`, {
            method: 'POST'
        });
        
        if (!response.ok) {
            throw new Error('Не удалось выбрать проект');
        }
        
        // Обновляем локальный массив проектов
        projects.forEach(p => {
            p.isSelected = (p.id == projectId);
        });
        
        console.log(`[Projects] Проект ${projectId} выбран`);
        
        // Перезагружаем проекты с сервера для синхронизации
        await loadProjects();
        
        // Перезагружаем диалоги для нового проекта
        await loadDialogues();
        
        // Очищаем текущий диалог, если он не относится к выбранному проекту
        if (currentDialogueId) {
            const currentDialogue = dialogues.find(d => d.id === currentDialogueId);
            if (!currentDialogue) {
                // Текущий диалог не относится к выбранному проекту
                currentDialogueId = null;
                document.getElementById('message-list').innerHTML = '';
                document.getElementById('prompt-input').disabled = true;
                document.getElementById('send-button').disabled = true;
            }
        }
    } catch (error) {
        console.error('Error selecting project:', error);
        alert('Ошибка выбора проекта: ' + error.message);
    }
}

function openProjectModal() {
    const modal = document.getElementById('project-modal-overlay');
    if (!modal) {
        console.error('Project modal overlay not found');
        return;
    }
    modal.classList.add('active');
    renderProjectList();
}

function closeProjectModal() {
    const modal = document.getElementById('project-modal-overlay');
    if (!modal) {
        console.error('Project modal overlay not found');
        return;
    }
    modal.classList.remove('active');
}

function renderProjectList() {
    const listElement = document.getElementById('modal-project-list');
    
    if (!listElement) {
        console.error('Modal project list element not found');
        return;
    }
    
    if (projects.length === 0) {
        listElement.innerHTML = '<div class="empty-state">Нет проектов</div>';
        return;
    }
    
    listElement.innerHTML = projects.map(p => `
        <div class="project-list-item">
            <span class="project-name">${escapeHtml(p.name)}</span>
            <button class="delete-project-btn" onclick="deleteProject(${p.id})">
                🗑️
            </button>
        </div>
    `).join('');
}

async function addProject() {
    const path = prompt('Введите путь к проекту:');
    
    if (!path) return;
    
    try {
        const response = await fetch(`${API_BASE}/api/projects`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ projectPath: path })
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Ошибка добавления проекта');
        }
        
        await loadProjects();
        renderProjectList();
    } catch (error) {
        alert('Ошибка добавления проекта: ' + error.message);
    }
}

async function deleteProject(projectId) {
    if (!confirm('Удалить проект из списка?')) {
        return;
    }
    
    try {
        await fetch(`${API_BASE}/api/projects/${projectId}`, {
            method: 'DELETE'
        });
        
        await loadProjects();
        renderProjectList();
    } catch (error) {
        alert('Ошибка удаления проекта: ' + error.message);
    }
}

function setupEventListeners() {
    document.getElementById('create-dialogue-button').addEventListener('click', createDialogue);
    document.getElementById('send-button').addEventListener('click', sendMessage);
    
    // Обработчик кнопки запуска модели
    const startModelButton = document.getElementById('start-model-button');
    if (startModelButton) {
        startModelButton.addEventListener('click', startOllamaModel);
    }
    
    const promptInput = document.getElementById('prompt-input');
    
    promptInput.addEventListener('keypress', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });
    
    // Автосохранение черновика при вводе текста (с debouncing)
    promptInput.addEventListener('input', (e) => {
        if (currentDialogueId && draftManager) {
            const content = e.target.value;
            draftManager.saveDraft(currentDialogueId, content);
        }
    });
    
    // Настройка кнопок управления выполнением
    setupExecutionControlButtons();
    
    // Настройка кнопки остановки генерации
    const cancelGenerationBtn = document.getElementById('cancel-generation-btn');
    if (cancelGenerationBtn) {
        cancelGenerationBtn.addEventListener('click', cancelGeneration);
    }
    
    // Model configuration modal
    document.getElementById('open-config-button').addEventListener('click', openConfigModal);
    document.getElementById('close-modal').addEventListener('click', closeConfigModal);
    document.getElementById('model-config-overlay').addEventListener('click', (e) => {
        if (e.target.id === 'model-config-overlay') {
            closeConfigModal();
        }
    });
    
    // Project management modal
    const projectModalOverlay = document.getElementById('project-modal-overlay');
    if (projectModalOverlay) {
        projectModalOverlay.addEventListener('click', (e) => {
            if (e.target.id === 'project-modal-overlay') {
                closeProjectModal();
            }
        });
    }
    
    // Tab switching
    document.querySelectorAll('.tab-button').forEach(button => {
        button.addEventListener('click', () => switchTab(button.dataset.tab));
    });
    
    // Save configuration buttons
    document.getElementById('save-provider').addEventListener('click', saveProviderConfiguration);
    document.getElementById('save-local').addEventListener('click', saveLocalConfiguration);
    
    // Refresh models button
    document.getElementById('refresh-models').addEventListener('click', refreshOllamaModels);
    
    // Test connection buttons
    document.getElementById('test-provider').addEventListener('click', testProviderConnection);
    document.getElementById('test-local').addEventListener('click', testLocalConnection);
    
    // Real-time validation - clear errors when user starts typing
    document.getElementById('provider-base-url').addEventListener('input', () => clearFieldError('provider-base-url'));
    document.getElementById('provider-api-key').addEventListener('input', () => clearFieldError('provider-api-key'));
    document.getElementById('provider-model').addEventListener('input', () => clearFieldError('provider-model'));
    document.getElementById('ollama-base-url').addEventListener('input', () => clearFieldError('ollama-base-url'));
    document.getElementById('ollama-model').addEventListener('change', () => clearFieldError('ollama-model'));
    
    // Close modal on Escape key
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            closeConfigModal();
        }
    });
}

async function loadDialogues() {
    try {
        const response = await fetch(`${API_BASE}/api/dialogues`);
        const dialogues = await response.json();
        
        const listElement = document.getElementById('dialogue-list');
        
        if (dialogues.length === 0) {
            listElement.innerHTML = '<div class="empty-state">Нет диалогов. Создайте новый.</div>';
            return;
        }
        
        listElement.innerHTML = dialogues.map(d => `
            <div class="dialogue-item" data-id="${d.id}">
                <div class="dialogue-info" onclick="selectDialogue(${d.id})">
                    <div>Диалог #${d.id}</div>
                    <div class="dialogue-path">${d.projectPath}</div>
                </div>
                <button class="dialogue-delete" onclick="deleteDialogue(event, ${d.id})" title="Удалить диалог">
                    🗑️
                </button>
            </div>
        `).join('');
        
        if (dialogues.length > 0 && !currentDialogueId) {
            selectDialogue(dialogues[0].id);
        }
    } catch (error) {
        console.error('Error loading dialogues:', error);
        showError('Ошибка загрузки диалогов');
    }
}

async function createDialogue() {
    // Получаем выбранный проект
    const selectedProject = projects.find(p => p.isSelected);
    
    if (!selectedProject) {
        alert('Выберите проект из списка');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogues`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ projectPath: selectedProject.path })
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        const dialogue = await response.json();
        
        await loadDialogues();
        selectDialogue(dialogue.id);
    } catch (error) {
        console.error('Error creating dialogue:', error);
        alert('Ошибка создания диалога: ' + error.message);
    }
}

async function deleteDialogue(event, dialogueId) {
    // Prevent event from bubbling to parent (which would select the dialogue)
    event.stopPropagation();
    
    if (!confirm('Вы уверены, что хотите удалить этот диалог? Это действие нельзя отменить.')) {
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogues/${dialogueId}`, {
            method: 'DELETE'
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        // If the deleted dialogue was selected, clear the current selection
        if (currentDialogueId === dialogueId) {
            currentDialogueId = null;
            document.getElementById('message-list').innerHTML = '<div class="empty-state">Выберите диалог</div>';
            document.getElementById('checkpoint-list').innerHTML = '<div class="empty-state">Нет чекпоинтов</div>';
        }
        
        // Reload the dialogue list
        await loadDialogues();
    } catch (error) {
        console.error('Error deleting dialogue:', error);
        alert('Ошибка удаления диалога: ' + error.message);
    }
}

async function selectDialogue(dialogueId) {
    // Сохранение черновика текущего диалога перед переключением
    if (currentDialogueId && draftManager) {
        const promptInput = document.getElementById('prompt-input');
        if (promptInput && promptInput.value.trim()) {
            draftManager.saveDraftImmediate(currentDialogueId, promptInput.value);
            console.log('[UI] Черновик текущего диалога сохранен перед переключением');
        }
    }
    
    // Остановка polling предыдущего диалога
    stopPollingExecutionStatus();
    
    // Отключение предыдущего WebSocket соединения
    if (wsClient) {
        wsClient.disconnect();
        wsClient = null;
    }
    
    currentDialogueId = dialogueId;
    
    // Update active state
    document.querySelectorAll('.dialogue-item').forEach(item => {
        item.classList.toggle('active', item.dataset.id == dialogueId);
    });
    
    // Показать панель поиска при выборе диалога
    const searchContainer = document.getElementById('message-search-container');
    if (searchContainer) {
        searchContainer.style.display = 'flex';
    }
    
    await loadMessages(dialogueId);
    await loadCheckpoints(dialogueId);
    
    // Загрузка черновика для нового диалога
    if (draftManager) {
        const draft = draftManager.loadDraft(dialogueId);
        const promptInput = document.getElementById('prompt-input');
        if (promptInput) {
            promptInput.value = draft || '';
            if (draft) {
                console.log('[UI] Черновик загружен для нового диалога');
            }
        }
    }
    
    // Инициализация WebSocketClient для нового диалога
    wsClient = new WebSocketClient(dialogueId);
    
    // Установка обработчика изменения статуса соединения
    wsClient.onConnectionChange = (status, data) => {
        console.log(`[UI] Статус WebSocket соединения изменен: ${status}`, data);
        updateConnectionStatus(status, data);
    };
    
    // Регистрация обработчиков входящих сообщений
    registerWebSocketHandlers(wsClient);
    
    // Установка WebSocket соединения
    await wsClient.connect();
    
    // Проверка статуса выполнения нового диалога
    try {
        const response = await fetch(
            `${API_BASE}/api/dialogues/${dialogueId}/execution-status`
        );
        
        if (response.ok) {
            const status = await response.json();
            updateExecutionUI(status);
            
            // Запуск polling если выполнение активно
            if (status.status === 'running') {
                startPollingExecutionStatus();
            }
        }
    } catch (error) {
        console.error('Error checking execution status:', error);
    }
}

// Глобальная переменная для отслеживания времени streaming
let streamingStartTime = null;
let lastChunkTime = null;
let isStreamingActive = false;

// Функция отмены генерации
async function cancelGeneration() {
    if (!wsClient || !wsClient.isConnected) {
        console.warn('[UI] WebSocket не подключен, невозможно отменить генерацию');
        return;
    }
    
    try {
        console.log('[UI] Отправка команды отмены генерации');
        
        // Отправка сообщения cancel_generation через WebSocket
        const sent = await wsClient.sendMessage('cancel_generation', {
            dialogueId: currentDialogueId
        });
        
        if (sent) {
            console.log('[UI] Команда отмены отправлена успешно');
            
            // Скрытие кнопки остановки
            const streamingControls = document.getElementById('streaming-controls');
            if (streamingControls) {
                streamingControls.style.display = 'none';
            }
            
            // Сброс флага streaming
            isStreamingActive = false;
        } else {
            console.error('[UI] Не удалось отправить команду отмены');
            showStatusMessage('Не удалось отменить генерацию', 'error');
        }
    } catch (error) {
        console.error('[UI] Ошибка при отмене генерации:', error);
        showStatusMessage('Ошибка отмены генерации: ' + error.message, 'error');
    }
}

// Регистрация обработчиков WebSocket сообщений
function registerWebSocketHandlers(client) {
    // Обработчик подтверждения подключения
    client.on('connection_ack', (payload) => {
        console.log('[UI] Получено connection_ack:', payload);
        updateConnectionStatus('connected');
    });
    
    // Обработчик начала генерации ответа ассистента
    client.on('assistant_message_start', (payload) => {
        console.log('[UI] Получено assistant_message_start:', payload);
        
        // Запуск измерения времени для мониторинга
        streamingStartTime = performance.now();
        lastChunkTime = streamingStartTime;
        isStreamingActive = true;
        
        // Показ кнопки остановки генерации
        const streamingControls = document.getElementById('streaming-controls');
        if (streamingControls) {
            streamingControls.style.display = 'flex';
        }
        
        // Удаление индикатора "печатает..." если он есть
        const typingIndicator = document.getElementById('typing-indicator');
        if (typingIndicator) {
            typingIndicator.remove();
        }
        
        // Создание пустого сообщения ассистента для streaming
        const messageList = document.getElementById('message-list');
        
        // Используем фиксированный ID для streaming сообщения
        const streamingId = 'streaming-message-active';
        
        // Если есть старый streaming элемент, удаляем у него ID и класс (превращаем в обычное сообщение)
        const oldStreamingElement = document.getElementById(streamingId);
        if (oldStreamingElement) {
            console.log('[UI] Финализация старого streaming элемента');
            oldStreamingElement.classList.remove('streaming');
            oldStreamingElement.removeAttribute('id');
        }
        
        // Создаем новый streaming элемент с индикатором "печатает..."
        const assistantMessageElement = createMessageElement('assistant', '');
        assistantMessageElement.id = streamingId;
        assistantMessageElement.classList.add('streaming');
        
        // Добавляем анимацию "печатает..." в контент
        const contentDiv = assistantMessageElement.querySelector('.message-content');
        if (contentDiv) {
            contentDiv.innerHTML = '<span class="typing-dots"><span>.</span><span>.</span><span>.</span></span>';
        }
        
        messageList.appendChild(assistantMessageElement);
        messageList.scrollTop = messageList.scrollHeight;
        console.log('[UI] Создан новый streaming элемент с индикатором печатания:', streamingId);
        
        console.log('[Monitoring] Streaming начат');
    });
    
    // Обработчик фрагмента сообщения
    client.on('assistant_message_chunk', (payload) => {
        console.log('[UI] Получен assistant_message_chunk:', payload);
        
        // Измерение времени добавления фрагмента для мониторинга
        const currentTime = performance.now();
        
        // Измерение TTFB (Time To First Byte) - время до получения первого фрагмента
        if (streamingStartTime && lastChunkTime === streamingStartTime) {
            const ttfb = currentTime - streamingStartTime;
            console.log(`[Monitoring] TTFB (Time To First Byte): ${ttfb.toFixed(2)}ms`);
            
            // Обновление метрики TTFB
            updatePerformanceMetric('ttfb', ttfb);
            
            // Логирование предупреждения если TTFB превышает 1 секунду
            if (ttfb > 1000) {
                console.warn(`[Monitoring] Высокий TTFB: ${ttfb.toFixed(2)}ms (>1000ms)`);
            }
        }
        
        const timeSinceLastChunk = lastChunkTime ? currentTime - lastChunkTime : 0;
        lastChunkTime = currentTime;
        
        // Логирование если время превышает 100ms (для мониторинга производительности)
        if (timeSinceLastChunk > 100) {
            console.warn(`[Monitoring] Задержка между фрагментами: ${timeSinceLastChunk.toFixed(2)}ms`);
        }
        
        // Поддержка как camelCase, так и PascalCase
        const content = payload.content || payload.Content || '';
        
        // Используем фиксированный ID для streaming сообщения
        const streamingId = 'streaming-message-active';
        
        // Поиск streaming сообщения
        let streamingMessage = document.getElementById(streamingId);
        
        // Если streaming сообщение не найдено, создаем его
        if (!streamingMessage) {
            console.log('[UI] Создание streaming сообщения (из chunk):', streamingId);
            
            // Удаление индикатора "печатает..." если он есть
            const typingIndicator = document.getElementById('typing-indicator');
            if (typingIndicator) {
                typingIndicator.remove();
            }
            
            const messageList = document.getElementById('message-list');
            streamingMessage = createMessageElement('assistant', '');
            streamingMessage.id = streamingId;
            streamingMessage.classList.add('streaming');
            messageList.appendChild(streamingMessage);
            console.log('[UI] Streaming элемент создан (из chunk)');
        }
        
        // Добавление фрагмента к содержимому сообщения
        const contentDiv = streamingMessage.querySelector('.message-content');
        if (contentDiv) {
            const startTime = performance.now();
            
            // Если это первый фрагмент и есть индикатор "печатает...", очищаем контент
            if (contentDiv.querySelector('.typing-dots')) {
                contentDiv.textContent = '';
            }
            
            contentDiv.textContent += content;
            const renderTime = performance.now() - startTime;
            
            // Логирование времени рендеринга для мониторинга
            if (renderTime > 50) {
                console.warn(`[Monitoring] Время рендеринга фрагмента: ${renderTime.toFixed(2)}ms`);
            }
            
            // Автопрокрутка к новому контенту
            const messageList = document.getElementById('message-list');
            messageList.scrollTop = messageList.scrollHeight;
        } else {
            console.error('[UI] Не найден .message-content в streaming элементе!');
        }
    });
    
    // Обработчик завершения генерации ответа
    client.on('assistant_message_end', (payload) => {
        console.log('[UI] Получено assistant_message_end:', payload);
        
        // Измерение общего времени streaming для мониторинга
        if (streamingStartTime) {
            const totalTime = performance.now() - streamingStartTime;
            console.log(`[Monitoring] Общее время streaming: ${totalTime.toFixed(2)}ms`);
            
            // Обновление метрики времени streaming
            updatePerformanceMetric('lastStreamingTime', totalTime);
            
            streamingStartTime = null;
            lastChunkTime = null;
        }
        
        // Скрытие кнопки остановки генерации
        const streamingControls = document.getElementById('streaming-controls');
        if (streamingControls) {
            streamingControls.style.display = 'none';
        }
        
        // Сброс флага streaming
        isStreamingActive = false;
        
        // Используем фиксированный ID для streaming сообщения
        const streamingId = 'streaming-message-active';
        
        // Поиск streaming сообщения
        const streamingMessage = document.getElementById(streamingId);
        
        if (streamingMessage) {
            // Удаляем только класс streaming, но оставляем ID для следующего использования
            streamingMessage.classList.remove('streaming');
            console.log('[UI] Streaming завершен, класс удален');
            
            // Обновление содержимого финальным текстом (если передан)
            const content = payload.content || payload.Content;
            if (content) {
                const contentDiv = streamingMessage.querySelector('.message-content');
                if (contentDiv) {
                    contentDiv.textContent = content;
                }
            }
        } else {
            console.warn('[UI] Streaming элемент не найден при завершении!');
        }
        
        // Загрузка чекпоинтов после завершения генерации
        loadCheckpoints(currentDialogueId).catch(err => 
            console.error('Error loading checkpoints:', err)
        );
        
        // Сброс флага обработки
        isProcessing = false;
        const sendButton = document.getElementById('send-button');
        sendButton.disabled = false;
        sendButton.textContent = 'Отправить';
    });
    
    // Обработчик ошибок
    client.on('error', (payload) => {
        console.error('[UI] Получена ошибка от WebSocket:', payload);
        
        // Скрытие кнопки остановки генерации
        const streamingControls = document.getElementById('streaming-controls');
        if (streamingControls) {
            streamingControls.style.display = 'none';
        }
        
        // Сброс флага streaming
        isStreamingActive = false;
        
        // Удаление индикатора "печатает..."
        const typingIndicator = document.getElementById('typing-indicator');
        if (typingIndicator) {
            typingIndicator.remove();
        }
        
        const messageList = document.getElementById('message-list');
        const errorMessage = payload.message || 'Произошла ошибка при генерации ответа';
        
        // Если есть частичный ответ, показываем его
        if (payload.partialResponse && payload.partialResponse.trim()) {
            // Удаление streaming сообщения если есть
            const streamingMessages = document.querySelectorAll('.message.streaming');
            streamingMessages.forEach(msg => msg.remove());
            
            // Создание сообщения с частичным ответом
            const partialMessageElement = createMessageElement('assistant', payload.partialResponse);
            partialMessageElement.classList.add('partial');
            
            // Добавление заголовка о частичном ответе
            const partialHeader = document.createElement('div');
            partialHeader.style.fontSize = '12px';
            partialHeader.style.color = '#856404';
            partialHeader.style.marginBottom = '8px';
            partialHeader.style.fontWeight = 'bold';
            partialHeader.textContent = '⚠️ Частичный ответ (генерация прервана):';
            partialMessageElement.insertBefore(partialHeader, partialMessageElement.firstChild);
            
            messageList.appendChild(partialMessageElement);
        } else {
            // Удаление streaming сообщения если нет частичного ответа
            const streamingMessages = document.querySelectorAll('.message.streaming');
            streamingMessages.forEach(msg => msg.remove());
        }
        
        // Показываем сообщение об ошибке
        const errorMessageElement = createMessageElement('assistant', `❌ Ошибка: ${errorMessage}`);
        errorMessageElement.classList.add('error');
        
        // Добавление кнопки повтора запроса
        const retryButton = document.createElement('button');
        retryButton.textContent = '🔄 Повторить запрос';
        retryButton.style.marginTop = '10px';
        retryButton.style.padding = '8px 16px';
        retryButton.style.background = '#007bff';
        retryButton.style.color = 'white';
        retryButton.style.border = 'none';
        retryButton.style.borderRadius = '4px';
        retryButton.style.cursor = 'pointer';
        retryButton.style.fontSize = '14px';
        retryButton.style.fontWeight = '500';
        
        retryButton.addEventListener('click', () => {
            // Получение последнего сообщения пользователя
            const messages = document.querySelectorAll('.message.user');
            if (messages.length > 0) {
                const lastUserMessage = messages[messages.length - 1];
                const lastContent = lastUserMessage.querySelector('.message-content');
                if (lastContent) {
                    const input = document.getElementById('prompt-input');
                    input.value = lastContent.textContent;
                    sendMessage();
                }
            }
        });
        
        retryButton.addEventListener('mouseenter', () => {
            retryButton.style.background = '#0056b3';
        });
        
        retryButton.addEventListener('mouseleave', () => {
            retryButton.style.background = '#007bff';
        });
        
        errorMessageElement.appendChild(retryButton);
        messageList.appendChild(errorMessageElement);
        messageList.scrollTop = messageList.scrollHeight;
        
        showStatusMessage('Ошибка генерации ответа: ' + errorMessage, 'error');
        
        // Сброс флага обработки
        isProcessing = false;
        const sendButton = document.getElementById('send-button');
        sendButton.disabled = false;
        sendButton.textContent = 'Отправить';
    });
}

async function loadMessages(dialogueId) {
    const messageList = document.getElementById('message-list');
    
    // ВАЖНО: Очищаем список сообщений при переключении диалога
    messageList.innerHTML = '';
    
    try {
        // Шаг 1: Загрузка и отображение кэшированных сообщений
        const cacheStartTime = performance.now();
        const cachedMessages = messageCache ? messageCache.getCachedMessages(dialogueId) : null;
        
        if (cachedMessages && cachedMessages.length > 0) {
            // Пересоздаем VirtualList для нового диалога
            // Вычисление высоты элемента сообщения динамически
            const tempMessage = document.createElement('div');
            tempMessage.className = 'message user';
            tempMessage.style.visibility = 'hidden';
            tempMessage.innerHTML = `
                <div class="message-role">Вы</div>
                <div class="message-content">Тестовое сообщение</div>
            `;
            messageList.appendChild(tempMessage);
            const itemHeight = tempMessage.offsetHeight + 20; // +20 для margin-bottom
            messageList.removeChild(tempMessage);
            
            virtualList = new VirtualList(messageList, itemHeight);
            virtualList.initScrollHandler();
            console.log(`[VirtualList] Инициализирован с высотой элемента ${itemHeight}px`);
            
            // Установка кэшированных сообщений в VirtualList
            virtualList.setItems(cachedMessages);
            
            const cacheDisplayTime = performance.now() - cacheStartTime;
            console.log(`[Monitoring] Кэшированные сообщения отображены за ${cacheDisplayTime.toFixed(2)}ms`);
        }
        
        // Шаг 2: Запрос обновлений с сервера в фоновом режиме
        const response = await fetch(`${API_BASE}/api/dialogues/${dialogueId}`);
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const dialogue = await response.json();
        
        // Шаг 3: Проверка наличия изменений
        if (!dialogue || !dialogue.messages) {
            if (!cachedMessages || cachedMessages.length === 0) {
                messageList.innerHTML = '<div class="empty-state">Нет сообщений. Начните диалог.</div>';
            }
            return;
        }
        
        // Проверка на изменения: сравнение количества и содержимого последнего сообщения
        let hasChanges = false;
        
        if (!cachedMessages || cachedMessages.length !== dialogue.messages.length) {
            hasChanges = true;
        } else if (dialogue.messages.length > 0) {
            const lastServerMessage = dialogue.messages[dialogue.messages.length - 1];
            const lastCachedMessage = cachedMessages[cachedMessages.length - 1];
            
            if (lastServerMessage.id !== lastCachedMessage.id || 
                lastServerMessage.content !== lastCachedMessage.content) {
                hasChanges = true;
            }
        }
        
        // Шаг 4: Обновление UI только если есть изменения
        if (hasChanges) {
            console.log('[MessageCache] Обнаружены изменения, обновляем UI');
            
            // Пересоздаем VirtualList для обновленных данных
            const tempMessage = document.createElement('div');
            tempMessage.className = 'message user';
            tempMessage.style.visibility = 'hidden';
            tempMessage.innerHTML = `
                <div class="message-role">Вы</div>
                <div class="message-content">Тестовое сообщение</div>
            `;
            messageList.appendChild(tempMessage);
            const itemHeight = tempMessage.offsetHeight + 20;
            messageList.removeChild(tempMessage);
            
            virtualList = new VirtualList(messageList, itemHeight);
            virtualList.initScrollHandler();
            console.log(`[VirtualList] Инициализирован с высотой элемента ${itemHeight}px`);
            
            // Установка обновленных сообщений в VirtualList
            virtualList.setItems(dialogue.messages);
            
            // Обновление кэша с новыми данными
            if (messageCache) {
                messageCache.cacheMessages(dialogueId, dialogue.messages);
            }
        } else {
            console.log('[MessageCache] Изменений не обнаружено, UI не обновляется');
        }
        
    } catch (error) {
        console.error('Error loading messages:', error);
        
        // Если есть кэшированные сообщения, оставляем их
        // Иначе показываем ошибку
        const cachedMessages = messageCache ? messageCache.getCachedMessages(dialogueId) : null;
        if (!cachedMessages || cachedMessages.length === 0) {
            messageList.innerHTML = '<div class="error">Ошибка загрузки сообщений. Попробуйте обновить страницу.</div>';
        } else {
            console.log('[MessageCache] Используем кэшированные сообщения из-за ошибки сети');
        }
    }
}

async function loadCheckpoints(dialogueId) {
    try {
        const response = await fetch(`${API_BASE}/api/dialogues/${dialogueId}/checkpoints`);
        const checkpoints = await response.json();
        
        const checkpointList = document.getElementById('checkpoint-list');
        
        if (checkpoints.length === 0) {
            checkpointList.innerHTML = '<div class="empty-state">Нет чекпоинтов</div>';
            return;
        }
        
        checkpointList.innerHTML = checkpoints.map(c => `
            <div class="checkpoint-item">
                <div class="checkpoint-description">${escapeHtml(c.description)}</div>
                <div class="checkpoint-date">${new Date(c.createdAt).toLocaleString('ru-RU')}</div>
                <button class="checkpoint-rollback" onclick="rollbackToCheckpoint(${c.id})">
                    Откатить
                </button>
            </div>
        `).join('');
    } catch (error) {
        console.error('Error loading checkpoints:', error);
    }
}

// Manual Checkpoint Functions

async function createManualCheckpoint() {
    if (!currentDialogueId) {
        alert('Выберите диалог');
        return;
    }
    
    const description = prompt('Описание чекпойнта (необязательно):');
    
    // Пользователь отменил
    if (description === null) {
        return;
    }
    
    try {
        const response = await fetch(
            `${API_BASE}/api/dialogues/${currentDialogueId}/checkpoints`,
            {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ 
                    description: description || 'Manual checkpoint' 
                })
            }
        );
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Ошибка создания чекпойнта');
        }
        
        await loadCheckpoints(currentDialogueId);
        showStatusMessage('Чекпойнт создан', 'success');
    } catch (error) {
        console.error('Error creating checkpoint:', error);
        showStatusMessage('Ошибка создания чекпойнта: ' + error.message, 'error');
    }
}

function showStatusMessage(message, type) {
    const toast = document.createElement('div');
    toast.className = `toast ${type === 'error' ? 'toast-error' : ''}`;
    toast.textContent = message;
    document.body.appendChild(toast);
    
    setTimeout(() => {
        toast.remove();
    }, 3000);
}

async function sendMessage() {
    if (!currentDialogueId) {
        alert('Выберите диалог');
        return;
    }
    
    if (isProcessing) {
        return;
    }
    
    const input = document.getElementById('prompt-input');
    const content = input.value.trim();
    
    if (!content) {
        return;
    }
    
    isProcessing = true;
    const sendButton = document.getElementById('send-button');
    sendButton.disabled = true;
    sendButton.textContent = 'Обработка...';
    
    // ОПТИМИЗАЦИЯ 1: Мгновенное отображение сообщения пользователя (оптимистичный UI)
    const userMessageElement = createMessageElement('user', content);
    const messageList = document.getElementById('message-list');
    messageList.appendChild(userMessageElement);
    messageList.scrollTop = messageList.scrollHeight;
    
    // ОПТИМИЗАЦИЯ 2: Показываем индикатор "печатает..."
    const typingIndicator = createTypingIndicator();
    messageList.appendChild(typingIndicator);
    messageList.scrollTop = messageList.scrollHeight;
    
    // Очищаем поле ввода сразу
    input.value = '';
    
    // Удаление черновика после отправки сообщения
    if (draftManager && currentDialogueId) {
        draftManager.clearDraft(currentDialogueId);
        console.log('[UI] Черновик удален после отправки сообщения');
    }
    
    try {
        // Проверка активности WebSocket соединения
        if (wsClient && wsClient.isConnected && !wsClient.isUsingHttp) {
            // Отправка через WebSocket
            console.log('[UI] Отправка сообщения через WebSocket');
            
            const sent = await wsClient.sendMessage('user_message', {
                dialogueId: currentDialogueId,
                content: content
            });
            
            if (!sent) {
                // Если отправка через WebSocket не удалась, fallback на HTTP
                console.warn('[UI] Не удалось отправить через WebSocket, используем HTTP');
                await sendMessageViaHttp(content, typingIndicator);
            }
            // Если отправка успешна, ответ придет через WebSocket обработчики
            
        } else {
            // Отправка через HTTP API
            console.log('[UI] Отправка сообщения через HTTP');
            await sendMessageViaHttp(content, typingIndicator);
        }
        
        // Проверка на команду запуска выполнения задач
        const contentLower = content.toLowerCase();
        if (contentLower.includes('начни выполнение') || 
            contentLower.includes('запусти выполнение') ||
            contentLower.includes('выполни все задачи') ||
            contentLower.includes('start execution') ||
            contentLower.includes('execute tasks') ||
            contentLower.includes('execute all tasks')) {
            // Запуск polling для отслеживания статуса выполнения
            startPollingExecutionStatus();
        }
        
    } catch (error) {
        console.error('Error sending message:', error);
        
        // Удаляем индикатор "печатает..." при ошибке
        typingIndicator.remove();
        
        // Показываем сообщение об ошибке
        const errorMessageElement = createMessageElement('assistant', 
            `❌ Ошибка: ${error.message}`);
        errorMessageElement.classList.add('error');
        messageList.appendChild(errorMessageElement);
        messageList.scrollTop = messageList.scrollHeight;
        
        showError('Ошибка отправки сообщения: ' + error.message);
    } finally {
        isProcessing = false;
        sendButton.disabled = false;
        sendButton.textContent = 'Отправить';
    }
}

// Вспомогательная функция для отправки сообщения через HTTP
async function sendMessageViaHttp(content, typingIndicator) {
    const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/messages`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content })
    });
    
    if (!response.ok) {
        const error = await response.text();
        throw new Error(error);
    }
    
    const result = await response.json();
    
    // Удаляем индикатор "печатает..." и добавляем ответ
    typingIndicator.remove();
    const messageList = document.getElementById('message-list');
    const assistantMessageElement = createMessageElement('assistant', result.content);
    messageList.appendChild(assistantMessageElement);
    messageList.scrollTop = messageList.scrollHeight;
    
    // Кэширование ответа ассистента при HTTP режиме
    if (messageCache && result.content && currentDialogueId) {
        const assistantMessage = {
            id: result.id || Date.now(),
            role: 'assistant',
            content: result.content,
            timestamp: new Date().toISOString()
        };
        messageCache.addMessage(currentDialogueId, assistantMessage);
        console.log('[MessageCache] Ответ ассистента (HTTP) добавлен в кэш');
    }
    
    // Загружаем чекпоинты асинхронно (не блокируем UI)
    loadCheckpoints(currentDialogueId).catch(err => 
        console.error('Error loading checkpoints:', err)
    );
}

// Вспомогательная функция для создания элемента сообщения
function createMessageElement(role, content) {
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${role}`;
    
    const roleDiv = document.createElement('div');
    roleDiv.className = 'message-role';
    roleDiv.textContent = role === 'user' ? 'Вы' : 'Ассистент';
    
    const contentDiv = document.createElement('div');
    contentDiv.className = 'message-content';
    contentDiv.textContent = content;
    
    messageDiv.appendChild(roleDiv);
    messageDiv.appendChild(contentDiv);
    
    return messageDiv;
}

// Вспомогательная функция для создания индикатора "печатает..."
function createTypingIndicator() {
    const typingDiv = document.createElement('div');
    typingDiv.className = 'message assistant typing-indicator';
    typingDiv.id = 'typing-indicator';
    
    const roleDiv = document.createElement('div');
    roleDiv.className = 'message-role';
    roleDiv.textContent = 'Ассистент';
    
    const contentDiv = document.createElement('div');
    contentDiv.className = 'message-content';
    contentDiv.innerHTML = '<span class="typing-dots"><span>.</span><span>.</span><span>.</span></span>';
    
    typingDiv.appendChild(roleDiv);
    typingDiv.appendChild(contentDiv);
    
    return typingDiv;
}

async function rollbackToCheckpoint(checkpointId) {
    if (!confirm('Вы уверены, что хотите откатить проект к этому чекпоинту?')) {
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/rollback`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ checkpointId })
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        alert('Откат выполнен успешно');
        await loadMessages(currentDialogueId);
    } catch (error) {
        console.error('Error rolling back:', error);
        alert('Ошибка отката: ' + error.message);
    }
}

function showError(message) {
    const messageList = document.getElementById('message-list');
    const errorDiv = document.createElement('div');
    errorDiv.className = 'error';
    errorDiv.textContent = message;
    messageList.insertBefore(errorDiv, messageList.firstChild);
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Model Connection Validation
async function validateModelConnection() {
    const modelIndicator = document.getElementById('model-status-indicator');
    const modelIcon = document.querySelector('.model-status-icon');
    const modelLabel = document.querySelector('.model-status-label');
    const startButton = document.getElementById('start-model-button');
    
    console.log('[ModelValidation] Начало проверки подключения к модели...');
    
    if (!modelIndicator || !modelLabel || !startButton) {
        console.error('[ModelValidation] Не найдены элементы индикатора модели');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/startup/validate`);
        const result = await response.json();
        
        console.log('[ModelValidation] Результат проверки:', result);
        
        if (result.isConnected) {
            // Модель активна
            modelIndicator.className = 'model-active';
            modelLabel.textContent = `Модель активна: ${result.modelName || 'неизвестно'}`;
            startButton.style.display = 'none';
            console.log('[ModelValidation] ✓ Модель активна:', result.modelName);
        } else {
            // Модель неактивна
            modelIndicator.className = 'model-inactive';
            modelLabel.textContent = result.errorMessage || 'Модель недоступна';
            
            console.warn('[ModelValidation] ✗ Модель неактивна:', result.errorMessage);
            
            // Показываем кнопку запуска только для Ollama
            const isOllamaError = result.errorMessage && 
                (result.errorMessage.toLowerCase().includes('ollama') || 
                 result.errorMessage.toLowerCase().includes('llama'));
            
            if (isOllamaError) {
                startButton.style.display = 'block';
                console.log('[ModelValidation] Показана кнопка запуска Ollama');
            }
            
            showStartupWarning(result.errorMessage);
        }
    } catch (error) {
        console.error('[ModelValidation] Ошибка при проверке подключения:', error);
        modelIndicator.className = 'model-inactive';
        modelLabel.textContent = 'Ошибка проверки';
        startButton.style.display = 'none';
    }
}

function showStartupWarning(message) {
    const warningDiv = document.createElement('div');
    warningDiv.className = 'startup-warning';
    
    // Проверяем, содержит ли сообщение информацию об Ollama
    const isOllamaError = message.toLowerCase().includes('ollama') || message.toLowerCase().includes('llama');
    
    warningDiv.innerHTML = `
        <div class="warning-content">
            <span class="warning-icon">⚠️</span>
            <span class="warning-message">${escapeHtml(message)}</span>
            ${isOllamaError ? '<button class="warning-action-btn" onclick="startOllamaModel()">🚀 Запустить модель</button>' : ''}
            <button class="warning-close" onclick="this.parentElement.parentElement.remove()">✕</button>
        </div>
    `;
    document.body.insertBefore(warningDiv, document.body.firstChild);
}

// Функция для запуска модели Ollama
async function startOllamaModel() {
    const btn = document.querySelector('.warning-action-btn') || document.getElementById('start-model-button');
    if (!btn) return;
    
    const originalText = btn.textContent;
    btn.textContent = '⏳ Запуск...';
    btn.disabled = true;
    
    try {
        const response = await fetch('/api/ollama/start', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        if (response.ok) {
            btn.textContent = '✓ Запущено!';
            btn.style.background = '#28a745';
            
            // Перезагружаем страницу через 2 секунды
            setTimeout(() => {
                window.location.reload();
            }, 2000);
        } else {
            const error = await response.text();
            btn.textContent = '✗ Ошибка';
            btn.style.background = '#dc3545';
            alert(`Не удалось запустить модель: ${error}`);
            setTimeout(() => {
                btn.textContent = originalText;
                btn.disabled = false;
                btn.style.background = '';
            }, 3000);
        }
    } catch (error) {
        btn.textContent = '✗ Ошибка';
        btn.style.background = '#dc3545';
        alert(`Ошибка запуска: ${error.message}`);
        setTimeout(() => {
            btn.textContent = originalText;
            btn.disabled = false;
            btn.style.background = '';
        }, 3000);
    }
}

function showHelp() {
    const helpText = `
📖 Краткая справка по использованию

⚠️ ВАЖНО: Команды работают с проектом, который вы указали при создании диалога!

🔹 ПРИМЕРЫ КОМАНД:

Просмотр кода:
• "Покажи файл Program.cs"
• "Покажи файл Services/UserService.cs"
• "Найди класс UserService"
• "Покажи методы класса OrderController"
• "Покажи структуру проекта"

Рефакторинг:
• "Переименуй метод GetUser в FetchUserData"
• "Извлеки этот код в отдельный метод"
• "Добавь проверку на null в метод ProcessOrder"

Создание кода:
• "Создай класс EmailService в папке Services"
• "Добавь метод SendEmail в класс EmailService"
• "Создай интерфейс IRepository"

Работа с файлами:
• "Покажи все файлы в папке Services"
• "Покажи все .cs файлы"
• "Создай папку Models"

🔹 СОВЕТЫ:

✅ Указывайте относительные пути от корня проекта
✅ Для вложенных файлов: "Services/UserService.cs"
✅ Будьте конкретны в командах
✅ Используйте чекпоинты для отката

⚙️ НАСТРОЙКИ:

Нажмите кнопку ⚙️ для настройки модели:
• Provider - облачные модели (OpenAI, DeepSeek, F5AI)
• Local - локальные модели (Ollama)

📚 Полная документация: USAGE_GUIDE.md
    `.trim();
    
    alert(helpText);
}

// Model Configuration Modal Functions

async function openConfigModal() {
    const overlay = document.getElementById('model-config-overlay');
    overlay.classList.add('active');
    await loadConfiguration();
}

async function loadConfiguration() {
    try {
        const response = await fetch(`${API_BASE}/api/configuration`);
        
        if (!response.ok) {
            throw new Error('Failed to load configuration');
        }
        
        const data = await response.json();
        const config = data.configuration;
        
        if (!config) {
            return;
        }
        
        // Determine which tab to activate based on provider type
        const activeTab = config.provider?.toLowerCase() === 'ollama' ? 'local' : 'provider';
        switchTab(activeTab);
        
        // Populate Provider tab fields
        if (config.openAI) {
            document.getElementById('provider-base-url').value = config.openAI.baseUrl || '';
            document.getElementById('provider-api-key').value = config.openAI.apiKey || '';
            document.getElementById('provider-model').value = config.openAI.model || '';
        }
        
        // Populate Local tab fields
        if (config.ollama) {
            document.getElementById('ollama-base-url').value = config.ollama.baseUrl || '';
            
            // Set the model value in the select dropdown
            const modelSelect = document.getElementById('ollama-model');
            const modelValue = config.ollama.model || '';
            
            // Check if the option exists, if not add it
            let optionExists = false;
            for (let i = 0; i < modelSelect.options.length; i++) {
                if (modelSelect.options[i].value === modelValue) {
                    optionExists = true;
                    break;
                }
            }
            
            if (!optionExists && modelValue) {
                const option = document.createElement('option');
                option.value = modelValue;
                option.textContent = modelValue;
                modelSelect.appendChild(option);
            }
            
            modelSelect.value = modelValue;
        }
        
    } catch (error) {
        console.error('Error loading configuration:', error);
        showStatusMessage('Failed to load configuration: ' + error.message, 'error');
    }
}

function closeConfigModal() {
    const overlay = document.getElementById('model-config-overlay');
    overlay.classList.remove('active');
}

function switchTab(tabName) {
    // Update tab buttons
    document.querySelectorAll('.tab-button').forEach(button => {
        if (button.dataset.tab === tabName) {
            button.classList.add('active');
        } else {
            button.classList.remove('active');
        }
    });
    
    // Update tab content
    document.querySelectorAll('.tab-content').forEach(content => {
        if (content.id === `${tabName}-tab`) {
            content.classList.add('active');
        } else {
            content.classList.remove('active');
        }
    });
}

function showStatusMessage(message, type) {
    const statusElement = document.getElementById('status-message');
    statusElement.textContent = message;
    statusElement.className = type; // 'success' or 'error'
    
    // Auto-hide success messages after 3 seconds
    if (type === 'success') {
        setTimeout(() => {
            statusElement.className = '';
            statusElement.textContent = '';
        }, 3000);
    }
}

// Validation Functions

function validateUrl(url) {
    if (!url || url.trim() === '') {
        return false;
    }
    
    try {
        const urlObj = new URL(url);
        return urlObj.protocol === 'http:' || urlObj.protocol === 'https:';
    } catch (e) {
        return false;
    }
}

function validateRequiredField(value) {
    return value && value.trim() !== '';
}

function clearFieldError(fieldId) {
    const field = document.getElementById(fieldId);
    if (field) {
        field.style.borderColor = '';
        
        // Remove any existing error message
        const existingError = field.parentElement.querySelector('.field-error');
        if (existingError) {
            existingError.remove();
        }
    }
}

function showFieldError(fieldId, message) {
    const field = document.getElementById(fieldId);
    if (field) {
        field.style.borderColor = '#dc3545';
        
        // Remove any existing error message
        const existingError = field.parentElement.querySelector('.field-error');
        if (existingError) {
            existingError.remove();
        }
        
        // Add new error message
        const errorDiv = document.createElement('div');
        errorDiv.className = 'field-error';
        errorDiv.textContent = message;
        errorDiv.style.color = '#dc3545';
        errorDiv.style.fontSize = '12px';
        errorDiv.style.marginTop = '4px';
        field.parentElement.appendChild(errorDiv);
    }
}

function validateProviderConfiguration() {
    let isValid = true;
    const errors = [];
    
    // Clear previous errors
    clearFieldError('provider-base-url');
    clearFieldError('provider-api-key');
    clearFieldError('provider-model');
    
    // Get field values
    const baseUrl = document.getElementById('provider-base-url').value;
    const apiKey = document.getElementById('provider-api-key').value;
    const model = document.getElementById('provider-model').value;
    
    // Validate base URL
    if (!validateRequiredField(baseUrl)) {
        showFieldError('provider-base-url', 'Base URL is required');
        errors.push('Base URL is required');
        isValid = false;
    } else if (!validateUrl(baseUrl)) {
        showFieldError('provider-base-url', 'Invalid URL format');
        errors.push('Base URL must be a valid URL');
        isValid = false;
    }
    
    // Validate API key
    if (!validateRequiredField(apiKey)) {
        showFieldError('provider-api-key', 'API Key is required');
        errors.push('API Key is required');
        isValid = false;
    }
    
    // Validate model
    if (!validateRequiredField(model)) {
        showFieldError('provider-model', 'Model name is required');
        errors.push('Model name is required');
        isValid = false;
    }
    
    return { isValid, errors };
}

function validateLocalConfiguration() {
    let isValid = true;
    const errors = [];
    
    // Clear previous errors
    clearFieldError('ollama-base-url');
    clearFieldError('ollama-model');
    
    // Get field values
    const baseUrl = document.getElementById('ollama-base-url').value;
    const model = document.getElementById('ollama-model').value;
    
    // Validate base URL
    if (!validateRequiredField(baseUrl)) {
        showFieldError('ollama-base-url', 'Ollama URL is required');
        errors.push('Ollama URL is required');
        isValid = false;
    } else if (!validateUrl(baseUrl)) {
        showFieldError('ollama-base-url', 'Invalid URL format');
        errors.push('Ollama URL must be a valid URL');
        isValid = false;
    }
    
    // Validate model
    if (!validateRequiredField(model)) {
        showFieldError('ollama-model', 'Model selection is required');
        errors.push('Model selection is required');
        isValid = false;
    }
    
    return { isValid, errors };
}

async function saveProviderConfiguration() {
    // Validate configuration
    const validation = validateProviderConfiguration();
    
    if (!validation.isValid) {
        showStatusMessage('Please fix the validation errors: ' + validation.errors.join(', '), 'error');
        return;
    }
    
    // Get field values
    const baseUrl = document.getElementById('provider-base-url').value.trim();
    const apiKey = document.getElementById('provider-api-key').value.trim();
    const model = document.getElementById('provider-model').value.trim();
    
    // Build configuration request
    const configRequest = {
        provider: 'OpenAI',
        openAI: {
            baseUrl: baseUrl,
            apiKey: apiKey,
            model: model
        }
    };
    
    try {
        const saveButton = document.getElementById('save-provider');
        saveButton.disabled = true;
        saveButton.textContent = 'Saving...';
        
        const response = await fetch(`${API_BASE}/api/configuration`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(configRequest)
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        const result = await response.json();
        
        if (result.success) {
            showStatusMessage('Provider configuration saved successfully', 'success');
        } else {
            showStatusMessage('Failed to save configuration: ' + (result.message || 'Unknown error'), 'error');
        }
        
    } catch (error) {
        console.error('Error saving provider configuration:', error);
        showStatusMessage('Error saving configuration: ' + error.message, 'error');
    } finally {
        const saveButton = document.getElementById('save-provider');
        saveButton.disabled = false;
        saveButton.textContent = 'Save Provider Configuration';
    }
}

async function saveLocalConfiguration() {
    // Validate configuration
    const validation = validateLocalConfiguration();
    
    if (!validation.isValid) {
        showStatusMessage('Please fix the validation errors: ' + validation.errors.join(', '), 'error');
        return;
    }
    
    // Get field values
    const baseUrl = document.getElementById('ollama-base-url').value.trim();
    const model = document.getElementById('ollama-model').value.trim();
    
    // Build configuration request
    const configRequest = {
        provider: 'Ollama',
        ollama: {
            baseUrl: baseUrl,
            model: model
        }
    };
    
    try {
        const saveButton = document.getElementById('save-local');
        saveButton.disabled = true;
        saveButton.textContent = 'Saving...';
        
        const response = await fetch(`${API_BASE}/api/configuration`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(configRequest)
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        const result = await response.json();
        
        if (result.success) {
            showStatusMessage('Local configuration saved successfully', 'success');
        } else {
            showStatusMessage('Failed to save configuration: ' + (result.message || 'Unknown error'), 'error');
        }
        
    } catch (error) {
        console.error('Error saving local configuration:', error);
        showStatusMessage('Error saving configuration: ' + error.message, 'error');
    } finally {
        const saveButton = document.getElementById('save-local');
        saveButton.disabled = false;
        saveButton.textContent = 'Save Local Configuration';
    }
}

async function refreshOllamaModels() {
    const refreshButton = document.getElementById('refresh-models');
    const modelSelect = document.getElementById('ollama-model');
    const ollamaBaseUrl = document.getElementById('ollama-base-url').value.trim();
    
    try {
        // Disable button and show loading state
        refreshButton.disabled = true;
        refreshButton.textContent = 'Refreshing...';
        
        // Clear current options except the first one
        modelSelect.innerHTML = '<option value="">Select a model</option>';
        
        // Fetch models from the API
        const response = await fetch(`${API_BASE}/api/configuration/ollama/models`);
        
        if (!response.ok) {
            throw new Error('Failed to fetch models from Ollama');
        }
        
        const data = await response.json();
        
        // Check if we got any models
        if (!data.models || data.models.length === 0) {
            // Handle connection error gracefully
            const baseUrlDisplay = ollamaBaseUrl || 'http://localhost:11434';
            showStatusMessage(
                `Unable to connect to Ollama at ${baseUrlDisplay}. Please verify Ollama is running and the URL is correct.`,
                'error'
            );
            return;
        }
        
        // Populate the dropdown with fetched models
        data.models.forEach(modelName => {
            const option = document.createElement('option');
            option.value = modelName;
            option.textContent = modelName;
            modelSelect.appendChild(option);
        });
        
        showStatusMessage(`Successfully loaded ${data.models.length} model(s) from Ollama`, 'success');
        
    } catch (error) {
        console.error('Error fetching Ollama models:', error);
        const baseUrlDisplay = ollamaBaseUrl || 'http://localhost:11434';
        showStatusMessage(
            `Error connecting to Ollama at ${baseUrlDisplay}: ${error.message}`,
            'error'
        );
    } finally {
        // Re-enable button and restore text
        refreshButton.disabled = false;
        refreshButton.textContent = 'Refresh Models';
    }
}

// Test Connection Functions

async function testProviderConnection() {
    // Validate configuration first
    const validation = validateProviderConfiguration();
    
    if (!validation.isValid) {
        showStatusMessage('Please fix the validation errors before testing connection', 'error');
        return;
    }
    
    // Get field values
    const baseUrl = document.getElementById('provider-base-url').value.trim();
    const apiKey = document.getElementById('provider-api-key').value.trim();
    const model = document.getElementById('provider-model').value.trim();
    
    // Build configuration request
    const configRequest = {
        provider: 'OpenAI',
        openAI: {
            baseUrl: baseUrl,
            apiKey: apiKey,
            model: model
        }
    };
    
    try {
        const testButton = document.getElementById('test-provider');
        testButton.disabled = true;
        testButton.textContent = 'Testing...';
        
        const response = await fetch(`${API_BASE}/api/configuration/test`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(configRequest)
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        const result = await response.json();
        
        if (result.success) {
            showStatusMessage('Connection test successful! ' + result.message, 'success');
        } else {
            showStatusMessage('Connection test failed: ' + result.message, 'error');
        }
        
    } catch (error) {
        console.error('Error testing provider connection:', error);
        showStatusMessage('Connection test failed: ' + error.message, 'error');
    } finally {
        const testButton = document.getElementById('test-provider');
        testButton.disabled = false;
        testButton.textContent = 'Test Connection';
    }
}

async function testLocalConnection() {
    // Validate configuration first
    const validation = validateLocalConfiguration();
    
    if (!validation.isValid) {
        showStatusMessage('Please fix the validation errors before testing connection', 'error');
        return;
    }
    
    // Get field values
    const baseUrl = document.getElementById('ollama-base-url').value.trim();
    const model = document.getElementById('ollama-model').value.trim();
    
    // Build configuration request
    const configRequest = {
        provider: 'Ollama',
        ollama: {
            baseUrl: baseUrl,
            model: model
        }
    };
    
    try {
        const testButton = document.getElementById('test-local');
        testButton.disabled = true;
        testButton.textContent = 'Testing...';
        
        const response = await fetch(`${API_BASE}/api/configuration/test`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(configRequest)
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        const result = await response.json();
        
        if (result.success) {
            showStatusMessage('Connection test successful! ' + result.message, 'success');
        } else {
            showStatusMessage('Connection test failed: ' + result.message, 'error');
        }
        
    } catch (error) {
        console.error('Error testing local connection:', error);
        showStatusMessage('Connection test failed: ' + error.message, 'error');
    } finally {
        const testButton = document.getElementById('test-local');
        testButton.disabled = false;
        testButton.textContent = 'Test Connection';
    }
}

// Функции управления агентским режимом выполнения задач

// Настройка кнопок управления выполнением
function setupExecutionControlButtons() {
    const stopButton = document.getElementById('stop-execution-btn');
    const resumeButton = document.getElementById('resume-execution-btn');
    
    if (stopButton) {
        stopButton.addEventListener('click', stopExecution);
    }
    
    if (resumeButton) {
        resumeButton.addEventListener('click', resumeExecution);
    }
}

// Остановка выполнения задач
async function stopExecution() {
    if (!currentDialogueId) {
        return;
    }
    
    try {
        const stopButton = document.getElementById('stop-execution-btn');
        stopButton.disabled = true;
        stopButton.textContent = 'Останавливаю...';
        
        // Отправка команды через sendMessage
        const input = document.getElementById('prompt-input');
        input.value = 'останови выполнение';
        await sendMessage();
        
    } catch (error) {
        console.error('Error stopping execution:', error);
        showStatusMessage('Ошибка остановки выполнения', 'error');
    } finally {
        const stopButton = document.getElementById('stop-execution-btn');
        stopButton.disabled = false;
        stopButton.textContent = 'Остановить';
    }
}

// Возобновление выполнения задач
async function resumeExecution() {
    if (!currentDialogueId) {
        return;
    }
    
    try {
        const resumeButton = document.getElementById('resume-execution-btn');
        resumeButton.disabled = true;
        resumeButton.textContent = 'Возобновляю...';
        
        // Отправка команды через sendMessage
        const input = document.getElementById('prompt-input');
        input.value = 'продолжи выполнение';
        await sendMessage();
        
        // Запуск polling после возобновления
        startPollingExecutionStatus();
        
    } catch (error) {
        console.error('Error resuming execution:', error);
        showStatusMessage('Ошибка возобновления выполнения', 'error');
    } finally {
        const resumeButton = document.getElementById('resume-execution-btn');
        resumeButton.disabled = false;
        resumeButton.textContent = 'Возобновить';
    }
}

// Polling статуса выполнения
async function pollExecutionStatus() {
    // Проверка наличия currentDialogueId
    if (!currentDialogueId) {
        stopPollingExecutionStatus();
        return;
    }
    
    try {
        // Вызов GET /api/dialogues/{id}/execution-status
        const response = await fetch(
            `${API_BASE}/api/dialogues/${currentDialogueId}/execution-status`
        );
        
        // Обработка ошибки 404 - остановка polling
        if (response.status === 404) {
            console.error('Dialogue not found (404), stopping polling');
            stopPollingExecutionStatus();
            return;
        }
        
        if (!response.ok) {
            console.error('Failed to fetch execution status:', response.status);
            // Продолжаем polling несмотря на ошибку (кроме 404)
            return;
        }
        
        const status = await response.json();
        
        // Обновление UI через updateExecutionUI
        updateExecutionUI(status);
        
        // Загрузка новых сообщений через loadMessages
        await loadMessages(currentDialogueId);
        
        // Остановка polling если status завершен
        if (status.status === 'completed' || 
            status.status === 'failed' || 
            status.status === 'none') {
            stopPollingExecutionStatus();
        }
        
    } catch (error) {
        // Обработка ошибок сети - логирование, продолжение polling
        console.error('Network error polling execution status:', error);
        // Не останавливаем polling - возможно временная проблема
    }
}

// Запуск polling статуса выполнения
function startPollingExecutionStatus() {
    // Проверка на уже запущенный polling
    if (executionPollingInterval) {
        return;
    }
    
    // Создание interval с интервалом 2000ms
    executionPollingInterval = setInterval(pollExecutionStatus, 2000);
    
    // Немедленный первый вызов
    pollExecutionStatus();
}

// Остановка polling статуса выполнения
function stopPollingExecutionStatus() {
    if (executionPollingInterval) {
        clearInterval(executionPollingInterval);
        executionPollingInterval = null;
    }
}

// Обновление UI на основе статуса выполнения
function updateExecutionUI(status) {
    currentExecutionStatus = status.status;
    
    // Обновление кнопок управления
    updateControlButtons(status.status);
    
    // Обновление индикатора прогресса
    updateStatusIndicator(status);
}

// Обновление кнопок управления
function updateControlButtons(status) {
    const stopButton = document.getElementById('stop-execution-btn');
    const resumeButton = document.getElementById('resume-execution-btn');
    const controlsContainer = document.getElementById('execution-controls');
    
    if (!stopButton || !resumeButton || !controlsContainer) {
        return;
    }
    
    // Скрыть контейнер если status="none" или "completed"
    if (status === 'none' || status === 'completed') {
        controlsContainer.style.display = 'none';
        return;
    }
    
    // Показать контейнер для других статусов
    controlsContainer.style.display = 'flex';
    
    // Управление видимостью кнопок
    if (status === 'running') {
        stopButton.style.display = 'inline-block';
        resumeButton.style.display = 'none';
    } else if (status === 'stopped' || status === 'failed') {
        stopButton.style.display = 'none';
        resumeButton.style.display = 'inline-block';
    }
}

// Обновление индикатора статуса
function updateStatusIndicator(status) {
    const indicator = document.getElementById('execution-status-indicator');
    
    if (!indicator) {
        return;
    }
    
    // Скрыть если нет активного выполнения
    if (status.status === 'none') {
        indicator.style.display = 'none';
        return;
    }
    
    indicator.style.display = 'block';
    
    // Формирование текста индикатора
    let statusText = '';
    let statusEmoji = '';
    
    switch (status.status) {
        case 'running':
            statusEmoji = '🔄';
            statusText = 'Выполняется...';
            break;
        case 'stopped':
            statusEmoji = '⏸️';
            statusText = 'Приостановлено';
            break;
        case 'completed':
            statusEmoji = '✅';
            statusText = 'Завершено';
            break;
        case 'failed':
            statusEmoji = '❌';
            statusText = 'Ошибка';
            break;
    }
    
    // Добавление прогресса
    let progressText = '';
    if (status.progress) {
        progressText = ` (${status.progress})`;
    }
    
    // Добавление текущей задачи
    let currentTaskText = '';
    if (status.currentTask) {
        const truncated = status.currentTask.substring(0, 50);
        currentTaskText = `<br><small>${truncated}${status.currentTask.length > 50 ? '...' : ''}</small>`;
    }
    
    indicator.innerHTML = `
        <span class="status-emoji">${statusEmoji}</span>
        <span class="status-text">${statusText}${progressText}</span>
        ${currentTaskText}
    `;
}


// ============================================
// Функции поиска по сообщениям
// ============================================

// Выполнение поиска по сообщениям
function performMessageSearch() {
    const searchInput = document.getElementById('message-search-input');
    const query = searchInput.value.trim();
    
    if (!query) {
        showSearchInfo('Введите текст для поиска');
        return;
    }
    
    if (!virtualList) {
        showSearchInfo('Список сообщений не инициализирован');
        return;
    }
    
    console.log(`[Search] Поиск по запросу: "${query}"`);
    
    // Выполнение поиска через VirtualList
    currentSearchResults = virtualList.searchMessages(query);
    currentSearchIndex = 0;
    
    if (currentSearchResults.length === 0) {
        showSearchInfo('Совпадений не найдено');
        return;
    }
    
    // Отображение информации о результатах
    showSearchInfo(`Найдено: ${currentSearchResults.length} совпадений`);
    
    // Прокрутка к первому результату
    scrollToSearchResult(0);
}

// Прокрутка к результату поиска по индексу
function scrollToSearchResult(index) {
    if (!currentSearchResults || currentSearchResults.length === 0) {
        return;
    }
    
    if (index < 0 || index >= currentSearchResults.length) {
        return;
    }
    
    currentSearchIndex = index;
    const result = currentSearchResults[index];
    
    // Прокрутка к найденному сообщению с подсветкой
    if (virtualList) {
        virtualList.scrollToItem(result.index, true);
    }
    
    // Обновление информации о текущем результате
    showSearchInfo(`Результат ${index + 1} из ${currentSearchResults.length}`);
    
    console.log(`[Search] Прокрутка к результату ${index + 1}/${currentSearchResults.length}`);
}

// Переход к следующему результату поиска
function nextSearchResult() {
    if (!currentSearchResults || currentSearchResults.length === 0) {
        return;
    }
    
    const nextIndex = (currentSearchIndex + 1) % currentSearchResults.length;
    scrollToSearchResult(nextIndex);
}

// Переход к предыдущему результату поиска
function previousSearchResult() {
    if (!currentSearchResults || currentSearchResults.length === 0) {
        return;
    }
    
    const prevIndex = currentSearchIndex === 0 
        ? currentSearchResults.length - 1 
        : currentSearchIndex - 1;
    scrollToSearchResult(prevIndex);
}

// Очистка поиска
function clearMessageSearch() {
    const searchInput = document.getElementById('message-search-input');
    searchInput.value = '';
    
    currentSearchResults = [];
    currentSearchIndex = 0;
    
    showSearchInfo('');
    
    console.log('[Search] Поиск очищен');
}

// Отображение информации о поиске
function showSearchInfo(message) {
    const infoElement = document.getElementById('search-results-info');
    if (infoElement) {
        infoElement.textContent = message;
    }
}

// Показать/скрыть панель поиска
function toggleSearchPanel() {
    const searchContainer = document.getElementById('message-search-container');
    if (searchContainer) {
        const isVisible = searchContainer.style.display !== 'none';
        searchContainer.style.display = isVisible ? 'none' : 'flex';
        
        if (!isVisible) {
            // Фокус на поле ввода при открытии
            const searchInput = document.getElementById('message-search-input');
            if (searchInput) {
                searchInput.focus();
            }
        }
    }
}

// Инициализация обработчиков поиска
function initializeSearchHandlers() {
    const searchButton = document.getElementById('search-button');
    const clearButton = document.getElementById('clear-search-button');
    const searchInput = document.getElementById('message-search-input');
    
    if (searchButton) {
        searchButton.addEventListener('click', performMessageSearch);
    }
    
    if (clearButton) {
        clearButton.addEventListener('click', clearMessageSearch);
    }
    
    if (searchInput) {
        // Поиск по нажатию Enter
        searchInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                performMessageSearch();
            }
        });
        
        // Поддержка навигации по результатам через Ctrl+G (следующий) и Ctrl+Shift+G (предыдущий)
        searchInput.addEventListener('keydown', (e) => {
            if (e.ctrlKey && e.key === 'g') {
                e.preventDefault();
                if (e.shiftKey) {
                    previousSearchResult();
                } else {
                    nextSearchResult();
                }
            }
        });
    }
    
    // Добавление горячей клавиши Ctrl+F для открытия поиска
    document.addEventListener('keydown', (e) => {
        if (e.ctrlKey && e.key === 'f') {
            e.preventDefault();
            toggleSearchPanel();
        }
        
        // Горячая клавиша Ctrl+Shift+D для переключения режима отладки
        if (e.ctrlKey && e.shiftKey && e.key === 'D') {
            e.preventDefault();
            toggleDebugMode();
        }
    });
    
    console.log('[Search] Обработчики поиска инициализированы');
}

// Обновление индикатора статуса соединения
function updateConnectionStatus(status, data) {
    const indicator = document.getElementById('connection-status-indicator');
    if (!indicator) {
        console.warn('[UI] Индикатор статуса соединения не найден');
        return;
    }
    
    const statusLabel = indicator.querySelector('.status-label');
    if (!statusLabel) {
        console.warn('[UI] Элемент статуса не найден');
        return;
    }
    
    // Удаление всех классов статуса
    indicator.className = '';
    
    // Обновление метрики статуса соединения
    updatePerformanceMetric('connectionStatus', status);
    
    // Установка нового статуса и текста
    switch (status) {
        case 'connected':
            indicator.classList.add('status-connected');
            statusLabel.textContent = 'WebSocket подключен';
            console.log('[UI] Статус обновлен: WebSocket подключен');
            break;
            
        case 'disconnected':
            indicator.classList.add('status-disconnected');
            statusLabel.textContent = 'Отключено';
            console.log('[UI] Статус обновлен: Отключено');
            break;
            
        case 'error':
            indicator.classList.add('status-error');
            statusLabel.textContent = 'Ошибка соединения';
            console.log('[UI] Статус обновлен: Ошибка соединения');
            break;
            
        case 'reconnecting':
            indicator.classList.add('status-reconnecting');
            if (data && data.attempt) {
                statusLabel.textContent = `Переподключение (попытка ${data.attempt}/5)...`;
            } else {
                statusLabel.textContent = 'Переподключение...';
            }
            console.log('[UI] Статус обновлен: Переподключение', data);
            break;
            
        case 'http_fallback':
            indicator.classList.add('status-http');
            statusLabel.textContent = 'HTTP режим';
            console.log('[UI] Статус обновлен: HTTP режим');
            break;
            
        default:
            indicator.classList.add('status-disconnected');
            statusLabel.textContent = 'Неизвестный статус';
            console.warn('[UI] Неизвестный статус соединения:', status);
    }
}

const API_BASE = '';

let currentDialogueId = null;
let isProcessing = false;
let projects = [];
let dialogues = []; // Глобальный список диалогов
let dialogueGroups = []; // Глобальный список групп диалогов
let currentGroupId = null; // ID текущей открытой группы для контекста
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
                console.log(`[WebSocket] Установка this.isConnected = true`);
                
                this.isConnected = true;
                this.reconnectAttempts = 0; // Сброс счетчика попыток
                
                console.log(`[WebSocket] После установки: this.isConnected =`, this.isConnected);
                
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
        console.log('[WebSocket] sendMessage вызван');
        console.log('[WebSocket] type:', type);
        console.log('[WebSocket] payload:', payload);
        console.log('[WebSocket] this.isConnected:', this.isConnected);
        console.log('[WebSocket] this.ws:', this.ws);
        console.log('[WebSocket] this.ws?.readyState:', this.ws?.readyState);
        console.log('[WebSocket] WebSocket.OPEN:', WebSocket.OPEN);
        
        if (!this.isConnected || !this.ws || this.ws.readyState !== WebSocket.OPEN) {
            console.warn('[WebSocket] Соединение не активно, сообщение не отправлено');
            console.warn('[WebSocket] Детали: isConnected=', this.isConnected, 
                        'ws=', this.ws, 
                        'readyState=', this.ws?.readyState);
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
        
        // Добавляем messageId как data-атрибут
        if (item.id) {
            messageDiv.dataset.messageId = item.id;
        }
        
        const roleDiv = document.createElement('div');
        roleDiv.className = 'message-role';
        roleDiv.textContent = item.role === 'user' ? 'Вы' : 'Ассистент';
        
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        contentDiv.textContent = item.content;
        
        messageDiv.appendChild(roleDiv);
        messageDiv.appendChild(contentDiv);
        
        // Добавляем кнопку удаления если есть messageId
        if (item.id) {
            const deleteBtn = document.createElement('button');
            deleteBtn.className = 'message-delete-btn';
            deleteBtn.innerHTML = '<span class="material-icons">delete</span>';
            deleteBtn.title = 'Удалить сообщение';
            deleteBtn.onclick = (e) => {
                e.stopPropagation();
                deleteMessage(item.id);
            };
            messageDiv.appendChild(deleteBtn);
        }
        
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
            connectionText = 'WebSocket';
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
    loadDialogueGroups();
    setupEventListeners();
});

// Project Management Functions

async function loadProjects() {
    try {
        const response = await fetch(`${API_BASE}/api/projects`);
        projects = await response.json();
        
        console.log('[Projects] Загружено проектов:', projects.length);
        
        // Автоматически выбираем проект, если он единственный и не выбран
        if (projects.length === 1 && !projects[0].isSelected) {
            console.log('[Projects] Автоматически выбираем единственный проект:', projects[0].name);
            await selectProject(projects[0].id);
        } else {
            updateProjectSelector();
        }
    } catch (error) {
        console.error('Error loading projects:', error);
    }
}

function updateProjectSelector() {
    const displayInput = document.getElementById('selected-project-display');
    
    if (!displayInput) {
        console.warn('[Projects] Поле отображения проекта не найдено');
        return;
    }
    
    if (projects.length === 0) {
        displayInput.value = 'Нет проектов';
        displayInput.style.color = '#999';
        return;
    }
    
    // Находим выбранный проект
    const selectedProject = projects.find(p => p.isSelected);
    
    if (selectedProject) {
        displayInput.value = selectedProject.name;
        displayInput.style.color = '#000';
        displayInput.title = selectedProject.path;
        console.log(`[Projects] Отображен выбранный проект: ${selectedProject.name}`);
    } else {
        displayInput.value = 'Проект не выбран';
        displayInput.style.color = '#999';
        displayInput.title = '';
        console.log('[Projects] Проект не выбран');
    }
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
        await loadDialogueGroups();
        
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
        <div class="project-list-item" style="display: flex; align-items: center; padding: 12px; border: 1px solid #ddd; border-radius: 4px; margin-bottom: 8px; background: ${p.isSelected ? '#e7f3ff' : 'white'};">
            <input 
                type="radio" 
                name="project-selection" 
                value="${p.id}" 
                ${p.isSelected ? 'checked' : ''}
                style="margin-right: 12px; cursor: pointer;"
            />
            <div style="flex: 1;">
                <div style="font-weight: 500;">${escapeHtml(p.name)}</div>
                <div style="font-size: 12px; color: #666;">${escapeHtml(p.path)}</div>
            </div>
            <button class="delete-project-btn" onclick="deleteProject(${p.id})" style="padding: 4px 8px; background: #dc3545; color: white; border: none; border-radius: 4px; cursor: pointer;">
                🗑️
            </button>
        </div>
    `).join('');
}

/**
 * Выбор проекта из модального окна
 */
async function selectProjectFromModal() {
    const selectedRadio = document.querySelector('input[name="project-selection"]:checked');
    
    if (!selectedRadio) {
        alert('Выберите проект из списка');
        return;
    }
    
    const projectId = parseInt(selectedRadio.value);
    console.log('[Projects] Выбран проект из модального окна:', projectId);
    
    try {
        await selectProject(projectId);
        closeProjectModal();
    } catch (error) {
        console.error('[Projects] Ошибка выбора проекта:', error);
        alert('Ошибка выбора проекта: ' + error.message);
    }
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
    document.getElementById('create-dialogue-group-button').addEventListener('click', createDialogueGroup);
    document.getElementById('send-button').addEventListener('click', sendMessage);
    
    // Обработчик кнопки запуска модели
    const startModelButton = document.getElementById('start-model-button');
    if (startModelButton) {
        startModelButton.addEventListener('click', startOllamaModel);
    }
    
    // Обработчик кнопки переподключения WebSocket
    const reconnectButton = document.getElementById('reconnect-websocket-button');
    if (reconnectButton) {
        reconnectButton.addEventListener('click', reconnectWebSocket);
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
    
    // Project management modal
    const projectModalOverlay = document.getElementById('project-modal-overlay');
    if (projectModalOverlay) {
        projectModalOverlay.addEventListener('click', (e) => {
            if (e.target.id === 'project-modal-overlay') {
                closeProjectModal();
            }
        });
    }
}

async function loadDialogueGroups() {
    try {
        // Загружаем группы
        const groupsResponse = await fetch(`${API_BASE}/api/dialogue-groups`);
        dialogueGroups = await groupsResponse.json();
        
        // Загружаем все диалоги
        const dialoguesResponse = await fetch(`${API_BASE}/api/dialogues`);
        dialogues = await dialoguesResponse.json();
        
        const listElement = document.getElementById('dialogue-list');
        
        // Разделяем диалоги на группированные и без группы
        const groupedDialogues = dialogues.filter(d => d.dialogueGroupId != null);
        const ungroupedDialogues = dialogues.filter(d => d.dialogueGroupId == null);
        
        let html = '';
        
        // Отображаем группы
        if (dialogueGroups.length > 0) {
            dialogueGroups.forEach(group => {
                const groupDialogues = dialogues.filter(d => d.dialogueGroupId === group.id);
                const isCollapsed = group.isCollapsed ? 'collapsed' : '';
                const allTasksCompleted = areAllTasksCompleted(group.tasks);
                const runButtonClass = allTasksCompleted ? 'completed' : '';
                const runButtonTitle = allTasksCompleted ? 'Все задачи выполнены' : 'Запустить задачи';
                const runButtonDisabled = allTasksCompleted ? 'disabled' : '';
                const runButtonIcon = allTasksCompleted ? 'check_circle' : 'play_arrow';
                
                html += `
                    <div class="dialogue-group ${isCollapsed}" data-group-id="${group.id}">
                        <div class="dialogue-group-header" onclick="toggleGroup(${group.id})">
                            <span class="group-toggle-icon material-icons">expand_more</span>
                            <span class="group-name">${escapeHtml(group.name)}</span>
                            <div class="group-actions" onclick="event.stopPropagation()">
                                <button class="group-action-btn add" onclick="createDialogueInGroup(${group.id})" title="Добавить диалог"><span class="material-icons">add</span></button>
                                <button class="group-action-btn context" onclick="openContextModal(${group.id})" title="Контекст"><span class="material-icons">description</span></button>
                                <button class="group-action-btn run ${runButtonClass}" 
                                        onclick="executeGroupTasks(${group.id})" 
                                        title="${runButtonTitle}"
                                        ${runButtonDisabled}>
                                    <span class="material-icons">${runButtonIcon}</span>
                                </button>
                                <button class="group-action-btn rename" onclick="renameDialogueGroup(${group.id})" title="Переименовать группу"><span class="material-icons">edit</span></button>
                                <button class="group-action-btn delete" onclick="deleteDialogueGroup(${group.id})" title="Удалить группу"><span class="material-icons">delete</span></button>
                            </div>
                        </div>
                        <div class="dialogue-group-content">
                            ${groupDialogues.length > 0 ? groupDialogues.map(d => `
                                <div class="dialogue-item ${d.id === currentDialogueId ? 'active' : ''}" data-id="${d.id}">
                                    <div class="dialogue-info" onclick="selectDialogue(${d.id})">
                                        <div>Диалог #${d.id}</div>
                                        <div class="dialogue-path">${new Date(d.createdAt).toLocaleString()}</div>
                                    </div>
                                    <button class="dialogue-delete" onclick="deleteDialogue(event, ${d.id})" title="Удалить диалог"><span class="material-icons">delete</span></button>
                                </div>
                            `).join('') : '<div class="empty-state">Нет диалогов в группе</div>'}
                        </div>
                    </div>
                `;
            });
        }
        
        // Отображаем диалоги без группы
        if (ungroupedDialogues.length > 0) {
            html += `
                <div class="ungrouped-section">
                    <div class="ungrouped-header">Без группы</div>
                    ${ungroupedDialogues.map(d => `
                        <div class="dialogue-item ${d.id === currentDialogueId ? 'active' : ''}" data-id="${d.id}">
                            <div class="dialogue-info" onclick="selectDialogue(${d.id})">
                                <div>Диалог #${d.id}</div>
                                <div class="dialogue-path">${d.projectPath}</div>
                            </div>
                            <button class="dialogue-delete" onclick="deleteDialogue(event, ${d.id})" title="Удалить диалог"><span class="material-icons">delete</span></button>
                        </div>
                    `).join('')}
                </div>
            `;
        }
        
        if (html === '') {
            html = '<div class="empty-state">Нет групп и диалогов. Создайте группу.</div>';
        }
        
        listElement.innerHTML = html;
        
        // Проверка существования текущего диалога
        if (currentDialogueId) {
            const dialogueExists = dialogues.some(d => d.id === currentDialogueId);
            if (!dialogueExists) {
                console.log('[UI] Текущий диалог не найден в базе');
                currentDialogueId = null;
                document.getElementById('message-list').innerHTML = '<div class="empty-state">Выберите диалог из списка</div>';
            } else {
                // Если текущий диалог существует, вызываем checkAndShowTasksButton
                await checkAndShowTasksButton();
            }
        } else if (dialogues.length > 0) {
            // Если нет текущего диалога, но есть диалоги, выбираем первый
            await selectDialogue(dialogues[0].id);
        }
        
    } catch (error) {
        console.error('Error loading dialogue groups:', error);
        showError('Ошибка загрузки групп диалогов');
    }
}

async function createDialogue() {
    console.log('[UI] Попытка создания диалога...');
    console.log('[UI] Список проектов:', projects);
    
    // Получаем выбранный проект
    const selectedProject = projects.find(p => p.isSelected);
    
    console.log('[UI] Выбранный проект:', selectedProject);
    
    if (!selectedProject) {
        console.error('[UI] Проект не выбран!');
        alert('Выберите проект из списка');
        return;
    }
    
    console.log('[UI] Отправка запроса на создание диалога для проекта:', selectedProject.path);
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogues`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ projectPath: selectedProject.path })
        });
        
        console.log('[UI] Ответ сервера:', response.status, response.statusText);
        
        if (!response.ok) {
            const error = await response.text();
            console.error('[UI] Ошибка от сервера:', error);
            throw new Error(error);
        }
        
        const dialogue = await response.json();
        console.log('[UI] Диалог создан:', dialogue);
        
        await loadDialogueGroups();
        selectDialogue(dialogue.id);
    } catch (error) {
        console.error('[UI] Ошибка создания диалога:', error);
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
        await loadDialogueGroups();
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
    
    // Проверка и отображение кнопки "Задачи"
    checkAndShowTasksButton();
    
    console.log('[UI] Диалог выбран:', currentDialogueId);
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
        
        // Удаляем прогресс-контейнер выполнения задач если он есть
        const taskExecutionProgress = document.getElementById('task-execution-progress');
        if (taskExecutionProgress) {
            console.log('[Tasks] Удаление прогресс-контейнера выполнения задач');
            taskExecutionProgress.remove();
        }
        
        // Проверяем, завершилось ли выполнение задач (по содержимому сообщения)
        const content = payload.content || payload.Content || '';
        if (content.includes('✅') && content.includes('Задачи успешно выполнены')) {
            console.log('[Tasks] Обнаружено завершение выполнения задач, воспроизводим звук');
            playNotificationSound();
        }
        
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
        
        // Включаем кнопку "Выполнить задачи" обратно
        const executeTasksButton = document.getElementById('execute-tasks-button');
        if (executeTasksButton) {
            executeTasksButton.disabled = false;
        }
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
    
    // Обработчик прогресса выполнения задач
    client.on('task_progress', (payload) => {
        console.log('[Tasks] Получен прогресс задачи:', payload);
        handleTaskProgress(payload);
    });
    
    // Обработчик прогресса генерации плана задач
    client.on('plan_generation_progress', (payload) => {
        console.log('[Tasks] Получен прогресс генерации плана:', payload);
        updatePlanGenerationProgress(payload);
    });
    
    // Обработчик прогресса выполнения задач
    client.on('task_execution_progress', (payload) => {
        console.log('[Tasks] Получен прогресс выполнения задач:', payload);
        updateTaskExecutionProgress(payload);
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
            
            // Автопрокрутка к последнему сообщению
            setTimeout(() => {
                const messageList = document.getElementById('message-list');
                messageList.scrollTop = messageList.scrollHeight;
            }, 100);
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
            
            // Автопрокрутка к последнему сообщению
            setTimeout(() => {
                const messageList = document.getElementById('message-list');
                messageList.scrollTop = messageList.scrollHeight;
            }, 100);
        } else {
            console.log('[MessageCache] Изменений не обнаружено, UI не обновляется');
            
            // Автопрокрутка к последнему сообщению даже если нет изменений
            setTimeout(() => {
                const messageList = document.getElementById('message-list');
                messageList.scrollTop = messageList.scrollHeight;
            }, 100);
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
    console.log('[UI] sendMessage вызвана');
    console.log('[UI] currentDialogueId:', currentDialogueId);
    console.log('[UI] isProcessing:', isProcessing);
    console.log('[UI] wsClient:', wsClient);
    console.log('[UI] wsClient.isConnected:', wsClient?.isConnected);
    console.log('[UI] wsClient.isUsingHttp:', wsClient?.isUsingHttp);
    
    if (!currentDialogueId) {
        alert('Выберите диалог');
        return;
    }
    
    if (isProcessing) {
        console.log('[UI] Отмена: уже обрабатывается другое сообщение');
        return;
    }
    
    // Проверяем, есть ли у текущего диалога группа с контекстом
    const currentDialogue = dialogues.find(d => d.id === currentDialogueId);
    if (currentDialogue && currentDialogue.dialogueGroupId) {
        const group = dialogueGroups.find(g => g.id === currentDialogue.dialogueGroupId);
        if (group) {
            const hasContext = group.requirements || group.design || group.tasks;
            if (hasContext) {
                console.log(`[Context] Диалог использует контекст группы "${group.name}"`);
                if (group.requirements) console.log('[Context] ✓ Requirements загружены');
                if (group.design) console.log('[Context] ✓ Design загружен');
                if (group.tasks) console.log('[Context] ✓ Tasks загружены');
            }
        }
    }
    
    const input = document.getElementById('prompt-input');
    const content = input.value.trim();
    
    console.log('[UI] Содержимое сообщения:', content);
    
    if (!content) {
        console.log('[UI] Отмена: пустое сообщение');
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

// Функция удаления сообщения
async function deleteMessage(messageId) {
    if (!confirm('Вы уверены, что хотите удалить это сообщение?')) {
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/messages/${messageId}`, {
            method: 'DELETE'
        });
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        // Удаляем элемент из DOM
        const messageElement = document.querySelector(`[data-message-id="${messageId}"]`);
        if (messageElement) {
            messageElement.remove();
        }
        
        // Перезагружаем сообщения для обновления кэша
        if (currentDialogueId) {
            await loadMessages(currentDialogueId);
        }
        
        console.log(`[UI] Сообщение ${messageId} удалено`);
    } catch (error) {
        console.error('Error deleting message:', error);
        alert('Ошибка при удалении сообщения');
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
function createMessageElement(role, content, messageId = null) {
    const messageDiv = document.createElement('div');
    messageDiv.className = `message ${role}`;
    
    // Сохраняем messageId как data-атрибут если он есть
    if (messageId) {
        messageDiv.dataset.messageId = messageId;
    }
    
    const roleDiv = document.createElement('div');
    roleDiv.className = 'message-role';
    roleDiv.textContent = role === 'user' ? 'Вы' : 'Ассистент';
    
    const contentDiv = document.createElement('div');
    contentDiv.className = 'message-content';
    contentDiv.textContent = content;
    
    messageDiv.appendChild(roleDiv);
    messageDiv.appendChild(contentDiv);
    
    // Добавляем кнопку удаления если есть messageId
    if (messageId) {
        const deleteBtn = document.createElement('button');
        deleteBtn.className = 'message-delete-btn';
        deleteBtn.innerHTML = '🗑️';
        deleteBtn.title = 'Удалить сообщение';
        deleteBtn.onclick = () => deleteMessage(messageId);
        messageDiv.appendChild(deleteBtn);
    }
    
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
    
    const reasoningIndicator = document.getElementById('reasoning-model-status-indicator');
    const reasoningIcon = document.querySelector('.reasoning-model-status-icon');
    const reasoningLabel = document.querySelector('.reasoning-model-status-label');
    
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
            modelLabel.textContent = result.modelName || 'неизвестно';
            if (modelIcon) modelIcon.title = 'Модель активна';
            startButton.style.display = 'none';
            console.log('[ModelValidation] ✓ Модель активна:', result.modelName);
        } else {
            // Модель неактивна
            modelIndicator.className = 'model-inactive';
            modelLabel.textContent = result.modelName || 'Модель';
            if (modelIcon) modelIcon.title = result.errorMessage || 'Модель недоступна';
            
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
        
        // Проверка reasoning модели
        if (reasoningIndicator && reasoningLabel) {
            try {
                const reasoningResponse = await fetch(`${API_BASE}/api/startup/validate-reasoning`);
                const reasoningResult = await reasoningResponse.json();
                
                console.log('[ModelValidation] Результат проверки reasoning модели:', reasoningResult);
                
                const startReasoningButton = document.getElementById('start-reasoning-model-button');
                
                if (reasoningResult.isConnected) {
                    reasoningIndicator.className = 'model-active';
                    reasoningLabel.textContent = reasoningResult.modelName || 'неизвестно';
                    if (reasoningIcon) reasoningIcon.title = 'Reasoning модель активна';
                    if (startReasoningButton) startReasoningButton.style.display = 'none';
                    console.log('[ModelValidation] ✓ Reasoning модель активна:', reasoningResult.modelName);
                } else {
                    reasoningIndicator.className = 'model-inactive';
                    reasoningLabel.textContent = reasoningResult.modelName || 'Reasoning модель';
                    if (reasoningIcon) reasoningIcon.title = reasoningResult.errorMessage || 'Reasoning модель недоступна';
                    console.warn('[ModelValidation] ✗ Reasoning модель неактивна:', reasoningResult.errorMessage);
                    
                    // Показываем кнопку запуска всегда, когда модель неактивна
                    if (startReasoningButton) {
                        startReasoningButton.style.display = 'block';
                        console.log('[ModelValidation] Показана кнопка запуска reasoning модели');
                    }
                }
            } catch (error) {
                console.error('[ModelValidation] Ошибка при проверке reasoning модели:', error);
                reasoningIndicator.className = 'model-inactive';
                reasoningLabel.textContent = 'Reasoning модель';
                if (reasoningIcon) reasoningIcon.title = 'Ошибка проверки reasoning модели';
            }
        }
    } catch (error) {
        console.error('[ModelValidation] Ошибка при проверке подключения:', error);
        modelIndicator.className = 'model-inactive';
        modelLabel.textContent = 'Модель';
        if (modelIcon) modelIcon.title = 'Ошибка проверки';
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

// Функция для запуска reasoning модели Ollama
async function startReasoningModel() {
    const btn = document.getElementById('start-reasoning-model-button');
    if (!btn) return;
    
    const originalText = btn.textContent;
    btn.textContent = '⏳ Запуск...';
    btn.disabled = true;
    
    try {
        console.log('[StartReasoningModel] Отправка запроса на /api/ollama/start-reasoning');
        
        const response = await fetch('/api/ollama/start-reasoning', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        console.log('[StartReasoningModel] Получен ответ:', response.status, response.statusText);
        
        if (response.ok) {
            const result = await response.json();
            console.log('[StartReasoningModel] Успешный результат:', result);
            
            btn.textContent = '✓ Запущено!';
            btn.style.background = '#28a745';
            
            // Ждем 5 секунд, чтобы модель полностью загрузилась в Ollama
            console.log('[StartReasoningModel] Ожидание загрузки модели (5 секунд)...');
            await new Promise(resolve => setTimeout(resolve, 5000));
            
            // Повторная валидация моделей
            console.log('[StartReasoningModel] Повторная валидация моделей...');
            await validateModelConnection();
            
            // Сброс кнопки
            setTimeout(() => {
                btn.textContent = originalText;
                btn.disabled = false;
                btn.style.background = '';
            }, 2000);
        } else {
            console.error('[StartReasoningModel] Ошибка HTTP:', response.status);
            
            let errorMessage = 'Неизвестная ошибка';
            const contentType = response.headers.get('content-type');
            
            console.log('[StartReasoningModel] Content-Type:', contentType);
            
            try {
                if (contentType && contentType.includes('application/json')) {
                    const errorData = await response.json();
                    console.error('[StartReasoningModel] JSON ошибка:', errorData);
                    errorMessage = errorData.message || errorData.detail || errorData.title || JSON.stringify(errorData);
                } else if (contentType && contentType.includes('application/problem+json')) {
                    const errorData = await response.json();
                    console.error('[StartReasoningModel] Problem JSON:', errorData);
                    errorMessage = errorData.detail || errorData.title || JSON.stringify(errorData);
                } else {
                    const textError = await response.text();
                    console.error('[StartReasoningModel] Text ошибка:', textError);
                    errorMessage = textError || `HTTP ${response.status}: ${response.statusText}`;
                }
            } catch (parseError) {
                console.error('[StartReasoningModel] Ошибка парсинга ответа:', parseError);
                errorMessage = `HTTP ${response.status}: ${response.statusText}`;
            }
            
            btn.textContent = '✗ Ошибка';
            btn.style.background = '#dc3545';
            
            console.error('[StartReasoningModel] Финальное сообщение об ошибке:', errorMessage);
            alert(`Не удалось запустить reasoning модель:\n\n${errorMessage}\n\nПроверьте:\n1. Установлена ли Ollama\n2. Запущен ли сервис Ollama\n3. Доступна ли модель deepseek-r1:7b`);
            
            setTimeout(() => {
                btn.textContent = originalText;
                btn.disabled = false;
                btn.style.background = '';
            }, 3000);
        }
    } catch (error) {
        btn.textContent = '✗ Ошибка';
        btn.style.background = '#dc3545';
        
        console.error('[StartReasoningModel] Исключение:', error);
        console.error('[StartReasoningModel] Stack trace:', error.stack);
        alert(`Ошибка запуска: ${error.message}\n\nПроверьте консоль браузера и логи сервера для подробностей.`);
        
        setTimeout(() => {
            btn.textContent = originalText;
            btn.disabled = false;
            btn.style.background = '';
        }, 3000);
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
    
    // Получаем кнопку переподключения
    const reconnectButton = document.getElementById('reconnect-websocket-button');
    
    // Удаление всех классов статуса
    indicator.className = '';
    
    // Обновление метрики статуса соединения
    updatePerformanceMetric('connectionStatus', status);
    
    // Получаем иконку статуса
    const statusIcon = indicator.querySelector('.status-icon');
    
    // Установка нового статуса и текста
    switch (status) {
        case 'connected':
            indicator.classList.add('status-connected');
            statusLabel.textContent = 'WebSocket';
            if (statusIcon) statusIcon.title = 'Подключено';
            if (reconnectButton) reconnectButton.style.display = 'none';
            console.log('[UI] Статус обновлен: WebSocket подключен');
            break;
            
        case 'disconnected':
            indicator.classList.add('status-disconnected');
            statusLabel.textContent = 'WebSocket';
            if (statusIcon) statusIcon.title = 'Отключено';
            if (reconnectButton) reconnectButton.style.display = 'inline-block';
            console.log('[UI] Статус обновлен: Отключено');
            break;
            
        case 'error':
            indicator.classList.add('status-error');
            statusLabel.textContent = 'WebSocket';
            if (statusIcon) statusIcon.title = 'Ошибка соединения';
            if (reconnectButton) reconnectButton.style.display = 'inline-block';
            console.log('[UI] Статус обновлен: Ошибка соединения');
            break;
            
        case 'reconnecting':
            indicator.classList.add('status-reconnecting');
            statusLabel.textContent = 'WebSocket';
            if (data && data.attempt) {
                if (statusIcon) statusIcon.title = `Переподключение (попытка ${data.attempt}/5)...`;
            } else {
                if (statusIcon) statusIcon.title = 'Переподключение...';
            }
            if (reconnectButton) reconnectButton.style.display = 'none';
            console.log('[UI] Статус обновлен: Переподключение', data);
            break;
            
        case 'http_fallback':
            indicator.classList.add('status-http');
            statusLabel.textContent = 'WebSocket';
            if (statusIcon) statusIcon.title = 'HTTP режим';
            if (reconnectButton) reconnectButton.style.display = 'inline-block';
            console.log('[UI] Статус обновлен: HTTP режим');
            break;
            
        default:
            indicator.classList.add('status-disconnected');
            statusLabel.textContent = 'WebSocket';
            if (statusIcon) statusIcon.title = 'Неизвестный статус';
            if (reconnectButton) reconnectButton.style.display = 'inline-block';
            console.warn('[UI] Неизвестный статус соединения:', status);
    }
}

/**
 * Функция для ручного переподключения WebSocket
 */
async function reconnectWebSocket() {
    console.log('[UI] Ручное переподключение WebSocket...');
    
    if (!currentDialogueId) {
        alert('Сначала выберите диалог');
        return;
    }
    
    // Отключаем старое соединение если есть
    if (wsClient) {
        wsClient.disconnect();
        wsClient = null;
    }
    
    // Создаем новое соединение
    wsClient = new WebSocketClient(currentDialogueId);
    
    // Установка обработчика изменения статуса соединения
    wsClient.onConnectionChange = (status, data) => {
        console.log(`[UI] Статус WebSocket соединения изменен: ${status}`, data);
        updateConnectionStatus(status, data);
    };
    
    // Регистрация обработчиков входящих сообщений
    registerWebSocketHandlers(wsClient);
    
    // Установка WebSocket соединения
    try {
        await wsClient.connect();
        console.log('[UI] WebSocket переподключен успешно');
    } catch (error) {
        console.error('[UI] Ошибка переподключения WebSocket:', error);
        alert('Не удалось переподключить WebSocket: ' + error.message);
    }
}


// ========================================
// Утилиты для отладки и сброса состояния
// ========================================

/**
 * Сброс состояния приложения (очистка localStorage и перезагрузка)
 * Используйте в консоли браузера: resetAppState()
 */
function resetAppState() {
    console.log('[Debug] Сброс состояния приложения...');
    
    // Очистка localStorage
    if (messageCache) {
        console.log('[Debug] Очистка кэша сообщений...');
        Object.keys(localStorage).forEach(key => {
            if (key.startsWith('msg_cache_')) {
                localStorage.removeItem(key);
            }
        });
    }
    
    if (draftManager) {
        console.log('[Debug] Очистка черновиков...');
        Object.keys(localStorage).forEach(key => {
            if (key.startsWith('draft_')) {
                localStorage.removeItem(key);
            }
        });
    }
    
    // Очистка других данных
    console.log('[Debug] Очистка других данных localStorage...');
    localStorage.clear();
    
    // Отключение WebSocket
    if (wsClient) {
        console.log('[Debug] Отключение WebSocket...');
        wsClient.disconnect();
    }
    
    console.log('[Debug] Состояние очищено. Перезагрузка страницы...');
    
    // Перезагрузка страницы
    setTimeout(() => {
        window.location.reload();
    }, 500);
}

// Делаем функцию доступной глобально для использования из консоли
window.resetAppState = resetAppState;

console.log('[Debug] Функция resetAppState() доступна в консоли браузера');


// ============================================
// Функции для работы с группами диалогов
// ============================================

/**
 * Создание новой группы диалогов
 */
async function createDialogueGroup() {
    console.log('[UI] Попытка создания группы диалогов...');
    
    // Проверка выбранного проекта
    const selectedProject = projects.find(p => p.isSelected);
    
    if (!selectedProject) {
        console.error('[UI] Проект не выбран!');
        alert('Выберите проект из списка');
        return;
    }
    
    // Запрашиваем название группы
    const groupName = prompt('Введите название группы диалогов:');
    
    if (!groupName || groupName.trim() === '') {
        console.log('[UI] Создание группы отменено');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogue-groups`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                name: groupName.trim(),
                projectPath: selectedProject.path 
            })
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        const group = await response.json();
        console.log('[UI] Группа создана:', group);
        
        // Перезагружаем список групп и диалогов
        await loadDialogueGroups();
        
    } catch (error) {
        console.error('[UI] Ошибка создания группы:', error);
        alert('Ошибка создания группы: ' + error.message);
    }
}

/**
 * Переключение состояния группы (свернуть/развернуть)
 */
async function toggleGroup(groupId) {
    console.log('[UI] Переключение группы:', groupId);
    
    const groupElement = document.querySelector(`.dialogue-group[data-group-id="${groupId}"]`);
    if (!groupElement) {
        console.error('[UI] Элемент группы не найден');
        return;
    }
    
    // Переключаем класс collapsed
    const isCollapsed = groupElement.classList.toggle('collapsed');
    
    try {
        // Находим группу в массиве
        const group = dialogueGroups.find(g => g.id === groupId);
        if (!group) {
            console.error('[UI] Группа не найдена в массиве');
            return;
        }
        
        // Отправляем обновление на сервер
        const response = await fetch(`${API_BASE}/api/dialogue-groups/${groupId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                name: group.name,
                isCollapsed: isCollapsed 
            })
        });
        
        if (!response.ok) {
            throw new Error('Не удалось обновить состояние группы');
        }
        
        console.log('[UI] Состояние группы обновлено:', isCollapsed ? 'свернута' : 'развернута');
        
    } catch (error) {
        console.error('[UI] Ошибка обновления состояния группы:', error);
        // Откатываем изменение в UI
        groupElement.classList.toggle('collapsed');
    }
}

/**
 * Создание диалога в группе
 */
async function createDialogueInGroup(groupId) {
    console.log('[UI] Создание диалога в группе:', groupId);
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogue-groups/${groupId}/dialogues`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        const dialogue = await response.json();
        console.log('[UI] Диалог создан в группе:', dialogue);
        
        // Перезагружаем список групп и диалогов
        await loadDialogueGroups();
        
        // Выбираем новый диалог
        selectDialogue(dialogue.id);
        
    } catch (error) {
        console.error('[UI] Ошибка создания диалога в группе:', error);
        alert('Ошибка создания диалога: ' + error.message);
    }
}

/**
 * Переименование группы диалогов
 */
async function renameDialogueGroup(groupId) {
    console.log('[UI] Попытка переименования группы:', groupId);
    
    // Находим группу в массиве
    const group = dialogueGroups.find(g => g.id === groupId);
    if (!group) {
        console.error('[UI] Группа не найдена');
        return;
    }
    
    // Запрашиваем новое название
    const newName = prompt('Введите новое название группы:', group.name);
    
    if (!newName || newName.trim() === '') {
        console.log('[UI] Переименование отменено');
        return;
    }
    
    // Если название не изменилось, ничего не делаем
    if (newName.trim() === group.name) {
        console.log('[UI] Название не изменилось');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogue-groups/${groupId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                name: newName.trim(),
                isCollapsed: group.isCollapsed 
            })
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        console.log('[UI] Группа переименована');
        
        // Перезагружаем список групп и диалогов
        await loadDialogueGroups();
        
        // Показываем уведомление
        showStatusMessage('Группа переименована', 'success');
        
    } catch (error) {
        console.error('[UI] Ошибка переименования группы:', error);
        alert('Ошибка переименования группы: ' + error.message);
    }
}

/**
 * Удаление группы диалогов
 */
async function deleteDialogueGroup(groupId) {
    console.log('[UI] Попытка удаления группы:', groupId);
    
    if (!confirm('Вы уверены, что хотите удалить эту группу? Все диалоги в группе будут также удалены. Это действие нельзя отменить.')) {
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogue-groups/${groupId}`, {
            method: 'DELETE'
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        console.log('[UI] Группа удалена');
        
        // Если удалили группу где был выбранный диалог, очищаем экран
        if (currentDialogueId) {
            const currentDialogue = dialogues.find(d => d.id === currentDialogueId);
            if (currentDialogue && currentDialogue.dialogueGroupId === groupId) {
                currentDialogueId = null;
                document.getElementById('message-list').innerHTML = '<div class="empty-state">Выберите диалог</div>';
                document.getElementById('checkpoint-list').innerHTML = '<div class="empty-state">Нет чекпоинтов</div>';
            }
        }
        
        // Перезагружаем список групп и диалогов
        await loadDialogueGroups();
        
    } catch (error) {
        console.error('[UI] Ошибка удаления группы:', error);
        alert('Ошибка удаления группы: ' + error.message);
    }
}

/**
 * Проверка, все ли задачи в списке выполнены
 */
function areAllTasksCompleted(tasksText) {
    if (!tasksText || tasksText.trim() === '') {
        console.log('[areAllTasksCompleted] Нет текста задач');
        return false;
    }
    
    // Ищем все чекбоксы в формате [ ] или [x]
    const checkboxPattern = /\[([ xX])\]/g;
    const matches = tasksText.match(checkboxPattern);
    
    console.log('[areAllTasksCompleted] Tasks text:', tasksText);
    console.log('[areAllTasksCompleted] Matches:', matches);
    
    if (!matches || matches.length === 0) {
        console.log('[areAllTasksCompleted] Нет чекбоксов');
        return false; // Нет задач с чекбоксами
    }
    
    // Проверяем, все ли чекбоксы отмечены
    const allCompleted = matches.every(match => match === '[x]' || match === '[X]');
    console.log('[areAllTasksCompleted] All completed:', allCompleted);
    return allCompleted;
}

/**
 * Запуск выполнения задач группы
 */
async function executeGroupTasks(groupId) {
    console.log('[UI] Запуск выполнения задач для группы:', groupId);
    
    try {
        // Находим первый диалог в группе
        const group = dialogueGroups.find(g => g.id === groupId);
        if (!group) {
            showNotification('Группа не найдена', 'error');
            return;
        }
        
        const groupDialogues = dialogues.filter(d => d.dialogueGroupId === groupId);
        if (groupDialogues.length === 0) {
            showNotification('В группе нет диалогов. Создайте диалог для выполнения задач.', 'error');
            return;
        }
        
        // Выбираем первый диалог
        const firstDialogue = groupDialogues[0];
        await selectDialogue(firstDialogue.id);
        
        // Запускаем выполнение задач
        const response = await fetch(`/api/dialogues/${firstDialogue.id}/execute-tasks-direct`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Ошибка запуска задач');
        }
        
        showNotification('Задачи запущены', 'success');
    } catch (error) {
        console.error('[UI] Ошибка запуска задач:', error);
        showNotification(`Ошибка: ${error.message}`, 'error');
    }
}

/**
 * Открытие модального окна контекста группы
 */
async function openContextModal(groupId) {
    console.log('[UI] Открытие модального окна контекста для группы:', groupId);
    
    // Сохраняем ID группы для последующего сохранения
    currentGroupId = groupId;
    
    // Находим группу в массиве
    const group = dialogueGroups.find(g => g.id === groupId);
    if (!group) {
        console.error('[UI] Группа не найдена');
        return;
    }
    
    // Заполняем поля модального окна
    const requirementsTextarea = document.getElementById('context-requirements');
    const designTextarea = document.getElementById('context-design');
    const tasksTextarea = document.getElementById('context-tasks');
    
    if (requirementsTextarea) requirementsTextarea.value = group.requirements || '';
    if (designTextarea) designTextarea.value = group.design || '';
    if (tasksTextarea) tasksTextarea.value = group.tasks || '';
    
    // Показываем модальное окно
    const modalOverlay = document.getElementById('context-modal-overlay');
    if (modalOverlay) {
        modalOverlay.classList.add('active');
    }
    
    console.log('[UI] Модальное окно контекста открыто');
}

/**
 * Закрытие модального окна контекста
 */
function closeContextModal() {
    console.log('[UI] Закрытие модального окна контекста');
    
    const modalOverlay = document.getElementById('context-modal-overlay');
    if (modalOverlay) {
        modalOverlay.classList.remove('active');
    }
    
    // Очищаем ID текущей группы
    currentGroupId = null;
    
    // Очищаем поля
    const requirementsTextarea = document.getElementById('context-requirements');
    const designTextarea = document.getElementById('context-design');
    const tasksTextarea = document.getElementById('context-tasks');
    
    if (requirementsTextarea) requirementsTextarea.value = '';
    if (designTextarea) designTextarea.value = '';
    if (tasksTextarea) tasksTextarea.value = '';
}

/**
 * Сохранение контекста группы
 */
async function saveGroupContext() {
    console.log('[UI] Сохранение контекста группы:', currentGroupId);
    
    if (!currentGroupId) {
        console.error('[UI] ID группы не установлен');
        return;
    }
    
    // Получаем значения из полей
    const requirementsTextarea = document.getElementById('context-requirements');
    const designTextarea = document.getElementById('context-design');
    const tasksTextarea = document.getElementById('context-tasks');
    
    const requirements = requirementsTextarea ? requirementsTextarea.value : '';
    const design = designTextarea ? designTextarea.value : '';
    const tasks = tasksTextarea ? tasksTextarea.value : '';
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogue-groups/${currentGroupId}/context`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                requirements: requirements,
                design: design,
                tasks: tasks
            })
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        console.log('[UI] Контекст группы сохранен');
        
        // Закрываем модальное окно
        closeContextModal();
        
        // Перезагружаем список групп для обновления данных
        await loadDialogueGroups();
        
        // Показываем уведомление
        showStatusMessage('Контекст группы сохранен', 'success');
        
        // Проверяем и обновляем кнопку "Задачи"
        checkAndShowTasksButton();
        
    } catch (error) {
        console.error('[UI] Ошибка сохранения контекста:', error);
        alert('Ошибка сохранения контекста: ' + error.message);
    }
}


// ============================================================================
// АВТОМАТИЧЕСКОЕ ПЛАНИРОВАНИЕ И ВЫПОЛНЕНИЕ ЗАДАЧ
// ============================================================================

let currentTaskPlan = null; // Текущий план задач
let runningTasks = new Set(); // Множество ID выполняющихся задач

/**
 * Проверка наличия Tasks в текущей группе и отображение кнопки "Задачи"
 */
async function checkAndShowTasksButton() {
    const tasksButton = document.getElementById('tasks-button');
    const executeTasksButton = document.getElementById('execute-tasks-button');
    const deletePlanButton = document.getElementById('delete-plan-button');
    
    if (!tasksButton || !executeTasksButton || !deletePlanButton) return;
    
    // Проверяем состояние DeepSeek API toggle
    const deepSeekEnabled = await isDeepSeekApiEnabled();
    
    // Проверяем, есть ли текущий диалог и группа с заполненным Tasks
    if (currentDialogueId) {
        const dialogue = dialogues.find(d => d.id === currentDialogueId);
        
        if (dialogue && dialogue.dialogueGroupId) {
            const group = dialogueGroups.find(g => g.id === dialogue.dialogueGroupId);
            
            if (group) {
                // Проверяем заполненность ВСЕХ полей
                const hasRequirements = group.requirements && group.requirements.trim();
                const hasDesign = group.design && group.design.trim();
                const hasTasks = group.tasks && group.tasks.trim();
                
                if (hasRequirements && hasDesign && hasTasks) {
                    // Все поля заполнены
                    
                    // Кнопка "Выполнить задачи" показывается ВСЕГДА если DeepSeek API включен и все поля заполнены
                    if (deepSeekEnabled) {
                        executeTasksButton.style.display = 'flex';
                        executeTasksButton.disabled = false;
                    } else {
                        executeTasksButton.style.display = 'none';
                    }
                    
                    // Проверяем существует ли уже план (для кнопок создания/удаления плана)
                    try {
                        const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/task-plan`);
                        
                        if (response.ok) {
                            // План существует - показываем кнопку удаления, скрываем кнопку создания
                            tasksButton.style.display = 'none';
                            deletePlanButton.style.display = 'flex';
                        } else if (response.status === 404) {
                            // План не существует - показываем кнопку создания, скрываем кнопку удаления
                            tasksButton.style.display = 'flex';
                            tasksButton.disabled = false;
                            tasksButton.title = 'Сгенерировать план';
                            deletePlanButton.style.display = 'none';
                        } else {
                            // Ошибка - скрываем обе кнопки
                            tasksButton.style.display = 'none';
                            deletePlanButton.style.display = 'none';
                        }
                    } catch (error) {
                        console.error('[Tasks] Ошибка проверки плана:', error);
                        // При ошибке показываем кнопку создания как активную
                        tasksButton.style.display = 'flex';
                        tasksButton.disabled = false;
                        tasksButton.title = 'Сгенерировать план';
                        deletePlanButton.style.display = 'none';
                    }
                    return;
                }
            }
        }
    }
    
    // Если условия не выполнены - скрываем все кнопки
    tasksButton.style.display = 'none';
    executeTasksButton.style.display = 'none';
    deletePlanButton.style.display = 'none';
}

// Функция loadTaskPlanIfExists удалена - функционал планов задач больше не используется

/**
 * Открытие панели задач и создание плана
 */
async function openTaskPanel() {
    console.log('[Tasks] Открытие панели задач');
    
    if (!currentDialogueId) {
        alert('Выберите диалог');
        return;
    }
    
    // Проверяем заполненность всех полей контекста группы
    const dialogue = dialogues.find(d => d.id === currentDialogueId);
    if (!dialogue || !dialogue.dialogueGroupId) {
        alert('Диалог не принадлежит ни одной группе');
        return;
    }
    
    const group = dialogueGroups.find(g => g.id === dialogue.dialogueGroupId);
    if (!group) {
        alert('Группа диалогов не найдена');
        return;
    }
    
    // Валидация заполненности полей
    const missingFields = [];
    
    if (!group.requirements || !group.requirements.trim()) {
        missingFields.push('Требования (requirements.md)');
    }
    
    if (!group.design || !group.design.trim()) {
        missingFields.push('Проектирование (design.md)');
    }
    
    if (!group.tasks || !group.tasks.trim()) {
        missingFields.push('Задачи (tasks.md)');
    }
    
    if (missingFields.length > 0) {
        const fieldsList = missingFields.join('\n• ');
        alert(`Для генерации плана необходимо заполнить следующие поля:\n\n• ${fieldsList}\n\nОткройте контекст группы и заполните все поля.`);
        return;
    }
    
    try {
        // Показываем панель задач
        const app = document.getElementById('app');
        const taskPanel = document.getElementById('task-panel');
        
        app.classList.add('with-task-panel');
        taskPanel.style.display = 'flex';
        
        // Показываем loader
        const taskList = document.getElementById('task-list');
        taskList.innerHTML = '<div style="text-align: center; padding: 40px; color: #666;">Загрузка плана задач...<br><div class="task-loader" style="margin: 20px auto;"></div></div>';
        
        console.log('[Tasks] Проверка существующего плана для диалога:', currentDialogueId);
        
        // Сначала пытаемся загрузить существующий план
        let response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/task-plan`);
        
        if (response.status === 404) {
            // План не существует, создаем новый
            console.log('[Tasks] План не найден, создаем новый');
            taskList.innerHTML = '<div style="text-align: center; padding: 40px; color: #666;">Создание плана задач...<br><div class="task-loader" style="margin: 20px auto;"></div></div>';
            
            response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/plan-tasks`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
        } else {
            console.log('[Tasks] Загружен существующий план');
        }
        
        console.log('[Tasks] Получен ответ:', response.status, response.statusText);
        
        if (!response.ok) {
            const errorText = await response.text();
            console.error('[Tasks] Ошибка ответа:', errorText);
            
            let errorMessage = 'Ошибка загрузки плана';
            try {
                const error = JSON.parse(errorText);
                errorMessage = error.message || error.title || errorText;
            } catch {
                errorMessage = errorText || 'Неизвестная ошибка';
            }
            
            throw new Error(errorMessage);
        }
        
        currentTaskPlan = await response.json();
        console.log('[Tasks] План загружен:', currentTaskPlan);
        
        if (!currentTaskPlan || !currentTaskPlan.tasks || currentTaskPlan.tasks.length === 0) {
            throw new Error('План задач пуст');
        }
        
        // Отображаем план
        renderTaskPlan();
        
        // Обновляем кнопки - показываем кнопку удаления вместо создания
        await checkAndShowTasksButton();
        
    } catch (error) {
        console.error('[Tasks] Ошибка открытия панели задач:', error);
        alert('Ошибка загрузки плана задач: ' + error.message);
        closeTaskPanel();
    }
}

/**
 * Закрытие панели задач
 */
function closeTaskPanel() {
    const app = document.getElementById('app');
    const taskPanel = document.getElementById('task-panel');
    
    app.classList.remove('with-task-panel');
    taskPanel.style.display = 'none';
    
    // НЕ очищаем currentTaskPlan - план остается в памяти
    // currentTaskPlan = null;
    runningTasks.clear();
}

/**
 * Обновление прогресса генерации плана задач
 */
function updatePlanGenerationProgress(payload) {
    console.log('[Tasks] Обновление прогресса генерации:', payload);
    
    const taskList = document.getElementById('task-list');
    if (!taskList) {
        console.warn('[Tasks] Элемент task-list не найден');
        return;
    }
    
    // Проверяем, есть ли уже прогресс-бар
    let progressContainer = document.getElementById('plan-generation-progress');
    
    if (!progressContainer) {
        // Создаем контейнер для прогресса
        progressContainer = document.createElement('div');
        progressContainer.id = 'plan-generation-progress';
        progressContainer.style.textAlign = 'center';
        progressContainer.style.padding = '40px';
        progressContainer.style.color = '#666';
        
        // Добавляем лоадер
        const loaderDiv = document.createElement('div');
        loaderDiv.className = 'task-loader';
        loaderDiv.style.margin = '20px auto';
        
        // Добавляем текст прогресса
        const progressText = document.createElement('div');
        progressText.id = 'plan-progress-text';
        progressText.style.marginTop = '20px';
        progressText.style.fontSize = '14px';
        progressText.style.fontWeight = '500';
        
        // Добавляем прогресс-бар
        const progressBarContainer = document.createElement('div');
        progressBarContainer.style.width = '100%';
        progressBarContainer.style.maxWidth = '400px';
        progressBarContainer.style.height = '8px';
        progressBarContainer.style.backgroundColor = '#e0e0e0';
        progressBarContainer.style.borderRadius = '4px';
        progressBarContainer.style.margin = '15px auto';
        progressBarContainer.style.overflow = 'hidden';
        
        const progressBar = document.createElement('div');
        progressBar.id = 'plan-progress-bar';
        progressBar.style.height = '100%';
        progressBar.style.backgroundColor = '#4CAF50';
        progressBar.style.width = '0%';
        progressBar.style.transition = 'width 0.3s ease';
        
        progressBarContainer.appendChild(progressBar);
        
        progressContainer.appendChild(loaderDiv);
        progressContainer.appendChild(progressText);
        progressContainer.appendChild(progressBarContainer);
        
        // Очищаем taskList и добавляем прогресс
        taskList.innerHTML = '';
        taskList.appendChild(progressContainer);
    }
    
    // Обновляем текст и прогресс-бар
    const progressText = document.getElementById('plan-progress-text');
    const progressBar = document.getElementById('plan-progress-bar');
    
    if (progressText && payload.message) {
        progressText.textContent = payload.message;
    }
    
    if (progressBar && payload.current !== undefined && payload.total !== undefined) {
        const percentage = payload.total > 0 ? (payload.current / payload.total) * 100 : 0;
        progressBar.style.width = `${percentage}%`;
        
        console.log(`[Tasks] Прогресс: ${payload.current}/${payload.total} (${percentage.toFixed(1)}%)`);
    }
    
    // Если генерация завершена (current === total), удаляем прогресс через небольшую задержку
    if (payload.current === payload.total && payload.total > 0) {
        setTimeout(() => {
            const container = document.getElementById('plan-generation-progress');
            if (container) {
                console.log('[Tasks] Генерация завершена, удаляем прогресс-бар');
                // Не удаляем контейнер, так как план будет загружен и отображен автоматически
            }
        }, 1000);
    }
}

/**
 * Воспроизведение звука уведомления
 */
function playNotificationSound() {
    try {
        const audio = new Audio('/sound.mp3');
        audio.volume = 0.5; // Громкость 50%
        audio.play().catch(error => {
            console.warn('[Sound] Не удалось воспроизвести звук:', error);
        });
    } catch (error) {
        console.error('[Sound] Ошибка воспроизведения звука:', error);
    }
}

/**
 * Обновление прогресса выполнения задач
 */
function updateTaskExecutionProgress(payload) {
    console.log('[Tasks] Обновление прогресса выполнения:', payload);
    
    const messageList = document.getElementById('message-list');
    if (!messageList) {
        console.warn('[Tasks] Элемент message-list не найден');
        return;
    }
    
    // Проверяем, есть ли уже прогресс-контейнер
    let progressContainer = document.getElementById('task-execution-progress');
    
    if (!progressContainer) {
        // Создаем контейнер для прогресса в чате
        const messageDiv = document.createElement('div');
        messageDiv.className = 'message assistant';
        messageDiv.id = 'task-execution-progress';
        
        const roleDiv = document.createElement('div');
        roleDiv.className = 'message-role';
        roleDiv.textContent = 'Ассистент';
        
        const contentDiv = document.createElement('div');
        contentDiv.className = 'message-content';
        contentDiv.style.padding = '20px';
        
        // Добавляем лоадер
        const loaderDiv = document.createElement('div');
        loaderDiv.className = 'task-loader';
        loaderDiv.style.margin = '10px auto';
        
        // Добавляем текст прогресса
        const progressText = document.createElement('div');
        progressText.id = 'task-execution-text';
        progressText.style.marginTop = '15px';
        progressText.style.fontSize = '14px';
        progressText.style.fontWeight = '500';
        progressText.style.textAlign = 'center';
        
        // Добавляем прогресс-бар
        const progressBarContainer = document.createElement('div');
        progressBarContainer.style.width = '100%';
        progressBarContainer.style.height = '8px';
        progressBarContainer.style.backgroundColor = '#e0e0e0';
        progressBarContainer.style.borderRadius = '4px';
        progressBarContainer.style.margin = '15px 0';
        progressBarContainer.style.overflow = 'hidden';
        
        const progressBar = document.createElement('div');
        progressBar.id = 'task-execution-bar';
        progressBar.style.height = '100%';
        progressBar.style.backgroundColor = '#4CAF50';
        progressBar.style.width = '0%';
        progressBar.style.transition = 'width 0.3s ease';
        
        progressBarContainer.appendChild(progressBar);
        
        contentDiv.appendChild(loaderDiv);
        contentDiv.appendChild(progressText);
        contentDiv.appendChild(progressBarContainer);
        
        messageDiv.appendChild(roleDiv);
        messageDiv.appendChild(contentDiv);
        
        messageList.appendChild(messageDiv);
        messageList.scrollTop = messageList.scrollHeight;
        
        progressContainer = messageDiv;
    }
    
    // Обновляем текст и прогресс-бар
    const progressText = document.getElementById('task-execution-text');
    const progressBar = document.getElementById('task-execution-bar');
    
    if (progressText && payload.message) {
        progressText.textContent = payload.message;
    }
    
    if (progressBar && payload.current !== undefined && payload.total !== undefined && payload.total > 0) {
        const percentage = (payload.current / payload.total) * 100;
        progressBar.style.width = `${percentage}%`;
        
        console.log(`[Tasks] Прогресс выполнения: ${payload.current}/${payload.total} (${percentage.toFixed(1)}%)`);
    }
    
    // Прокручиваем вниз
    messageList.scrollTop = messageList.scrollHeight;
}

/**
 * Рекурсивное отображение дерева папок
 */
function renderFolderTree(node, prefix = '') {
    if (!node) return '';
    
    let html = '';
    const isRoot = prefix === '';
    const hasChildren = node.children && node.children.length > 0;
    
    // Корневая папка
    if (isRoot) {
        html += `<div class="folder-tree-item">${escapeHtml(node.name)}/</div>`;
        
        if (hasChildren) {
            node.children.forEach((child, index) => {
                const isLast = index === node.children.length - 1;
                html += renderFolderTreeNode(child, '', isLast);
            });
        }
    }
    
    return html;
}

/**
 * Отображение узла дерева папок
 */
function renderFolderTreeNode(node, prefix, isLast) {
    if (!node) return '';
    
    let html = '';
    const connector = isLast ? '└── ' : '├── ';
    const hasChildren = node.children && node.children.length > 0;
    const icon = hasChildren ? '📁' : '📄';
    
    html += `<div class="folder-tree-item">${prefix}${connector}${icon} ${escapeHtml(node.name)}${hasChildren ? '/' : ''}</div>`;
    
    if (hasChildren) {
        const childPrefix = prefix + (isLast ? '    ' : '│   ');
        node.children.forEach((child, index) => {
            const childIsLast = index === node.children.length - 1;
            html += renderFolderTreeNode(child, childPrefix, childIsLast);
        });
    }
    
    return html;
}

/**
 * Отрисовка плана задач
 */
function renderTaskPlan() {
    if (!currentTaskPlan || !currentTaskPlan.tasks) {
        console.error('[Tasks] Нет плана для отрисовки');
        return;
    }
    
    const taskList = document.getElementById('task-list');
    taskList.innerHTML = '';
    
    currentTaskPlan.tasks.forEach(task => {
        const taskElement = createTaskElement(task);
        taskList.appendChild(taskElement);
    });
}

/**
 * Создание HTML элемента задачи
 */
function createTaskElement(task) {
    const taskDiv = document.createElement('div');
    taskDiv.className = 'task-item';
    taskDiv.dataset.taskId = task.id;
    
    // Определяем иконку статуса
    let statusIcon = '○';
    let statusClass = 'task-status-pending';
    
    switch (task.status) {
        case 'Running':
            statusIcon = '◐';
            statusClass = 'task-status-running';
            break;
        case 'Completed':
            statusIcon = '✓';
            statusClass = 'task-status-completed';
            break;
        case 'Failed':
            statusIcon = '✗';
            statusClass = 'task-status-failed';
            break;
        case 'Stopped':
            statusIcon = '⏹';
            statusClass = 'task-status-stopped';
            break;
    }
    
    // Заголовок задачи
    const headerHtml = `
        <div class="task-header" onclick="toggleTaskContent(${task.id})">
            <div class="task-title">
                <span class="task-status-icon ${statusClass}">${statusIcon}</span>
                <span>${task.title}</span>
                <span class="material-icons" style="font-size: 18px; margin-left: auto;">expand_more</span>
            </div>
            <div class="task-controls" onclick="event.stopPropagation()">
                ${task.status === 'Running' ? 
                    `<button class="task-btn task-btn-stop" onclick="stopTask(${task.id})">
                        <span class="material-icons" style="font-size: 14px;">stop</span> Остановить
                    </button>
                    <div class="task-loader"></div>` :
                    `<button class="task-btn task-btn-run" onclick="executeTask(${task.id})">
                        <span class="material-icons" style="font-size: 14px;">play_arrow</span> Запустить
                    </button>`
                }
            </div>
        </div>
    `;
    
    // Содержимое задачи
    let contentHtml = `<div class="task-content" id="task-content-${task.id}">`;
    
    if (task.description) {
        contentHtml += `<div class="task-description">${escapeHtml(task.description)}</div>`;
    }
    
    // Отображение структуры папок в виде дерева
    if (task.folderStructure) {
        contentHtml += `
            <div class="task-folders">
                <div class="task-folders-title">📁 Структура проекта:</div>
                <div class="folder-tree">
                    ${renderFolderTree(task.folderStructure, '')}
                </div>
            </div>
        `;
    }
    
    if (task.subTasks && task.subTasks.length > 0) {
        contentHtml += `<div class="subtask-list">`;
        contentHtml += `<div class="task-folders-title" style="margin-top: 12px; margin-bottom: 8px;">📄 Файлы (${task.subTasks.length}):</div>`;
        
        task.subTasks.forEach(subtask => {
            const subtaskId = `${task.id}-${subtask.id}`;
            contentHtml += `
                <div class="subtask-item">
                    <div class="subtask-header" onclick="toggleSubtaskCode(${task.id}, ${subtask.id})">
                        <span class="subtask-title">${escapeHtml(subtask.filePath || subtask.title)}</span>
                        <span class="material-icons" style="font-size: 16px;">code</span>
                    </div>
                    <div class="subtask-code" id="subtask-code-${subtaskId}">${escapeHtml(subtask.code || '// Код не указан')}</div>
                </div>
            `;
        });
        contentHtml += '</div>';
    }
    
    contentHtml += '</div>';
    
    taskDiv.innerHTML = headerHtml + contentHtml;
    return taskDiv;
}

/**
 * Переключение видимости содержимого задачи
 */
function toggleTaskContent(taskId) {
    const content = document.getElementById(`task-content-${taskId}`);
    if (content) {
        content.classList.toggle('expanded');
    }
}

/**
 * Переключение видимости кода подзадачи
 */
function toggleSubtaskCode(taskId, subtaskId) {
    const code = document.getElementById(`subtask-code-${taskId}-${subtaskId}`);
    if (code) {
        code.classList.toggle('expanded');
    }
}

/**
 * Выполнение одной задачи
 */
async function executeTask(taskId) {
    console.log('[Tasks] Запуск задачи:', taskId);
    
    if (!currentTaskPlan) {
        alert('План задач не загружен');
        return;
    }
    
    try {
        // Обновляем статус задачи на Running
        const task = currentTaskPlan.tasks.find(t => t.id === taskId);
        if (task) {
            task.status = 'Running';
            runningTasks.add(taskId);
            renderTaskPlan();
        }
        
        // Отправляем запрос на выполнение
        const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/execute-task`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                dialogueId: currentDialogueId,
                planId: currentTaskPlan.planId,
                taskId: taskId
            })
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Ошибка выполнения задачи');
        }
        
        console.log('[Tasks] Задача запущена успешно');
        
    } catch (error) {
        console.error('[Tasks] Ошибка выполнения задачи:', error);
        alert('Ошибка выполнения задачи: ' + error.message);
        
        // Обновляем статус на Failed
        const task = currentTaskPlan.tasks.find(t => t.id === taskId);
        if (task) {
            task.status = 'Failed';
            runningTasks.delete(taskId);
            renderTaskPlan();
        }
    }
}

/**
 * Остановка выполнения задачи
 */
async function stopTask(taskId) {
    console.log('[Tasks] Остановка задачи:', taskId);
    
    if (!currentTaskPlan) return;
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/stop-task`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                planId: currentTaskPlan.planId,
                taskId: taskId
            })
        });
        
        if (!response.ok) {
            throw new Error('Ошибка остановки задачи');
        }
        
        // Обновляем статус
        const task = currentTaskPlan.tasks.find(t => t.id === taskId);
        if (task) {
            task.status = 'Stopped';
            runningTasks.delete(taskId);
            renderTaskPlan();
        }
        
    } catch (error) {
        console.error('[Tasks] Ошибка остановки задачи:', error);
    }
}

/**
 * Выполнение всех задач по цепочке
 */
async function executeAllTasks() {
    console.log('[Tasks] Запуск всех задач');
    
    if (!currentTaskPlan || !currentTaskPlan.tasks) {
        alert('План задач не загружен');
        return;
    }
    
    const executeAllBtn = document.getElementById('execute-all-tasks-btn');
    executeAllBtn.disabled = true;
    executeAllBtn.innerHTML = '<div class="task-loader"></div> Выполнение...';
    
    try {
        for (const task of currentTaskPlan.tasks) {
            if (task.status !== 'Completed') {
                await executeTask(task.id);
                // Ждем завершения задачи (в реальности это будет через WebSocket)
                await new Promise(resolve => setTimeout(resolve, 2000));
            }
        }
    } catch (error) {
        console.error('[Tasks] Ошибка выполнения всех задач:', error);
    } finally {
        executeAllBtn.disabled = false;
        executeAllBtn.innerHTML = '<span class="material-icons">play_arrow</span> Запустить все задачи';
    }
}

/**
 * Обработка прогресса выполнения задачи через WebSocket
 */
function handleTaskProgress(payload) {
    console.log('[Tasks] Прогресс задачи:', payload);
    
    const { taskId, progress, planId } = payload;
    
    // Добавляем сообщение в чат
    if (progress) {
        addMessageToChat('assistant', progress);
    }
    
    // Обновляем статус задачи в UI
    if (currentTaskPlan && currentTaskPlan.planId === planId) {
        const task = currentTaskPlan.tasks.find(t => t.id === taskId);
        if (task) {
            // Определяем статус по тексту прогресса
            if (progress.includes('Начало выполнения')) {
                task.status = 'Running';
                runningTasks.add(taskId);
            } else if (progress.includes('✓') || progress.includes('выполнена успешно')) {
                task.status = 'Completed';
                runningTasks.delete(taskId);
            } else if (progress.includes('✗') || progress.includes('Ошибка')) {
                task.status = 'Failed';
                runningTasks.delete(taskId);
            } else if (progress.includes('⏹') || progress.includes('остановлено')) {
                task.status = 'Stopped';
                runningTasks.delete(taskId);
            }
            
            // Перерисовываем план задач
            renderTaskPlan();
        }
    }
}

/**
 * Вспомогательная функция для экранирования HTML
 */
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

/**
 * Удаление плана задач
 */
async function deleteTaskPlan() {
    if (!currentDialogueId) {
        alert('Выберите диалог');
        return;
    }
    
    // Подтверждение удаления
    if (!confirm('Вы уверены, что хотите удалить план задач? Это действие нельзя отменить.')) {
        return;
    }
    
    try {
        console.log('[Tasks] Удаление плана задач для диалога:', currentDialogueId);
        
        const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/task-plan`, {
            method: 'DELETE'
        });
        
        if (!response.ok) {
            const errorText = await response.text();
            console.error('[Tasks] Ошибка удаления:', errorText);
            
            let errorMessage = 'Ошибка удаления плана';
            try {
                const error = JSON.parse(errorText);
                errorMessage = error.message || error.title || errorText;
            } catch {
                errorMessage = errorText || 'Неизвестная ошибка';
            }
            
            throw new Error(errorMessage);
        }
        
        console.log('[Tasks] План задач успешно удален');
        
        // Очищаем текущий план
        currentTaskPlan = null;
        
        // Закрываем панель задач
        closeTaskPanel();
        
        // Обновляем кнопки
        await checkAndShowTasksButton();
        
        // Показываем уведомление
        showStatusMessage('План задач удален', 'success');
        
    } catch (error) {
        console.error('[Tasks] Ошибка удаления плана задач:', error);
        alert('Ошибка удаления плана задач: ' + error.message);
    }
}

// Добавляем обработчик клика на кнопку "Задачи"
document.addEventListener('DOMContentLoaded', () => {
    const tasksButton = document.getElementById('tasks-button');
    if (tasksButton) {
        tasksButton.addEventListener('click', openTaskPanel);
    }
    
    const deletePlanButton = document.getElementById('delete-plan-button');
    if (deletePlanButton) {
        deletePlanButton.addEventListener('click', deleteTaskPlan);
    }
});


// ============================================================================
// DEEPSEEK API TOGGLE
// ============================================================================

/**
 * Загрузка состояния toggle при старте приложения
 */
async function loadDeepSeekToggleState() {
    try {
        const response = await fetch(`${API_BASE}/api/configuration`);
        if (!response.ok) {
            console.error('[DeepSeek] Ошибка загрузки конфигурации');
            return;
        }
        
        const data = await response.json();
        const config = data.configuration;
        
        if (config) {
            const toggle = document.getElementById('deepseek-api-toggle');
            if (toggle) {
                toggle.checked = config.useDeepSeekApi || false;
                console.log('[DeepSeek] Toggle состояние загружено:', toggle.checked);
            }
        }
    } catch (error) {
        console.error('[DeepSeek] Ошибка загрузки состояния toggle:', error);
    }
}

/**
 * Переключение между DeepSeek API и локальной Ollama моделью
 */
async function toggleDeepSeekApi() {
    const toggle = document.getElementById('deepseek-api-toggle');
    const useDeepSeekApi = toggle.checked;
    
    console.log('[DeepSeek] Переключение на:', useDeepSeekApi ? 'DeepSeek API' : 'Ollama локально');
    
    try {
        // Загружаем текущую конфигурацию
        const getResponse = await fetch(`${API_BASE}/api/configuration`);
        if (!getResponse.ok) {
            throw new Error('Не удалось загрузить конфигурацию');
        }
        
        const data = await getResponse.json();
        const config = data.configuration;
        
        // Обновляем настройку UseDeepSeekApi на верхнем уровне
        config.useDeepSeekApi = useDeepSeekApi;
        
        // Убеждаемся что секция DeepSeek существует
        if (!config.deepSeek) {
            config.deepSeek = {
                apiKey: 'sk-173a241826d841e390424dfabf177394',
                baseUrl: 'https://api.deepseek.com',
                chatModel: 'deepseek-chat',
                reasonerModel: 'deepseek-reasoner'
            };
        }
        
        // Сохраняем конфигурацию
        const saveResponse = await fetch(`${API_BASE}/api/configuration`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                provider: config.provider,
                useDeepSeekApi: config.useDeepSeekApi,
                openAI: config.openAI,
                ollama: config.ollama,
                deepSeek: config.deepSeek
            })
        });
        
        if (!saveResponse.ok) {
            throw new Error('Не удалось сохранить конфигурацию');
        }
        
        console.log('[DeepSeek] Конфигурация успешно сохранена');
        
        // Показываем уведомление
        const message = useDeepSeekApi 
            ? 'Включен DeepSeek API для чата и reasoning задач' 
            : 'Используется локальная Ollama модель';
        
        showStatusMessage(message, 'success');
        
    } catch (error) {
        console.error('[DeepSeek] Ошибка переключения:', error);
        alert('Ошибка переключения: ' + error.message);
        
        // Возвращаем toggle в предыдущее состояние
        toggle.checked = !useDeepSeekApi;
    }
}

// Загружаем состояние toggle при загрузке страницы
document.addEventListener('DOMContentLoaded', () => {
    loadDeepSeekToggleState();
});

/**
 * Проверка включен ли DeepSeek API
 */
async function isDeepSeekApiEnabled() {
    try {
        const response = await fetch(`${API_BASE}/api/configuration`);
        if (response.ok) {
            const data = await response.json();
            return data.configuration?.useDeepSeekApi === true;
        }
        return false;
    } catch (error) {
        console.error('[Config] Ошибка проверки DeepSeek API:', error);
        return false;
    }
}

/**
 * Прямое выполнение задач через DeepSeek API
 */
async function executeTasksDirect() {
    console.log('[Tasks] Запуск прямого выполнения задач');
    
    if (!currentDialogueId) {
        alert('Выберите диалог');
        return;
    }
    
    // Проверяем DeepSeek API
    const deepSeekEnabled = await isDeepSeekApiEnabled();
    if (!deepSeekEnabled) {
        alert('DeepSeek API не включен. Включите toggle "Use DeepSeek API" в настройках.');
        return;
    }
    
    // Проверяем заполненность контекста
    const dialogue = dialogues.find(d => d.id === currentDialogueId);
    if (!dialogue || !dialogue.dialogueGroupId) {
        alert('Диалог не принадлежит ни одной группе');
        return;
    }
    
    const group = dialogueGroups.find(g => g.id === dialogue.dialogueGroupId);
    if (!group) {
        alert('Группа диалогов не найдена');
        return;
    }
    
    const missingFields = [];
    if (!group.requirements || !group.requirements.trim()) {
        missingFields.push('Требования (requirements.md)');
    }
    if (!group.design || !group.design.trim()) {
        missingFields.push('Проектирование (design.md)');
    }
    if (!group.tasks || !group.tasks.trim()) {
        missingFields.push('Задачи (tasks.md)');
    }
    
    if (missingFields.length > 0) {
        const fieldsList = missingFields.join('\n• ');
        alert(`Для выполнения задач необходимо заполнить следующие поля:\n\n• ${fieldsList}\n\nОткройте контекст группы и заполните все поля.`);
        return;
    }
    
    // Подтверждение от пользователя
    const confirmed = confirm(
        'Вы уверены, что хотите выполнить задачи?\n\n' +
        'DeepSeek API создаст все файлы и папки в текущем проекте.\n\n' +
        'Это действие может занять несколько минут.'
    );
    
    if (!confirmed) {
        return;
    }
    
    try {
        // Отключаем кнопку
        const executeButton = document.getElementById('execute-tasks-button');
        if (executeButton) {
            executeButton.disabled = true;
        }
        
        console.log('[Tasks] Отправка запроса на выполнение задач');
        
        const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/execute-tasks-direct`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        if (!response.ok) {
            const errorText = await response.text();
            console.error('[Tasks] Ошибка ответа:', errorText);
            
            let errorMessage = 'Ошибка запуска выполнения задач';
            try {
                const error = JSON.parse(errorText);
                errorMessage = error.message || error.title || errorText;
            } catch {
                errorMessage = errorText || 'Неизвестная ошибка';
            }
            
            throw new Error(errorMessage);
        }
        
        const result = await response.json();
        console.log('[Tasks] Выполнение задач запущено:', result);
        
        // Показываем уведомление
        showStatusMessage('Выполнение задач запущено. Следите за прогрессом в чате.', 'success');
        
    } catch (error) {
        console.error('[Tasks] Ошибка запуска выполнения задач:', error);
        alert('Ошибка запуска выполнения задач: ' + error.message);
        
        // Включаем кнопку обратно
        const executeButton = document.getElementById('execute-tasks-button');
        if (executeButton) {
            executeButton.disabled = false;
        }
    }
}

// Привязка обработчиков событий к кнопкам
document.addEventListener('DOMContentLoaded', () => {
    // Кнопка "Задачи" (создание плана)
    const tasksButton = document.getElementById('tasks-button');
    if (tasksButton) {
        tasksButton.addEventListener('click', openTaskPanel);
    }
    
    // Кнопка "Выполнить задачи"
    const executeTasksButton = document.getElementById('execute-tasks-button');
    if (executeTasksButton) {
        executeTasksButton.addEventListener('click', executeTasksDirect);
    }
    
    // Кнопка "Удалить план"
    const deletePlanButton = document.getElementById('delete-plan-button');
    if (deletePlanButton) {
        deletePlanButton.addEventListener('click', async () => {
            if (!currentDialogueId) {
                alert('Выберите диалог');
                return;
            }
            
            const confirmed = confirm('Вы уверены, что хотите удалить план задач?');
            if (!confirmed) return;
            
            try {
                const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/task-plan`, {
                    method: 'DELETE'
                });
                
                if (response.ok) {
                    showStatusMessage('План задач удален', 'success');
                    closeTaskPanel();
                    await checkAndShowTasksButton();
                } else {
                    const error = await response.json();
                    throw new Error(error.message || 'Ошибка удаления плана');
                }
            } catch (error) {
                console.error('[Tasks] Ошибка удаления плана:', error);
                alert('Ошибка удаления плана: ' + error.message);
            }
        });
    }
});

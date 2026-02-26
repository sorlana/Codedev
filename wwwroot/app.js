const API_BASE = '';

let currentDialogueId = null;
let isProcessing = false;
let projects = [];

// Глобальные переменные для управления агентским режимом выполнения
let executionPollingInterval = null;
let currentExecutionStatus = 'none';

// Initialize app
document.addEventListener('DOMContentLoaded', () => {
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
    
    if (projects.length === 0) {
        selector.innerHTML = '<option value="">Нет проектов</option>';
        return;
    }
    
    selector.innerHTML = projects.map(p => `
        <option value="${p.id}" ${p.isSelected ? 'selected' : ''}>
            ${escapeHtml(p.name)}
        </option>
    `).join('');
}

async function selectProject(projectId) {
    if (!projectId) return;
    
    try {
        await fetch(`${API_BASE}/api/projects/${projectId}/select`, {
            method: 'POST'
        });
        
        await loadProjects();
    } catch (error) {
        console.error('Error selecting project:', error);
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
    document.getElementById('prompt-input').addEventListener('keypress', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });
    
    // Настройка кнопок управления выполнением
    setupExecutionControlButtons();
    
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
    // Остановка polling предыдущего диалога
    stopPollingExecutionStatus();
    
    currentDialogueId = dialogueId;
    
    // Update active state
    document.querySelectorAll('.dialogue-item').forEach(item => {
        item.classList.toggle('active', item.dataset.id == dialogueId);
    });
    
    await loadMessages(dialogueId);
    await loadCheckpoints(dialogueId);
    
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

async function loadMessages(dialogueId) {
    try {
        const response = await fetch(`${API_BASE}/api/dialogues/${dialogueId}`);
        
        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }
        
        const dialogue = await response.json();
        
        const messageList = document.getElementById('message-list');
        
        if (!dialogue || !dialogue.messages || dialogue.messages.length === 0) {
            messageList.innerHTML = '<div class="empty-state">Нет сообщений. Начните диалог.</div>';
            return;
        }
        
        messageList.innerHTML = dialogue.messages.map(m => `
            <div class="message ${m.role}">
                <div class="message-role">${m.role === 'user' ? 'Вы' : 'Ассистент'}</div>
                <div class="message-content">${escapeHtml(m.content)}</div>
            </div>
        `).join('');
        
        messageList.scrollTop = messageList.scrollHeight;
    } catch (error) {
        console.error('Error loading messages:', error);
        const messageList = document.getElementById('message-list');
        messageList.innerHTML = '<div class="error">Ошибка загрузки сообщений. Попробуйте обновить страницу.</div>';
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
    
    try {
        const response = await fetch(`${API_BASE}/api/dialogues/${currentDialogueId}/messages`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ content })
        });
        
        if (!response.ok) {
            const error = await response.text();
            throw new Error(error);
        }
        
        // Проверка на команду запуска выполнения задач
        const contentLower = content.toLowerCase();
        if (contentLower.includes('начни выполнение') || 
            contentLower.includes('запусти выполнение') ||
            contentLower.includes('start execution') ||
            contentLower.includes('execute tasks')) {
            // Запуск polling для отслеживания статуса выполнения
            startPollingExecutionStatus();
        }
        
        input.value = '';
        await loadMessages(currentDialogueId);
        await loadCheckpoints(currentDialogueId);
    } catch (error) {
        console.error('Error sending message:', error);
        showError('Ошибка отправки сообщения: ' + error.message);
    } finally {
        isProcessing = false;
        sendButton.disabled = false;
        sendButton.textContent = 'Отправить';
    }
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
    try {
        const response = await fetch(`${API_BASE}/api/startup/validate`);
        const result = await response.json();
        
        if (!result.isConnected && result.errorMessage) {
            showStartupWarning(result.errorMessage);
        }
    } catch (error) {
        console.error('Error validating model connection:', error);
    }
}

function showStartupWarning(message) {
    const warningDiv = document.createElement('div');
    warningDiv.className = 'startup-warning';
    warningDiv.innerHTML = `
        <div class="warning-content">
            <span class="warning-icon">⚠️</span>
            <span class="warning-message">${escapeHtml(message)}</span>
            <button class="warning-close" onclick="this.parentElement.parentElement.remove()">✕</button>
        </div>
    `;
    document.body.insertBefore(warningDiv, document.body.firstChild);
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

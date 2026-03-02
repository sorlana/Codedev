# Исправление отображения структуры папок

## Проблема
Структура папок отображалась как плоский список вместо дерева:
- Не было визуальной иерархии
- Не было понятно, какие папки вложены в другие
- В файлах не указывались полные пути с учетом папок

## Решение

### 1. Frontend - Рекурсивное отображение дерева
**Файл**: `wwwroot/app.js`

Добавлены функции для рекурсивного отображения дерева папок:
```javascript
function renderFolderTree(node, prefix = '') {
    // Отображает корневую папку и рекурсивно все дочерние
}

function renderFolderTreeNode(node, prefix, isLast) {
    // Отображает узел дерева с правильными коннекторами (├──, └──, │)
}
```

Обновлено отображение задачи:
- Вместо `task.folders` используется `task.folderStructure`
- Структура отображается в виде дерева с иконками 📁 и 📄
- В `filePath` теперь показывается полный путь

### 2. CSS стили для дерева
**Файл**: `wwwroot/index.html`

Добавлены стили:
```css
.folder-tree {
    font-family: 'Courier New', monospace;
    font-size: 13px;
    background: #f8f9fa;
    padding: 12px;
}

.folder-tree-item {
    white-space: pre;
    color: #495057;
}
```

### 3. Backend - Обновлен промпт
**Файл**: `Services/TaskPlannerService.cs`

Обновлен промпт для LLM:
- Использование `folderStructure` вместо `folders`
- Пример с рекурсивной структурой:
```json
{
  "folderStructure": {
    "name": "calculator",
    "children": [
      { "name": "src", "children": [
        { "name": "utils", "children": [] }
      ]},
      { "name": "tests", "children": [] }
    ]
  }
}
```
- Добавлено требование указывать полные пути в `filePath`

## Пример результата

Структура отображается так:
```
calculator/
├── 📁 src/
│   ├── 📁 utils/
│   │   └── 📄 helper.js
│   └── 📄 index.js
├── 📁 tests/
│   └── 📄 test.js
└── 📄 package.json
```

Файлы указываются с полными путями:
- `src/utils/helper.js`
- `src/index.js`
- `tests/test.js`

## Статус
✅ Frontend обновлен
✅ CSS стили добавлены
✅ Backend промпт исправлен
✅ Приложение запущено на http://localhost:5111
⏳ Требуется тестирование с новой генерацией плана

# BitcoinFinder Web Server

Веб-сервер для распределенного поиска Bitcoin seed-фраз с поддержкой множественных агентов и автоматическим управлением пулом соединений.

## Особенности

- **Поиск seed-фраз**: Полнофункциональный поиск Bitcoin seed-фраз с использованием BIP39
- **Распределенный поиск**: Поддержка множественных агентов для параллельной обработки задач
- **Автоматический сброс пула**: Сброс соединений каждые 30 минут при бездействии
- **Веб-интерфейс**: Современный веб-интерфейс для мониторинга и управления
- **REST API**: Полнофункциональный API для агентов и поиска
- **База данных**: Сохранение задач и статистики в SQL Server
- **Real-time обновления**: Автоматическое обновление статуса через JavaScript

## Архитектура

### Компоненты

1. **SeedPhraseFinder** - Поиск и генерация Bitcoin seed-фраз
2. **TaskManager** - Управление задачами поиска
3. **AgentManager** - Управление подключенными агентами
4. **PoolManager** - Автоматический сброс пула соединений
5. **WebController** - Веб-интерфейс
6. **ApiController** - REST API для агентов
7. **SearchController** - API для поиска seed-фраз

### База данных

- **SearchTasks** - Задачи поиска
- **AgentInfos** - Информация об агентах

## Установка и запуск

### Требования

- .NET 8.0 или выше
- SQL Server (LocalDB для разработки)
- Visual Studio 2022 или VS Code

### Шаги установки

1. Клонируйте репозиторий
2. Откройте проект в Visual Studio
3. Обновите строку подключения в `appsettings.json`
4. Запустите проект

```bash
dotnet restore
dotnet run
```

### Конфигурация

Основные настройки в `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=BitcoinFinderWebServer;Trusted_Connection=true;MultipleActiveResultSets=true"
  },
  "PoolSettings": {
    "ResetIntervalMinutes": 30,
    "CleanupIntervalMinutes": 5
  }
}
```

## Поиск seed-фраз

### Возможности поиска

- **BIP39 совместимость**: Поддержка стандарта BIP39 для seed-фраз
- **Генерация адресов**: Автоматическая генерация Bitcoin адресов из seed-фраз
- **Валидация**: Проверка корректности seed-фраз
- **Расчет комбинаций**: Подсчет общего количества возможных комбинаций
- **Поиск по шаблону**: Поиск с использованием * для неизвестных слов

### Примеры использования

```csharp
// Создание задачи поиска
var task = new SearchTask
{
    SeedPhrase = "abandon * * * * * * * * * * *",
    BitcoinAddress = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ",
    WordCount = 12,
    StartIndex = 0,
    EndIndex = 1000000
};

// Выполнение поиска
var result = await seedPhraseFinder.FindSeedPhraseAsync(task);
```

### API для поиска

#### Выполнение поиска
```http
POST /api/search/execute/{taskId}
```

#### Валидация seed-фразы
```http
POST /api/search/validate
Content-Type: application/json

{
  "seedPhrase": "abandon ability able about above absent absorb abstract absurd abuse access accident"
}
```

#### Расчет комбинаций
```http
GET /api/search/combinations?seedPhrase=* * * * * * * * * * * *&wordCount=12
```

## API для агентов

### Регистрация агента

```http
POST /api/api/register
Content-Type: application/json

{
  "agentId": "agent-001",
  "ipAddress": "192.168.1.100"
}
```

### Запрос задачи

```http
POST /api/api/task/request
Content-Type: application/json

{
  "agentId": "agent-001"
}
```

### Обновление прогресса

```http
POST /api/api/task/progress
Content-Type: application/json

{
  "agentId": "agent-001",
  "taskId": 1,
  "currentProgress": 50000,
  "speed": 1000.5
}
```

### Завершение задачи

```http
POST /api/api/task/complete
Content-Type: application/json

{
  "agentId": "agent-001",
  "taskId": 1,
  "success": true,
  "result": "abandon ability able about above absent absorb abstract absurd abuse access accident",
  "totalProcessed": 100000
}
```

### Heartbeat

```http
POST /api/api/heartbeat
Content-Type: application/json

{
  "agentId": "agent-001",
  "status": "Working",
  "currentTaskId": 1,
  "speed": 1000.5
}
```

## Веб-интерфейс

### Страницы

1. **Dashboard** - Общая статистика и мониторинг
2. **Tasks** - Управление задачами поиска
3. **Agents** - Мониторинг подключенных агентов
4. **Create Task** - Создание новых задач

### Функции

- Создание задач поиска
- Мониторинг прогресса в реальном времени
- Просмотр статистики агентов
- Управление пулом соединений

## Управление пулом

### Автоматический сброс

- Пул автоматически сбрасывается каждые 30 минут при отсутствии активности
- Все активные задачи возвращаются в статус "Pending"
- Агенты отключаются и должны переподключиться

### Ручной сброс

- Кнопка "Reset Timer" на дашборде
- Обновляет таймер сброса пула

## Разработка

### Структура проекта

```
BitcoinFinderWebServer/
├── Controllers/
│   ├── ApiController.cs      # API для агентов
│   ├── WebController.cs      # Веб-интерфейс
│   └── SearchController.cs   # API для поиска
├── Data/
│   └── ApplicationDbContext.cs
├── Models/
│   ├── SearchTask.cs
│   ├── AgentInfo.cs
│   └── ApiModels.cs
├── Services/
│   ├── SeedPhraseFinder.cs   # Поиск seed-фраз
│   ├── TaskManager.cs
│   ├── AgentManager.cs
│   └── PoolManager.cs
└── Views/
    ├── Shared/
    │   └── _Layout.cshtml
    └── Web/
        ├── Index.cshtml
        ├── Tasks.cshtml
        └── CreateTask.cshtml
```

### Добавление новых функций

1. Создайте модель в папке `Models/`
2. Добавьте сервис в папку `Services/`
3. Создайте контроллер в папке `Controllers/`
4. Добавьте представления в папку `Views/`

## Лицензия

Этот проект является частью BitcoinFinder и использует ту же лицензию.

## Поддержка

Для вопросов и предложений создавайте issues в репозитории проекта. 
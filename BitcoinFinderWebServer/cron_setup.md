# Настройка Cron для BitcoinFinder на Shared Hosting

## 🕐 Настройка Cron задач

### 1. Основной ping каждые 15 минут
```bash
# Добавьте в crontab (crontab -e):
*/15 * * * * curl -s "https://your-domain.com/api/cron/ping" > /dev/null 2>&1
```

### 2. Проверка здоровья каждые 30 минут
```bash
# Добавьте в crontab:
*/30 * * * * curl -s "https://your-domain.com/api/keep-alive/health" > /dev/null 2>&1
```

### 3. Сброс пула каждые 2 часа
```bash
# Добавьте в crontab:
0 */2 * * * curl -s -X POST "https://your-domain.com/api/cron/reset-pool" > /dev/null 2>&1
```

## 📋 Доступные API endpoints

### Основные endpoints:
- `GET /api/cron/ping` - Основной ping для поддержания активности
- `GET /api/cron/tasks-status` - Статус всех задач
- `POST /api/cron/start-task/{taskId}` - Запуск задачи
- `POST /api/cron/stop-task/{taskId}` - Остановка задачи
- `POST /api/cron/create-task` - Создание новой задачи
- `POST /api/cron/reset-pool` - Принудительный сброс пула

### Keep-alive endpoints:
- `GET /api/keep-alive` - Поддержание активности
- `GET /api/keep-alive/health` - Проверка здоровья

## 🔧 Примеры использования

### 1. Создание задачи через cron
```bash
curl -X POST "https://your-domain.com/api/cron/create-task" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test Task",
    "targetAddress": "1ABC...",
    "wordCount": 12,
    "startIndex": 0,
    "endIndex": 1000000,
    "blockSize": 10000,
    "threads": 1,
    "autoStart": true
  }'
```

### 2. Запуск существующей задачи
```bash
curl -X POST "https://your-domain.com/api/cron/start-task/YOUR_TASK_ID"
```

### 3. Проверка статуса
```bash
curl "https://your-domain.com/api/cron/tasks-status"
```

## ⚙️ Настройка для разных хостингов

### cPanel
1. Войдите в cPanel
2. Найдите "Cron Jobs"
3. Добавьте команды выше

### Plesk
1. Войдите в Plesk
2. Перейдите в "Scheduled Tasks"
3. Добавьте команды выше

### DirectAdmin
1. Войдите в DirectAdmin
2. Найдите "Cron Jobs"
3. Добавьте команды выше

### SSH доступ
```bash
# Редактирование crontab
crontab -e

# Просмотр текущих задач
crontab -l

# Удаление всех задач
crontab -r
```

## 📊 Мониторинг

### Логи cron
```bash
# Просмотр логов cron
tail -f /var/log/cron

# Или в cPanel:
# Logs -> Cron Job Logs
```

### Проверка работы
```bash
# Тестовый запрос
curl -v "https://your-domain.com/api/cron/ping"

# Ожидаемый ответ:
{
  "timestamp": "2025-01-XX...",
  "status": "success",
  "restoredTasks": 0,
  "cleanedAgents": 0,
  "memoryUsage": 45,
  "uptime": "00:15:30",
  "poolStatus": "Running: True, LastActivity: 14:30:15"
}
```

## 🚨 Важные моменты

### Ограничения shared hosting:
- **CPU время**: 30-60 секунд на запрос
- **Память**: 128-512 МБ
- **Потоки**: 1 поток максимум
- **Размер блоков**: 10000 комбинаций максимум

### Рекомендации:
1. **Интервал**: Не чаще чем каждые 15 минут
2. **Размер задач**: Маленькие блоки (10000)
3. **Потоки**: Только 1 поток
4. **Мониторинг**: Регулярно проверяйте логи

### Troubleshooting:
```bash
# Проверка доступности
curl -I "https://your-domain.com/api/cron/ping"

# Проверка с таймаутом
curl --max-time 10 "https://your-domain.com/api/cron/ping"

# Сохранение ответа в файл
curl "https://your-domain.com/api/cron/ping" > /tmp/cron_response.log 2>&1
```

## 📈 Оптимизация для shared hosting

### Настройки в appsettings.json:
```json
{
  "SharedHosting": {
    "Enabled": true,
    "MaxMemoryMB": 128,
    "MaxCpuTimeSeconds": 30,
    "MaxBackgroundTasks": 1,
    "AutoResetIntervalMinutes": 15,
    "KeepAliveIntervalSeconds": 30
  }
}
```

### Размеры блоков:
- **Малые задачи**: 1000-5000 комбинаций
- **Средние задачи**: 5000-10000 комбинаций
- **Большие задачи**: Не рекомендуется

### Частота cron:
- **Минимум**: 15 минут
- **Рекомендуется**: 30 минут
- **Максимум**: 60 минут 
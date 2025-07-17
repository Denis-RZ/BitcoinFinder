# Устранение проблем с BitcoinFinder Web Server

## Проблема: Не могу скомпилировать проект

### Решение 1: Остановка всех процессов

1. **Запустите скрипт остановки процессов:**
   ```powershell
   .\stop_processes.ps1
   ```

2. **Или выполните команды вручную:**
   ```powershell
   # Остановить все процессы dotnet
   Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Stop-Process -Force
   
   # Остановить все процессы BitcoinFinder
   Get-Process -Name "*BitcoinFinder*" -ErrorAction SilentlyContinue | Stop-Process -Force
   ```

### Решение 2: Очистка проекта

```powershell
# Очистить проект
dotnet clean

# Удалить папки bin и obj
Remove-Item -Path "bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "obj" -Recurse -Force -ErrorAction SilentlyContinue

# Восстановить пакеты
dotnet restore
```

### Решение 3: Пересборка проекта

```powershell
# Собрать проект
dotnet build

# Если есть ошибки, попробуйте:
dotnet build --verbosity detailed
```

## Проблема: Сервер не запускается

### Решение 1: Проверка портов

```powershell
# Проверить, какие порты заняты
netstat -an | findstr :5000
netstat -an | findstr :5002
```

### Решение 2: Запуск через batch файл

```cmd
# Запустить через batch файл
start_server.bat
```

### Решение 3: Запуск в режиме отладки

```powershell
# Запустить с подробным выводом
dotnet run --verbosity detailed
```

## Проблема: Ошибки компиляции

### Частые ошибки и решения:

1. **CS8618 - nullable reference types**
   - Это предупреждения, не ошибки
   - Проект скомпилируется успешно

2. **CS1998 - async methods without await**
   - Это предупреждения о синхронных async методах
   - Не влияют на компиляцию

3. **CS0414 - unused fields**
   - Предупреждения о неиспользуемых полях
   - Можно игнорировать

## Проверка работоспособности

### 1. Тест API
```powershell
# Запустить тестовый скрипт
.\test_api.ps1
```

### 2. Проверка веб-интерфейса
```
http://localhost:5002/Home/SeedSearch
```

### 3. Проверка API endpoints
```
http://localhost:5002/api/task/calculate-combinations?wordCount=12
http://localhost:5002/api/task/live-log
```

## Логи и отладка

### Просмотр логов
```powershell
# Запустить с выводом логов
dotnet run --environment Development
```

### Файлы логов
- Логи приложения: `logs/` (если настроено)
- Логи .NET: в консоли при запуске

## Быстрый старт

1. **Остановить все процессы:**
   ```powershell
   .\stop_processes.ps1
   ```

2. **Очистить проект:**
   ```powershell
   dotnet clean
   ```

3. **Собрать проект:**
   ```powershell
   dotnet build
   ```

4. **Запустить сервер:**
   ```powershell
   dotnet run
   ```

5. **Открыть в браузере:**
   ```
   http://localhost:5002/Home/SeedSearch
   ```

## Контакты для поддержки

Если проблемы не решаются:
1. Проверьте версию .NET: `dotnet --version`
2. Убедитесь, что установлен .NET 9.0 или выше
3. Проверьте права доступа к папке проекта 
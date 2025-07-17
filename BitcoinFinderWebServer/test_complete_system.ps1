# Комплексное тестирование системы управления задачами
$baseUrl = "http://localhost:5002"

Write-Host "=== Комплексное тестирование системы управления задачами ===" -ForegroundColor Green

# 1. Проверка доступности сервера
Write-Host "`n1. Проверка доступности сервера..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/list" -Method Get
    Write-Host "✅ Сервер доступен" -ForegroundColor Green
} catch {
    Write-Host "❌ Сервер недоступен: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# 2. Запуск сервера поиска
Write-Host "`n2. Запуск сервера поиска..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/start" -Method Post
    if ($response.success) {
        Write-Host "✅ Сервер поиска запущен" -ForegroundColor Green
    } else {
        Write-Host "❌ Ошибка запуска сервера: $($response.message)" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ Ошибка запуска сервера: $($_.Exception.Message)" -ForegroundColor Red
}

# 3. Установка количества потоков
Write-Host "`n3. Установка количества потоков..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/set-threads?threads=4" -Method Post
    if ($response.success) {
        Write-Host "✅ Количество потоков установлено: $($response.threads)" -ForegroundColor Green
    } else {
        Write-Host "❌ Ошибка установки потоков: $($response.message)" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ Ошибка установки потоков: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Создание тестовой задачи
Write-Host "`n4. Создание тестовой задачи..." -ForegroundColor Yellow
try {
    $taskData = @{
        Name = "Тестовая задача"
        TargetAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"
        KnownWords = ""
        WordCount = 12
        Language = "english"
        StartIndex = 0
        EndIndex = 0
        BatchSize = 10000
        BlockSize = 100000
        Threads = 4
    }
    
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/create" -Method Post -Body ($taskData | ConvertTo-Json) -ContentType "application/json"
    if ($response.success) {
        $taskId = $response.taskId
        Write-Host "✅ Задача создана: $taskId" -ForegroundColor Green
        Write-Host "   Всего комбинаций: $($response.totalCombinations)" -ForegroundColor Cyan
        Write-Host "   Количество блоков: $($response.blockCount)" -ForegroundColor Cyan
    } else {
        Write-Host "❌ Ошибка создания задачи: $($response.message)" -ForegroundColor Red
        $taskId = $null
    }
} catch {
    Write-Host "❌ Ошибка создания задачи: $($_.Exception.Message)" -ForegroundColor Red
    $taskId = $null
}

# 5. Получение списка задач
Write-Host "`n5. Получение списка задач..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/list" -Method Get
    Write-Host "✅ Найдено задач: $($response.Count)" -ForegroundColor Green
    if ($response.Count -gt 0) {
        $response | ForEach-Object {
            Write-Host "   - $($_.Name) (ID: $($_.Id), Статус: $($_.Status))" -ForegroundColor Cyan
        }
    }
} catch {
    Write-Host "❌ Ошибка получения списка задач: $($_.Exception.Message)" -ForegroundColor Red
}

# 6. Получение деталей задачи (если создана)
if ($taskId) {
    Write-Host "`n6. Получение деталей задачи..." -ForegroundColor Yellow
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/task/$taskId" -Method Get
        Write-Host "✅ Детали задачи получены" -ForegroundColor Green
        Write-Host "   Название: $($response.Name)" -ForegroundColor Cyan
        Write-Host "   Статус: $($response.Status)" -ForegroundColor Cyan
        Write-Host "   Обработано: $($response.ProcessedCombinations) / $($response.TotalCombinations)" -ForegroundColor Cyan
        Write-Host "   Прогресс: $([math]::Round($response.Progress * 100, 2))%" -ForegroundColor Cyan
    } catch {
        Write-Host "❌ Ошибка получения деталей задачи: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 7. Получение живого лога
Write-Host "`n7. Получение живого лога..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/live-log" -Method Get
    if ($response.success) {
        Write-Host "✅ Живой лог получен" -ForegroundColor Green
        Write-Host "   Фраз в логе: $($response.phrases.Count)" -ForegroundColor Cyan
        if ($response.phrases.Count -gt 0) {
            Write-Host "   Последние 3 фразы:" -ForegroundColor Cyan
            $response.phrases | Select-Object -Last 3 | ForEach-Object {
                Write-Host "     - $($_.phrase) (Статус: $($_.status))" -ForegroundColor White
            }
        }
    } else {
        Write-Host "❌ Ошибка получения живого лога: $($response.message)" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ Ошибка получения живого лога: $($_.Exception.Message)" -ForegroundColor Red
}

# 8. Тестирование управления задачей (если создана)
if ($taskId) {
    Write-Host "`n8. Тестирование управления задачей..." -ForegroundColor Yellow
    
    # Запуск задачи
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/task/task/$taskId/start" -Method Post
        if ($response.success) {
            Write-Host "✅ Задача запущена" -ForegroundColor Green
        } else {
            Write-Host "❌ Ошибка запуска задачи: $($response.message)" -ForegroundColor Red
        }
    } catch {
        Write-Host "❌ Ошибка запуска задачи: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # Пауза задачи
    Start-Sleep -Seconds 2
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/task/task/$taskId/pause" -Method Post
        if ($response.success) {
            Write-Host "✅ Задача приостановлена" -ForegroundColor Green
        } else {
            Write-Host "❌ Ошибка приостановки задачи: $($response.message)" -ForegroundColor Red
        }
    } catch {
        Write-Host "❌ Ошибка приостановки задачи: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n=== Тестирование завершено ===" -ForegroundColor Green
Write-Host "Откройте браузер и перейдите на: $baseUrl/Home/TaskManager" -ForegroundColor Cyan 
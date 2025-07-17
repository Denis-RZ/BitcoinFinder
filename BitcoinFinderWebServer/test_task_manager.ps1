# Тестирование API управления задачами
$baseUrl = "http://localhost:5002"

Write-Host "=== Тестирование API управления задачами ===" -ForegroundColor Green

# 1. Получение списка задач
Write-Host "`n1. Получение списка задач..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/list" -Method Get
    Write-Host "Успешно! Найдено задач: $($response.Count)" -ForegroundColor Green
    if ($response.Count -gt 0) {
        $response | ForEach-Object {
            Write-Host "  - $($_.Name) (ID: $($_.Id), Статус: $($_.Status))" -ForegroundColor Cyan
        }
    }
} catch {
    Write-Host "Ошибка: $($_.Exception.Message)" -ForegroundColor Red
}

# 2. Запуск сервера
Write-Host "`n2. Запуск сервера..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/start" -Method Post
    Write-Host "Успешно! $($response.message)" -ForegroundColor Green
} catch {
    Write-Host "Ошибка: $($_.Exception.Message)" -ForegroundColor Red
}

# 3. Установка количества потоков
Write-Host "`n3. Установка количества потоков..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/set-threads?threads=4" -Method Post
    Write-Host "Успешно! $($response.message)" -ForegroundColor Green
} catch {
    Write-Host "Ошибка: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Получение живого лога
Write-Host "`n4. Получение живого лога..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/task/live-log" -Method Get
    Write-Host "Успешно! Фраз в логе: $($response.phrases.Count)" -ForegroundColor Green
    if ($response.phrases.Count -gt 0) {
        Write-Host "Последние 3 фразы:" -ForegroundColor Cyan
        $response.phrases | Select-Object -Last 3 | ForEach-Object {
            Write-Host "  - $($_.phrase) (Статус: $($_.status))" -ForegroundColor White
        }
    }
} catch {
    Write-Host "Ошибка: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== Тестирование завершено ===" -ForegroundColor Green 
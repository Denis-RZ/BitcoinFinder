# Тест создания и сохранения задачи
$baseUrl = "http://localhost:5002"

Write-Host "=== Test создания и сохранения задачи ===" -ForegroundColor Green

# 1. Проверяем, что сервер работает
Write-Host "1. Проверка доступности сервера..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/task/status" -Method GET -TimeoutSec 10
    Write-Host "   Сервер доступен: $($response.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "   Ошибка подключения к серверу: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# 2. Создаем новую задачу
Write-Host "2. Создание новой задачи..." -ForegroundColor Yellow
$taskData = @{
    Name = "Test Task $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    TargetAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"
    WordCount = 12
    Language = "english"
    StartIndex = 0
    EndIndex = 0
    BatchSize = 1000
    BlockSize = 10000
    Threads = 2
}

try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/task/create" -Method POST -Body ($taskData | ConvertTo-Json) -ContentType "application/json" -TimeoutSec 30
    $result = $response.Content | ConvertFrom-Json
    
    if ($result.Success) {
        Write-Host "   Задача создана успешно!" -ForegroundColor Green
        Write-Host "   TaskId: $($result.TaskId)" -ForegroundColor Cyan
        Write-Host "   TotalCombinations: $($result.TotalCombinations)" -ForegroundColor Cyan
        Write-Host "   BlockCount: $($result.BlockCount)" -ForegroundColor Cyan
        
        $taskId = $result.TaskId
    } else {
        Write-Host "   Ошибка создания задачи: $($result.Message)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   Ошибка при создании задачи: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# 3. Проверяем список задач
Write-Host "3. Проверка списка задач..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/task/list" -Method GET -TimeoutSec 10
    $tasks = $response.Content | ConvertFrom-Json
    
    Write-Host "   Найдено задач: $($tasks.Count)" -ForegroundColor Green
    foreach ($task in $tasks) {
        Write-Host "   - $($task.Name) (ID: $($task.Id), Status: $($task.Status))" -ForegroundColor Cyan
    }
} catch {
    Write-Host "   Ошибка при получении списка задач: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Проверяем конкретную задачу
Write-Host "4. Проверка конкретной задачи..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/api/task/$taskId" -Method GET -TimeoutSec 10
    $task = $response.Content | ConvertFrom-Json
    
    Write-Host "   Задача найдена: $($task.Name)" -ForegroundColor Green
    Write-Host "   Статус: $($task.Status)" -ForegroundColor Cyan
    Write-Host "   Блоков: $($task.Blocks.Count)" -ForegroundColor Cyan
    Write-Host "   Обработано: $($task.ProcessedCombinations)/$($task.TotalCombinations)" -ForegroundColor Cyan
} catch {
    Write-Host "   Ошибка при получении задачи: $($_.Exception.Message)" -ForegroundColor Red
}

# 5. Проверяем файл конфигурации
Write-Host "5. Проверка файла конфигурации..." -ForegroundColor Yellow
$configFile = "tasks_config.json"
if (Test-Path $configFile) {
    $fileInfo = Get-Item $configFile
    Write-Host "   Файл конфигурации существует: $configFile" -ForegroundColor Green
    Write-Host "   Размер: $($fileInfo.Length) байт" -ForegroundColor Cyan
    Write-Host "   Последнее изменение: $($fileInfo.LastWriteTime)" -ForegroundColor Cyan
    
    # Показываем первые несколько строк файла
    $content = Get-Content $configFile -TotalCount 10
    Write-Host "   Первые строки файла:" -ForegroundColor Yellow
    $content | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
} else {
    Write-Host "   Файл конфигурации НЕ найден: $configFile" -ForegroundColor Red
}

Write-Host "=== Test completed ===" -ForegroundColor Green 
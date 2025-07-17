# Тест API BitcoinFinder Web Server

Write-Host "Testing BitcoinFinder Web Server API..." -ForegroundColor Green

# Тест 1: Проверка доступности сервера
Write-Host "`n1. Testing server availability..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5002/api/task/calculate-combinations?wordCount=12" -UseBasicParsing
    Write-Host "Server is running! Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response: $($response.Content)" -ForegroundColor Cyan
} catch {
    Write-Host "Server is not responding: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Тест 2: Создание задачи
Write-Host "`n2. Creating test task..." -ForegroundColor Yellow
$taskBody = @{
    Name = "TestTask"
    TargetAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"
    WordCount = 4
    Threads = 1
    BatchSize = 1000
    BlockSize = 1000
} | ConvertTo-Json

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5002/api/task/create" -Method POST -Body $taskBody -ContentType "application/json" -UseBasicParsing
    Write-Host "Task created successfully! Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response: $($response.Content)" -ForegroundColor Cyan
    
    # Извлекаем TaskId из ответа
    $result = $response.Content | ConvertFrom-Json
    $taskId = $result.TaskId
    Write-Host "Task ID: $taskId" -ForegroundColor Magenta
} catch {
    Write-Host "Failed to create task: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Error response: $responseBody" -ForegroundColor Red
    }
}

# Тест 3: Получение живого лога
Write-Host "`n3. Testing live log..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5002/api/task/live-log" -UseBasicParsing
    Write-Host "Live log retrieved! Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response: $($response.Content)" -ForegroundColor Cyan
} catch {
    Write-Host "Failed to get live log: $($_.Exception.Message)" -ForegroundColor Red
}

# Тест 4: Запуск сервера поиска
Write-Host "`n4. Starting server search..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5002/api/task/start" -Method POST -UseBasicParsing
    Write-Host "Server search started! Status: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Response: $($response.Content)" -ForegroundColor Cyan
} catch {
    Write-Host "Failed to start server search: $($_.Exception.Message)" -ForegroundColor Red
}

# Test API live-log
Write-Host "Testing live-log API..." -ForegroundColor Green

Start-Sleep -Seconds 5

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5002/api/task/live-log" -Method GET
    Write-Host "✅ API Response:" -ForegroundColor Green
    Write-Host "   Success: $($response.Success)" -ForegroundColor Green
    Write-Host "   Phrases Count: $($response.Phrases.Count)" -ForegroundColor Green
    Write-Host "   Total Phrases: $($response.TotalPhrases)" -ForegroundColor Green
    Write-Host "   Processed Combinations: $($response.ProcessedCombinations)" -ForegroundColor Green
    
    if ($response.Phrases.Count -gt 0) {
        Write-Host "   First phrase: $($response.Phrases[0].Phrase)" -ForegroundColor Green
        Write-Host "   First status: $($response.Phrases[0].Status)" -ForegroundColor Green
        Write-Host "   First index: $($response.Phrases[0].Index)" -ForegroundColor Green
    }
    
    # Check if Combination field exists
    if ($response.Phrases.Count -gt 0) {
        $hasCombination = $response.Phrases[0].PSObject.Properties.Name -contains "Combination"
        Write-Host "   Has Combination field: $hasCombination" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`nAPI testing completed!" -ForegroundColor Green 
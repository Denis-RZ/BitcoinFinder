# Testing live log operations
Write-Host "=== Live Log Testing ===" -ForegroundColor Green

# Check server availability
Write-Host "1. Checking server availability..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5002/api/task/list" -Method GET
    Write-Host "✅ Server is available" -ForegroundColor Green
} catch {
    Write-Host "❌ Server is not available: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Create test task
Write-Host "`n2. Creating test task..." -ForegroundColor Yellow
$taskData = @{
    Name = "LiveLogTest"
    TargetAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"  # Test address
    KnownWords = ""
    WordCount = 12
    Language = "english"
    StartIndex = 0
    EndIndex = 0
    BatchSize = 10000
    BlockSize = 1000
    Threads = 2
}

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5002/api/task/create" -Method POST -Body ($taskData | ConvertTo-Json) -ContentType "application/json"
    Write-Host "✅ Task created: $($response.taskId)" -ForegroundColor Green
    $taskId = $response.taskId
} catch {
    Write-Host "❌ Error creating task: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Start server search
Write-Host "`n3. Starting server search..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5002/api/task/start" -Method POST
    Write-Host "✅ Server search started" -ForegroundColor Green
} catch {
    Write-Host "❌ Error starting search: $($_.Exception.Message)" -ForegroundColor Red
}

# Wait and check live log
Write-Host "`n4. Checking live log..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

for ($i = 1; $i -le 5; $i++) {
    Write-Host "`nLive log check #$i..." -ForegroundColor Cyan
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:5002/api/task/live-log" -Method GET
        Write-Host "✅ Records received: $($response.Phrases.Count)" -ForegroundColor Green
        Write-Host "   Total processed: $($response.ProcessedCombinations)" -ForegroundColor Green
        
        if ($response.Phrases.Count -gt 0) {
            $latest = $response.Phrases[0]
            Write-Host "   Latest phrase: $($latest.Phrase)" -ForegroundColor Green
            Write-Host "   Status: $($latest.Status)" -ForegroundColor Green
            Write-Host "   Time: $($latest.Timestamp)" -ForegroundColor Green
        }
    } catch {
        Write-Host "❌ Error getting live log: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    Start-Sleep -Seconds 2
}

# Check task status
Write-Host "`n5. Checking task status..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "http://localhost:5002/api/task/$taskId" -Method GET
    Write-Host "✅ Task status: $($response.Status)" -ForegroundColor Green
    Write-Host "   Processed combinations: $($response.ProcessedCombinations)" -ForegroundColor Green
} catch {
    Write-Host "❌ Error getting task status: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== Testing completed ===" -ForegroundColor Green
Write-Host "Open http://localhost:5002 in browser to view live log" -ForegroundColor Cyan 
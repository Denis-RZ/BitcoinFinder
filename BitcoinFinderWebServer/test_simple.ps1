# Simple test for live-log API
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
    }
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
} 
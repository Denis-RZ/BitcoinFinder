# Скрипт для поддержания активности BitcoinFinder на shared hosting
# Запускать каждые 15 минут через планировщик задач

param(
    [string]$ServerUrl = "https://your-domain.com",
    [int]$IntervalSeconds = 30
)

Write-Host "BitcoinFinder Shared Hosting Monitor" -ForegroundColor Green
Write-Host "Server: $ServerUrl" -ForegroundColor Yellow
Write-Host "Interval: $IntervalSeconds seconds" -ForegroundColor Yellow
Write-Host "Started: $(Get-Date)" -ForegroundColor Cyan

function Send-KeepAlive {
    param([string]$Url)
    
    try {
        $response = Invoke-RestMethod -Uri "$Url/api/keep-alive" -Method GET -TimeoutSec 10
        Write-Host "$(Get-Date -Format 'HH:mm:ss'): Keep-alive OK - Memory: $($response.memoryUsage)MB" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "$(Get-Date -Format 'HH:mm:ss'): Keep-alive FAILED - $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Test-Health {
    param([string]$Url)
    
    try {
        $response = Invoke-RestMethod -Uri "$Url/api/keep-alive/health" -Method GET -TimeoutSec 10
        Write-Host "$(Get-Date -Format 'HH:mm:ss'): Health OK - Status: $($response.status)" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "$(Get-Date -Format 'HH:mm:ss'): Health FAILED - $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# Основной цикл мониторинга
$consecutiveFailures = 0
$maxFailures = 3

while ($true) {
    $keepAliveSuccess = Send-KeepAlive -Url $ServerUrl
    $healthSuccess = Test-Health -Url $ServerUrl
    
    if ($keepAliveSuccess -and $healthSuccess) {
        $consecutiveFailures = 0
        Write-Host "$(Get-Date -Format 'HH:mm:ss'): All checks passed" -ForegroundColor Green
    } else {
        $consecutiveFailures++
        Write-Host "$(Get-Date -Format 'HH:mm:ss'): Check failed ($consecutiveFailures/$maxFailures)" -ForegroundColor Yellow
        
        if ($consecutiveFailures -ge $maxFailures) {
            Write-Host "$(Get-Date -Format 'HH:mm:ss'): Too many failures, stopping monitor" -ForegroundColor Red
            break
        }
    }
    
    Write-Host "$(Get-Date -Format 'HH:mm:ss'): Waiting $IntervalSeconds seconds..." -ForegroundColor Gray
    Start-Sleep -Seconds $IntervalSeconds
}

Write-Host "Monitor stopped at $(Get-Date)" -ForegroundColor Cyan 
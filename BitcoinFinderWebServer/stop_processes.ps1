# Скрипт для остановки всех процессов BitcoinFinder и dotnet

Write-Host "Stopping all BitcoinFinder and dotnet processes..." -ForegroundColor Yellow

# Останавливаем процессы dotnet
$dotnetProcesses = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
if ($dotnetProcesses) {
    Write-Host "Found $($dotnetProcesses.Count) dotnet processes. Stopping..." -ForegroundColor Red
    $dotnetProcesses | Stop-Process -Force
    Write-Host "dotnet processes stopped." -ForegroundColor Green
} else {
    Write-Host "No dotnet processes found." -ForegroundColor Green
}

# Останавливаем процессы BitcoinFinder
$bitcoinProcesses = Get-Process -Name "*BitcoinFinder*" -ErrorAction SilentlyContinue
if ($bitcoinProcesses) {
    Write-Host "Found $($bitcoinProcesses.Count) BitcoinFinder processes. Stopping..." -ForegroundColor Red
    $bitcoinProcesses | Stop-Process -Force
    Write-Host "BitcoinFinder processes stopped." -ForegroundColor Green
} else {
    Write-Host "No BitcoinFinder processes found." -ForegroundColor Green
}

# Проверяем порты
Write-Host "`nChecking ports 5000 and 5002..." -ForegroundColor Yellow
$port5000 = netstat -an | findstr :5000
$port5002 = netstat -an | findstr :5002

if ($port5000) {
    Write-Host "Port 5000 is in use:" -ForegroundColor Red
    Write-Host $port5000 -ForegroundColor Red
} else {
    Write-Host "Port 5000 is free." -ForegroundColor Green
}

if ($port5002) {
    Write-Host "Port 5002 is in use:" -ForegroundColor Red
    Write-Host $port5002 -ForegroundColor Red
} else {
    Write-Host "Port 5002 is free." -ForegroundColor Green
}

Write-Host "`nAll processes stopped successfully!" -ForegroundColor Green
Write-Host "You can now run: dotnet run" -ForegroundColor Cyan 
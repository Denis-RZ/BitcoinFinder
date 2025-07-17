# Скрипт для установки APK на Android устройство
param(
    [string]$DeviceId = "",
    [string]$ApkPath = ""
)

Write-Host "BitcoinFinderAndroidNew - Android Installer" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green

# Проверяем наличие ADB
try {
    $adbVersion = adb version
    Write-Host "ADB найден:" -ForegroundColor Yellow
    Write-Host $adbVersion[0] -ForegroundColor Gray
} catch {
    Write-Host "ОШИБКА: ADB не найден!" -ForegroundColor Red
    Write-Host "Установите Android SDK или добавьте adb в PATH" -ForegroundColor Red
    Write-Host "Скачать: https://developer.android.com/studio/releases/platform-tools" -ForegroundColor Cyan
    exit 1
}

# Получаем список устройств
Write-Host "`nПоиск подключенных устройств..." -ForegroundColor Yellow
$devices = adb devices

if ($devices.Count -le 1) {
    Write-Host "ОШИБКА: Устройства не найдены!" -ForegroundColor Red
    Write-Host "Подключите Android устройство по USB и включите отладку" -ForegroundColor Red
    Write-Host "Настройки -> О телефоне -> Нажмите 7 раз на 'Номер сборки'" -ForegroundColor Cyan
    Write-Host "Настройки -> Для разработчиков -> Отладка по USB" -ForegroundColor Cyan
    exit 1
}

# Показываем доступные устройства
Write-Host "`nДоступные устройства:" -ForegroundColor Yellow
$deviceList = @()
for ($i = 1; $i -lt $devices.Count; $i++) {
    if ($devices[$i] -match "(\S+)\s+device") {
        $deviceId = $matches[1]
        $deviceList += $deviceId
        Write-Host "[$($deviceList.Count)] $deviceId" -ForegroundColor White
    }
}

# Выбираем устройство
if ($DeviceId -eq "") {
    if ($deviceList.Count -eq 1) {
        $DeviceId = $deviceList[0]
        Write-Host "`nАвтоматически выбрано устройство: $DeviceId" -ForegroundColor Green
    } else {
        Write-Host "`nВыберите устройство (1-$($deviceList.Count)):" -ForegroundColor Yellow
        $choice = Read-Host
        if ($choice -match "^\d+$" -and [int]$choice -le $deviceList.Count) {
            $DeviceId = $deviceList[[int]$choice - 1]
        } else {
            Write-Host "Неверный выбор!" -ForegroundColor Red
            exit 1
        }
    }
}

# Ищем APK файл
if ($ApkPath -eq "") {
    $possiblePaths = @(
        "bin\Release\net8.0-android\android-arm64\publish\BitcoinFinderAndroidNew-Signed.apk",
        "bin\Release\net8.0-android\android-arm64\publish\BitcoinFinderAndroidNew.apk",
        "bin\Release\net8.0-android\android-x64\publish\BitcoinFinderAndroidNew-Signed.apk",
        "bin\Release\net8.0-android\android-x64\publish\BitcoinFinderAndroidNew.apk"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $ApkPath = $path
            break
        }
    }
}

if ($ApkPath -eq "" -or -not (Test-Path $ApkPath)) {
    Write-Host "ОШИБКА: APK файл не найден!" -ForegroundColor Red
    Write-Host "Сначала соберите проект: .\build-installer.bat" -ForegroundColor Cyan
    exit 1
}

Write-Host "`nНайден APK: $ApkPath" -ForegroundColor Green

# Устанавливаем APK
Write-Host "`nУстановка на устройство $DeviceId..." -ForegroundColor Yellow
Write-Host "adb -s $DeviceId install -r `"$ApkPath`"" -ForegroundColor Gray

$result = adb -s $DeviceId install -r $ApkPath

if ($result -match "Success") {
    Write-Host "`n✅ УСТАНОВКА УСПЕШНА!" -ForegroundColor Green
    Write-Host "Приложение установлено на устройство" -ForegroundColor White
    Write-Host "Найдите 'BitcoinFinderAndroidNew' в списке приложений" -ForegroundColor Cyan
} else {
    Write-Host "`n❌ ОШИБКА УСТАНОВКИ!" -ForegroundColor Red
    Write-Host $result -ForegroundColor Red
    Write-Host "`nВозможные решения:" -ForegroundColor Yellow
    Write-Host "1. Включите 'Установка из неизвестных источников'" -ForegroundColor Cyan
    Write-Host "2. Разрешите установку через ADB в настройках разработчика" -ForegroundColor Cyan
    Write-Host "3. Проверьте, что устройство разблокировано" -ForegroundColor Cyan
}

Write-Host "`nНажмите любую клавишу для выхода..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown") 
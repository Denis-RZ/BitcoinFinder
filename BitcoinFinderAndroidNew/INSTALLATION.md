# BitcoinFinderAndroidNew - Инструкция по установке

## Варианты установки

### 1. Автоматическая установка (рекомендуется)

**Требования:**
- Android устройство подключено по USB
- Включена отладка по USB
- Установлен ADB (Android Debug Bridge)

**Шаги:**
1. Подключите Android устройство к компьютеру
2. Включите отладку по USB:
   - Настройки → О телефоне → Нажмите 7 раз на "Номер сборки"
   - Настройки → Для разработчиков → Отладка по USB
3. Запустите скрипт установки:
   ```powershell
   powershell -ExecutionPolicy Bypass -File install-to-device.ps1
   ```

### 2. Ручная установка APK

**Шаги:**
1. Соберите проект:
   ```bash
   .\build-installer.bat
   ```
2. Скопируйте APK файл на Android устройство:
   - `bin\Release\net8.0-android\android-arm64\publish\BitcoinFinderAndroidNew-Signed.apk`
3. На Android устройстве:
   - Настройки → Безопасность → Установка из неизвестных источников
   - Откройте APK файл и установите

### 3. Установка через Google Play Store

**Для разработчиков:**
1. Соберите AAB файл:
   ```bash
   .\build-installer.bat
   ```
2. Загрузите файл `BitcoinFinderAndroidNew.aab` в Google Play Console

## Сборка установщика

### Быстрая сборка
```bash
.\build-installer.bat
```

### Ручная сборка
```bash
# APK для ARM64
dotnet publish -c Release -f net8.0-android -r android-arm64 --self-contained false -p:AndroidPackageFormat=apk

# APK для x64
dotnet publish -c Release -f net8.0-android -r android-x64 --self-contained false -p:AndroidPackageFormat=apk

# AAB для Google Play
dotnet publish -c Release -f net8.0-android -r android-arm64 --self-contained false -p:AndroidPackageFormat=aab
```

## Файлы установщика

После сборки в папке `bin\Release\net8.0-android\` появятся:

### APK файлы (прямая установка)
- `android-arm64\publish\BitcoinFinderAndroidNew-Signed.apk` - для ARM64 устройств
- `android-x64\publish\BitcoinFinderAndroidNew-Signed.apk` - для x64 устройств

### AAB файл (Google Play Store)
- `android-arm64\publish\BitcoinFinderAndroidNew.aab` - для загрузки в Google Play

## Устранение проблем

### Ошибка "Установка заблокирована"
1. Настройки → Безопасность → Установка из неизвестных источников
2. Разрешите установку для нужного приложения

### Ошибка "Приложение не установлено"
1. Проверьте архитектуру устройства (ARM64/x64)
2. Убедитесь, что APK подписан
3. Попробуйте пересобрать проект

### ADB не найден
1. Скачайте Android SDK Platform Tools: https://developer.android.com/studio/releases/platform-tools
2. Добавьте папку в PATH
3. Или используйте ручную установку APK

## Системные требования

- Android 5.0 (API 21) или выше
- ARM64 или x64 архитектура
- Минимум 50MB свободного места
- Разрешения: Интернет, Хранилище

## Безопасность

⚠️ **Важно:** Приложение работает с криптографическими ключами. Устанавливайте только из доверенных источников. 
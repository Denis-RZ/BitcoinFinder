# 📱 Инструкции по сборке APK

## 🚀 Быстрая сборка

### Вариант 1: Через скрипт (рекомендуется)
1. Дважды кликните на файл `build-apk.bat`
2. Дождитесь завершения сборки
3. APK файл будет в папке `bin\Release\net8.0-android\`

### Вариант 2: Через командную строку
```bash
# Очистка
dotnet clean

# Восстановление пакетов
dotnet restore

# Сборка
dotnet build -c Release

# Создание APK
dotnet publish -c Release -f net8.0-android
```

## 📁 Готовые файлы

После сборки в папке `bin\Release\net8.0-android\` будут файлы:

- ✅ **BitcoinFinderAndroidNew.BitcoinFinderAndroidNew-Signed.apk** - подписанный APK (используйте этот!)
- ⚠️ BitcoinFinderAndroidNew.BitcoinFinderAndroidNew.aab - App Bundle
- ⚠️ BitcoinFinderAndroidNew.BitcoinFinderAndroidNew-Signed.aab - подписанный App Bundle

## 📱 Установка на Android

1. **Скопируйте** `BitcoinFinderAndroidNew.BitcoinFinderAndroidNew-Signed.apk` на Android устройство
2. **Включите** "Установка из неизвестных источников" в настройках
3. **Откройте** APK файл и установите приложение

## 🔧 Решение проблем

### Ошибка Visual Studio при публикации
- **Проблема**: Visual Studio не может опубликовать проект
- **Решение**: Используйте командную строку или скрипт `build-apk.bat`

### Приложение крашится при запуске
- **Проблема**: Недостаточно разрешений или ошибки в коде
- **Решение**: 
  - Проверьте логи в приложении
  - Убедитесь, что разрешена установка из неизвестных источников
  - Попробуйте очистить кэш приложения

### Ошибки компиляции
- **Проблема**: Ошибки XAML или C#
- **Решение**: 
  - Убедитесь, что установлен .NET 8 SDK
  - Проверьте, что все файлы сохранены
  - Попробуйте `dotnet clean` и пересоберите

## 📋 Требования

- ✅ .NET 8 SDK
- ✅ Android SDK (автоматически устанавливается с .NET)
- ✅ Windows 10/11

## 🎯 Результат

После успешной сборки у вас будет рабочий APK файл с современным интерфейсом для поиска Bitcoin ключей! 
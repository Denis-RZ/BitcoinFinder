@echo off
echo ========================================
echo Bitcoin Key Finder - APK Builder
echo ========================================

echo.
echo Очистка предыдущей сборки...
dotnet clean

echo.
echo Восстановление пакетов...
dotnet restore

echo.
echo Сборка проекта...
dotnet build -c Release

echo.
echo Создание APK...
dotnet publish -c Release -f net8.0-android

echo.
echo ========================================
echo Сборка завершена!
echo ========================================
echo.
echo APK файлы находятся в:
echo bin\Release\net8.0-android\
echo.
echo Используйте файл: BitcoinFinderAndroidNew.BitcoinFinderAndroidNew-Signed.apk
echo.
pause 
@echo off
echo Очистка проекта...
dotnet clean BitcoinFinderAndroidNew
echo.
echo Восстановление зависимостей...
dotnet restore BitcoinFinderAndroidNew
echo.
echo Сборка проекта...
dotnet build BitcoinFinderAndroidNew -c Release -f net8.0-android -p:AndroidPackageFormat=apk
echo.
echo Готово!
pause 
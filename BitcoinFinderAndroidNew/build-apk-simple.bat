@echo off
echo Building BitcoinFinderAndroidNew APK (Simple)...

REM Clean previous builds
dotnet clean -c Release

REM Restore packages
dotnet restore

REM Build APK for ARM64
echo Building for ARM64...
dotnet publish -c Release -f net8.0-android -r android-arm64 --self-contained false

REM Build APK for X64
echo Building for X64...
dotnet publish -c Release -f net8.0-android -r android-x64 --self-contained false

echo.
echo APK files should be in:
echo bin\Release\net8.0-android\android-arm64\publish\
echo bin\Release\net8.0-android\android-x64\publish\
echo.
echo Check for .apk files in these directories
pause 
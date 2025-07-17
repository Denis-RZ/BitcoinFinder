@echo off
echo Building BitcoinFinderAndroidNew Installer...

REM Clean previous builds
dotnet clean -c Release

REM Restore packages
dotnet restore

echo.
echo Building APK for direct installation...
dotnet publish -c Release -f net8.0-android -r android-arm64 --self-contained false -p:AndroidPackageFormat=apk

echo.
echo Building AAB for Google Play Store...
dotnet publish -c Release -f net8.0-android -r android-arm64 --self-contained false -p:AndroidPackageFormat=aab

echo.
echo Building APK for x64 devices...
dotnet publish -c Release -f net8.0-android -r android-x64 --self-contained false -p:AndroidPackageFormat=apk

echo.
echo ========================================
echo INSTALLATION FILES CREATED:
echo ========================================
echo APK files (direct install):
echo - bin\Release\net8.0-android\android-arm64\publish\BitcoinFinderAndroidNew-Signed.apk
echo - bin\Release\net8.0-android\android-x64\publish\BitcoinFinderAndroidNew-Signed.apk
echo.
echo AAB file (Google Play Store):
echo - bin\Release\net8.0-android\android-arm64\publish\BitcoinFinderAndroidNew.aab
echo.
echo ========================================
echo INSTALLATION INSTRUCTIONS:
echo ========================================
echo 1. Copy APK file to your Android device
echo 2. Enable "Install from unknown sources" in Android settings
echo 3. Open APK file and install
echo.
echo For Google Play Store: upload the .aab file
echo ========================================
pause 
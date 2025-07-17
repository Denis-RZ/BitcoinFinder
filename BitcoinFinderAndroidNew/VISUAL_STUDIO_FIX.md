# 🔧 Исправление ошибок Visual Studio

## 🚨 Текущие ошибки:
1. **NETSDK1013** - TargetFramework не распознается
2. **Publish target** - не поддерживается без указания фреймворка

## ✅ Решение:

### 1. **Закройте Visual Studio полностью**

### 2. **Удалите папки кэша:**
```
Удалите папки:
- BitcoinFinderAndroidNew\obj\
- BitcoinFinderAndroidNew\bin\
- %USERPROFILE%\.nuget\packages\ (опционально)
```

### 3. **Проверьте .NET SDK:**
```cmd
dotnet --version
dotnet workload list
```

### 4. **Установите MAUI workload:**
```cmd
dotnet workload install maui-android
```

### 5. **Откройте проект заново в Visual Studio**

### 6. **Или используйте bat файл:**
```cmd
cd BitcoinFinderAndroidNew
rebuild.bat
```

## 🔍 Альтернативное решение:

Если ошибки остаются, попробуйте:

1. **Создать новый MAUI проект**
2. **Скопировать код из старого проекта**
3. **Использовать новый csproj**

## 📱 Результат:
После исправления должен создаться APK файл в:
```
bin\Release\net8.0-android\android-arm64\
``` 
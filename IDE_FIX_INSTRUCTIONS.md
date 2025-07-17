# Инструкции по исправлению ошибок в IDE

## Проблема
IDE (Visual Studio, VS Code, Rider) показывает ошибки, хотя проект собирается успешно в командной строке.

## Причина
IDE кэширует информацию о проекте и не обновляет ее после изменений в конфигурации.

## Решение

### Для Visual Studio:
1. **Закройте Visual Studio**
2. **Удалите папки кэша**:
   ```
   %LOCALAPPDATA%\Microsoft\VisualStudio\[version]\ComponentModelCache
   %LOCALAPPDATA%\Microsoft\VisualStudio\[version]\Extensions
   ```
3. **Откройте проект заново**
4. **Выполните Clean Solution** (Build → Clean Solution)
5. **Выполните Rebuild Solution** (Build → Rebuild Solution)

### Для VS Code:
1. **Закройте VS Code**
2. **Удалите папку .vs** в корне проекта (если есть)
3. **Откройте проект заново**
4. **Перезапустите OmniSharp**:
   - Ctrl+Shift+P → "OmniSharp: Restart OmniSharp"
5. **Очистите кэш C#**:
   - Ctrl+Shift+P → "Developer: Reload Window"

### Для JetBrains Rider:
1. **Закройте Rider**
2. **Удалите папку .idea** в корне проекта
3. **Откройте проект заново**
4. **Выполните Clean** (Build → Clean)
5. **Выполните Rebuild** (Build → Rebuild)

### Альтернативное решение (для всех IDE):
1. **Удалите папки obj и bin** во всех проектах:
   ```powershell
   Get-ChildItem -Recurse -Directory -Name "obj", "bin" | Remove-Item -Recurse -Force
   ```
2. **Перезапустите IDE**
3. **Выполните сборку заново**

## Проверка работоспособности

После выполнения инструкций проверьте:

1. **Сборка в командной строке**:
   ```bash
   dotnet build
   ```

2. **Сборка Android проекта**:
   ```bash
   dotnet build BitcoinFinderAndroid -f:net9.0-android
   ```

3. **Сборка Windows версии**:
   ```bash
   dotnet build BitcoinFinderAndroid -f:net9.0-windows10.0.19041.0
   ```

## Переменные среды

Убедитесь, что переменные среды установлены:
```powershell
$env:ANDROID_HOME = "C:\Users\mikedell\AppData\Local\Android\Sdk"
$env:ANDROID_SDK_ROOT = "C:\Users\mikedell\AppData\Local\Android\Sdk"
```

## Результат

После выполнения всех шагов:
- ✅ Ошибки в IDE должны исчезнуть
- ✅ Проект должен собираться без ошибок
- ✅ IntelliSense должен работать корректно
- ✅ Все платформы должны поддерживаться

## Если проблемы остаются

1. **Проверьте версию .NET SDK**: `dotnet --version`
2. **Проверьте установленные workload**: `dotnet workload list`
3. **Переустановите workload**: `dotnet workload repair`
4. **Обратитесь к отчету**: `ANDROID_SETUP_REPORT.md` 
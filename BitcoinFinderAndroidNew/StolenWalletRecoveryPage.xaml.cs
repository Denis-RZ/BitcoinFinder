#nullable enable
using BitcoinFinderAndroidNew.Services;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace BitcoinFinderAndroidNew;

public partial class StolenWalletRecoveryPage : ContentPage
{
    private TargetedKeyFinder? keyFinder;
    private CancellationTokenSource cancellationTokenSource = new();
    private bool isRecovering = false;
    private DateTime recoveryStartTime;
    private IDispatcherTimer? updateTimer;
    private long totalProcessed = 0;
    private string foundPrivateKey = "";

    public StolenWalletRecoveryPage()
    {
        try
        {
            InitializeComponent();
            SetupEventHandlers();
            SetupTimer();
            AddLog("🔍 Страница восстановления похищенного кошелька загружена");
            AddLog("⚠️ Внимание: Поиск приватного ключа может занять много времени");
            AddLog("📅 Оптимизация под 2017 год активирована");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации: {ex}");
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Ошибка", $"Ошибка инициализации: {ex.Message}", "OK");
            });
        }
    }

    private void SetupEventHandlers()
    {
        // Анимация для кнопок
        StartRecoveryButton.Pressed += async (s, e) => await AnimateButton(StartRecoveryButton);
        StopRecoveryButton.Pressed += async (s, e) => await AnimateButton(StopRecoveryButton);
    }

    private void SetupTimer()
    {
        updateTimer = Application.Current?.Dispatcher.CreateTimer();
        if (updateTimer != null)
        {
            updateTimer.Interval = TimeSpan.FromMilliseconds(100);
            updateTimer.Tick += UpdateElapsedTime;
        }
    }

    private async Task AnimateButton(Button button)
    {
        await button.ScaleTo(0.95, 100);
        await button.ScaleTo(1.0, 100);
    }

    private async void OnStartRecoveryClicked(object sender, EventArgs e)
    {
        if (isRecovering)
        {
            await DisplayAlert("⚠️ Внимание", "Восстановление уже выполняется", "OK");
            return;
        }

        var targetAddress = TargetAddressEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(targetAddress))
        {
            await DisplayAlert("❌ Ошибка", "Введите Bitcoin адрес для восстановления", "OK");
            return;
        }

        if (!IsValidBitcoinAddress(targetAddress))
        {
            await DisplayAlert("❌ Ошибка", "Неверный формат Bitcoin адреса", "OK");
            return;
        }

        try
        {
            // Показываем предупреждение
            var result = await DisplayAlert("⚠️ Подтверждение", 
                $"Начинаем восстановление кошелька:\n\n" +
                $"📍 Адрес: {targetAddress}\n" +
                $"📅 Год похищения: 2017\n" +
                $"🧠 Стратегии: Все доступные\n\n" +
                $"Это может занять много времени. Продолжить?", 
                "🚀 Да, начать", "❌ Отмена");

            if (!result) return;

            // Инициализируем поиск
            keyFinder = new TargetedKeyFinder(targetAddress, new DateTime(2017, 1, 1));
            keyFinder.LogMessage += OnLogMessage;
            keyFinder.ProgressReported += OnProgressReported;
            keyFinder.KeyFound += OnKeyFound;

            // Запускаем восстановление
            isRecovering = true;
            recoveryStartTime = DateTime.Now;
            totalProcessed = 0;
            cancellationTokenSource = new CancellationTokenSource();

            StartRecoveryButton.IsEnabled = false;
            StopRecoveryButton.IsEnabled = true;
            StatusLabel.Text = "🚀 Статус: Восстановление запущено";

            AddLog($"🎯 Начинаем восстановление адреса: {targetAddress}");
            AddLog("📚 Проверяем популярные пароли 2017 года...");

            // Запускаем поиск в фоновом потоке
            _ = Task.Run(async () =>
            {
                var foundResult = await keyFinder.FindPrivateKeyAsync(cancellationTokenSource.Token);
                OnRecoveryCompleted(foundResult != null);
            });

            // Запускаем таймер обновления времени
            updateTimer?.Start();
        }
        catch (Exception ex)
        {
            await DisplayAlert("❌ Ошибка", $"Ошибка запуска восстановления: {ex.Message}", "OK");
            isRecovering = false;
        }
    }

    private void OnStopRecoveryClicked(object sender, EventArgs e)
    {
        if (!isRecovering) return;

        cancellationTokenSource?.Cancel();
        isRecovering = false;
        
        StartRecoveryButton.IsEnabled = true;
        StopRecoveryButton.IsEnabled = false;
        StatusLabel.Text = "⏹️ Статус: Восстановление остановлено";
        
        updateTimer?.Stop();
        AddLog("⏹️ Восстановление остановлено пользователем");
    }

    private void OnLogMessage(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AddLog(message);
        });
    }

    private void OnProgressReported(long index, string currentKey, string currentAddress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            totalProcessed++;
            
            CurrentKeyLabel.Text = $"🔑 Текущий ключ: {currentKey.Substring(0, Math.Min(16, currentKey.Length))}...";
            
            if (totalProcessed % 100 == 0)
            {
                var elapsed = DateTime.Now - recoveryStartTime;
                var speed = totalProcessed / elapsed.TotalSeconds;
                SpeedLabel.Text = $"⚡ Скорость: {speed:F0} к/с";
                ProcessedKeysLabel.Text = $"📈 Обработано: {totalProcessed:N0}";
            }
        });
    }

    private void OnKeyFound(FoundResult result)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            foundPrivateKey = result.PrivateKey;
            
            // Показываем результат
            ResultDetails.IsVisible = true;
            FoundKeyLabel.Text = $"🔑 Приватный ключ: {result.PrivateKey}";
            AddressLabel.Text = $"📍 Адрес: {result.BitcoinAddress}";
            StrategyUsedLabel.Text = $"🧠 Стратегия: {GetStrategyName(result.FoundAtIndex)}";
            ProcessingTimeLabel.Text = $"⏱️ Время поиска: {result.FoundAt - recoveryStartTime:hh\\:mm\\:ss}";
            
            // Обновляем статус
            StatusLabel.Text = "🎉 Статус: ПРИВАТНЫЙ КЛЮЧ НАЙДЕН!";
            StatusLabel.TextColor = Color.FromArgb("#2E7D32");
            
            // Показываем уведомление
            await DisplayAlert("🎉 УСПЕХ!", 
                $"Приватный ключ найден!\n\n" +
                $"🔑 Ключ: {result.PrivateKey}\n" +
                $"📍 Адрес: {result.BitcoinAddress}\n" +
                $"🧠 Стратегия: {GetStrategyName(result.FoundAtIndex)}\n\n" +
                $"Теперь вы можете восстановить доступ к кошельку!", 
                "✅ Понятно");
            
            AddLog("🎉 ПРИВАТНЫЙ КЛЮЧ УСПЕШНО НАЙДЕН!");
            AddLog($"🔑 Ключ: {result.PrivateKey}");
            AddLog($"🧠 Стратегия: {GetStrategyName(result.FoundAtIndex)}");
        });
    }

    private void OnRecoveryCompleted(bool success)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            isRecovering = false;
            StartRecoveryButton.IsEnabled = true;
            StopRecoveryButton.IsEnabled = false;
            updateTimer?.Stop();

            if (!success)
            {
                StatusLabel.Text = "❌ Статус: Ключ не найден";
                AddLog("❌ Приватный ключ не найден в проверенных диапазонах");
                AddLog("💡 Попробуйте другие стратегии или расширьте поиск");
            }
        });
    }

    private void UpdateElapsedTime(object? sender, EventArgs e)
    {
        if (isRecovering)
        {
            var elapsed = DateTime.Now - recoveryStartTime;
            ElapsedTimeLabel.Text = $"⏱️ Время: {elapsed:hh\\:mm\\:ss}";
        }
    }

    private async void OnCopyKeyClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(foundPrivateKey))
        {
            await DisplayAlert("❌ Ошибка", "Нет приватного ключа для копирования", "OK");
            return;
        }

        try
        {
            await Clipboard.SetTextAsync(foundPrivateKey);
            await DisplayAlert("✅ Успех", "Приватный ключ скопирован в буфер обмена", "OK");
            AddLog("📋 Приватный ключ скопирован в буфер обмена");
        }
        catch (Exception ex)
        {
            await DisplayAlert("❌ Ошибка", $"Ошибка копирования: {ex.Message}", "OK");
        }
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logEntry = $"[{timestamp}] {message}";
        
        if (LogLabel.Text.Length > 5000)
        {
            LogLabel.Text = LogLabel.Text.Substring(LogLabel.Text.Length - 4000);
        }
        
        LogLabel.Text += logEntry + Environment.NewLine;
    }

    private bool IsValidBitcoinAddress(string address)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        // Простая проверка формата Bitcoin адреса
        if (address.Length < 26 || address.Length > 35)
            return false;

        if (!address.StartsWith("1") && !address.StartsWith("3") && !address.StartsWith("bc1"))
            return false;

        // Проверяем на допустимые символы
        var validChars = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        return address.All(c => validChars.Contains(c));
    }

    private string GetStrategyName(long foundAtIndex)
    {
        return foundAtIndex switch
        {
            -1 => "Известный адрес",
            -2 => "Словарный поиск",
            -3 => "Brain wallet",
            -4 => "Известный адрес",
            _ => foundAtIndex < 1000 ? "Простые числа" : "Случайный поиск"
        };
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        
        // Останавливаем восстановление при закрытии страницы
        if (isRecovering)
        {
            cancellationTokenSource?.Cancel();
            updateTimer?.Stop();
        }
    }
} 
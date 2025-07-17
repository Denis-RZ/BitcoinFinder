#nullable enable
using BitcoinFinderAndroidNew.Services;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace BitcoinFinderAndroidNew;

public partial class MainPage : ContentPage
{
    private BackgroundSearchService backgroundSearchService = null!;
    private ProgressManager progressManager = null!;
    private CancellationTokenSource cancellationTokenSource = new();
    private bool isSearching = false;
    private List<FoundResult> foundResults = new();
    private DateTime searchStartTime;
    private IDispatcherTimer? updateTimer;

    public MainPage()
    {
        try
        {
            InitializeComponent();
            InitializeServices();
            SetupEventHandlers();
            SetDefaultValues();
            LoadSavedProgress();
            SetupAnimations();
        }
        catch (Exception ex)
        {
            // Логируем ошибку и показываем пользователю
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации: {ex}");
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("Ошибка", $"Ошибка инициализации приложения: {ex.Message}", "OK");
            });
        }
    }

    private void InitializeServices()
    {
        try
        {
            progressManager = new ProgressManager();
            backgroundSearchService = new BackgroundSearchService(progressManager);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации сервисов: {ex}");
            throw;
        }
    }

    private void SetupEventHandlers()
    {
        backgroundSearchService.ProgressReported += OnProgressReported;
        backgroundSearchService.LogMessage += OnLogMessage;
        backgroundSearchService.Found += OnFound;
        backgroundSearchService.ProgressSaved += OnProgressSaved;
    }

    private void SetupAnimations()
    {
        // Анимация для кнопок при нажатии
        StartButton.Pressed += async (s, e) => await AnimateButton(StartButton);
        StopButton.Pressed += async (s, e) => await AnimateButton(StopButton);
        
        // Анимация для прогресс-бара
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

    private void SetDefaultValues()
    {
        FormatPicker.SelectedIndex = 0; // Decimal
        NetworkPicker.SelectedIndex = 0; // Mainnet
    }

    private void LoadSavedProgress()
    {
        try
        {
            var progress = progressManager.LoadProgress();
            if (progress != null)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var result = await DisplayAlert("🔄 Восстановление", 
                        $"Найден сохраненный прогресс:\n" +
                        $"📋 Задача: {progress.TaskName}\n" +
                        $"📍 Позиция: {progress.CurrentIndex:N0}\n" +
                        $"⏱️ Время: {progress.ElapsedTime:hh\\:mm\\:ss}\n\n" +
                        $"Восстановить поиск?", "✅ Да", "❌ Нет");
                    
                    if (result)
                    {
                        RestoreProgress(progress);
                    }
                });
            }

            var results = progressManager.LoadFoundResults();
            if (results.Any())
            {
                foundResults = results;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UpdateResultsDisplay();
                    AddLog($"✅ Загружено {results.Count} найденных результатов");
                });
            }
        }
        catch (Exception ex)
        {
            AddLog($"❌ Ошибка загрузки прогресса: {ex.Message}");
        }
    }

    private void RestoreProgress(SearchProgress progress)
    {
        // Восстанавливаем параметры из сохраненного прогресса
        TargetAddressEntry.Text = progress.TargetAddress;
        StartIndexEntry.Text = progress.CurrentIndex.ToString();
        EndIndexEntry.Text = progress.EndIndex.ToString();
        FormatPicker.SelectedIndex = (int)progress.Format;
        NetworkPicker.SelectedIndex = progress.Network == NetworkType.Mainnet ? 0 : 1;
        
        AddLog($"🔄 Восстановлен прогресс: {progress.TaskName}");
    }



    private async void OnStartClicked(object sender, EventArgs e)
    {
        if (isSearching)
        {
            DisplayAlert("⚠️ Внимание", "Поиск уже выполняется", "OK");
            return;
        }

        try
        {
            var parameters = GetSearchParameters();
            
            if (parameters.TargetAddress == "")
            {
                return; // Ошибка уже показана в GetSearchParameters
            }
            
            // Показываем оценку времени
            ShowTimeEstimate(parameters);
            
            isSearching = true;
            searchStartTime = DateTime.Now;
            cancellationTokenSource = new CancellationTokenSource();
            
            // Анимация запуска
            AnimateStartSearch();
            
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            
            ClearResults();
            
            // Запускаем поиск
            _ = Task.Run(async () =>
            {
                var success = await backgroundSearchService.StartSearchAsync(parameters, "Поиск семейных биткоинов", cancellationTokenSource.Token);
                OnSearchCompleted(success);
            });
        }
        catch (Exception ex)
        {
            DisplayAlert("❌ Ошибка", $"Ошибка запуска поиска: {ex.Message}", "OK");
            isSearching = false;
        }
    }

    private async void ShowTimeEstimate(PrivateKeyParameters parameters)
    {
        var totalKeys = parameters.EndIndex - parameters.StartIndex;
        var estimatedSpeed = parameters.ThreadCount * 1000; // Примерная скорость: 1000 ключей/сек на поток
        var estimatedSeconds = totalKeys / estimatedSpeed;
        
        var timeSpan = TimeSpan.FromSeconds(estimatedSeconds);
        string timeEstimate;
        
        if (timeSpan.TotalDays > 1)
        {
            timeEstimate = $"~{timeSpan.TotalDays:F1} дней";
        }
        else if (timeSpan.TotalHours > 1)
        {
            timeEstimate = $"~{timeSpan.TotalHours:F1} часов";
        }
        else if (timeSpan.TotalMinutes > 1)
        {
            timeEstimate = $"~{timeSpan.TotalMinutes:F1} минут";
        }
        else
        {
            timeEstimate = $"~{timeSpan.TotalSeconds:F0} секунд";
        }
        
        var result = await DisplayAlert("⏱️ Оценка времени", 
            $"Диапазон: {parameters.StartIndex:N0} - {parameters.EndIndex:N0}\n" +
            $"Всего ключей: {totalKeys:N0}\n" +
            $"Потоков: {parameters.ThreadCount}\n" +
            $"Оценка времени: {timeEstimate}\n\n" +
            $"Продолжить поиск?", "✅ Да", "❌ Нет");
        
        if (!result)
        {
            isSearching = false;
            return;
        }
        
        AddLog($"🚀 Начинаем поиск: {parameters.TargetAddress}");
        AddLog($"📊 Диапазон: {parameters.StartIndex:N0} - {parameters.EndIndex:N0}");
        AddLog($"⏱️ Оценка времени: {timeEstimate}");
    }

    private async Task AnimateStartSearch()
    {
        // Анимация запуска поиска
        await StartButton.ScaleTo(0.9, 200);
        await StartButton.ScaleTo(1.0, 200);
        
        // Пульсация статуса
        await StatusLabel.FadeTo(0.5, 500);
        await StatusLabel.FadeTo(1.0, 500);
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        if (!isSearching)
            return;

        backgroundSearchService.StopSearch();
        cancellationTokenSource?.Cancel();
        updateTimer?.Stop();
        AddLog("⏹️ Остановка поиска...");
    }

    private void UpdateElapsedTime(object? sender, EventArgs e)
    {
        if (isSearching)
        {
            var elapsed = DateTime.Now - searchStartTime;
            ElapsedTimeLabel.Text = $"⏱️ Время: {elapsed:hh\\:mm\\:ss}";
        }
    }

    private PrivateKeyParameters GetSearchParameters()
    {
        try
        {
            var parameters = new PrivateKeyParameters();

            // Проверяем целевой адрес
            if (string.IsNullOrWhiteSpace(TargetAddressEntry.Text))
            {
                DisplayAlert("❌ Ошибка", "Укажите целевой Bitcoin адрес", "OK");
                return new PrivateKeyParameters();
            }

            parameters.TargetAddress = TargetAddressEntry.Text.Trim();

            // Проверяем диапазон индексов
            if (!long.TryParse(StartIndexEntry.Text, out var startIndex) || 
                !long.TryParse(EndIndexEntry.Text, out var endIndex))
            {
                DisplayAlert("❌ Ошибка", "Укажите корректные начальный и конечный индексы", "OK");
                return new PrivateKeyParameters();
            }

            if (startIndex >= endIndex)
            {
                DisplayAlert("❌ Ошибка", "Начальный индекс должен быть меньше конечного", "OK");
                return new PrivateKeyParameters();
            }

            parameters.StartIndex = startIndex;
            parameters.EndIndex = endIndex;

            // Format
            parameters.Format = FormatPicker.SelectedIndex switch
            {
                0 => KeyFormat.Decimal,
                1 => KeyFormat.Hex,
                _ => KeyFormat.Decimal
            };

            // Network
            parameters.Network = NetworkPicker.SelectedIndex == 0 ? NetworkType.Mainnet : NetworkType.Testnet;

            // Thread count
            if (!int.TryParse(ThreadCountEntry.Text, out var threadCount) || threadCount <= 0)
            {
                DisplayAlert("❌ Ошибка", "Укажите корректное количество потоков", "OK");
                return new PrivateKeyParameters();
            }
            parameters.ThreadCount = threadCount;

            return parameters;
        }
        catch (Exception ex)
        {
            DisplayAlert("❌ Ошибка", ex.Message, "OK");
            return new PrivateKeyParameters();
        }
    }

    private void OnProgressReported(ProgressInfo progress)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Анимация прогресс-бара
            await ProgressBar.ProgressTo(progress.Progress / 100.0, 250, Easing.Linear);
            
            CurrentKeyLabel.Text = $"🔑 Ключ: {progress.CurrentKey}";
            CurrentAddressLabel.Text = $"📍 Адрес: {progress.CurrentAddress}";
            ProcessedKeysLabel.Text = $"📈 Обработано: {progress.ProcessedKeys:N0}";
            SpeedLabel.Text = $"⚡ Скорость: {progress.Speed:F0} к/с";
            ProgressLabel.Text = $"📊 Прогресс: {progress.Progress:F1}%";
            ElapsedTimeLabel.Text = $"⏱️ Время: {progress.ElapsedTime:hh\\:mm\\:ss}";
            StatusLabel.Text = $"⏳ Статус: {progress.Status}";
            
            // Анимация обновления статуса
            if (progress.Status.Contains("Поиск"))
            {
                await StatusLabel.FadeTo(0.7, 500);
                await StatusLabel.FadeTo(1.0, 500);
            }
        });
    }

    private void OnLogMessage(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AddLog(message);
        });
    }

    private void OnFound(FoundResult result)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            foundResults.Add(result);
            UpdateResultsDisplay();
            
            // Анимация найденного результата
            await AnimateFoundResult();
            
            AddLog($"🎉 НАЙДЕНО! Приватный ключ: {result.PrivateKey}");
            AddLog($"📍 Адрес: {result.BitcoinAddress}");
            AddLog($"💰 Баланс: {result.Balance} BTC");
            
            // Вибрация устройства (если доступна)
            try
            {
                HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
            }
            catch { /* Игнорируем ошибки вибрации */ }
        });
    }

    private async Task AnimateFoundResult()
    {
        // Анимация результата
        await ResultDetails.ScaleTo(1.05, 200);
        await ResultDetails.ScaleTo(1.0, 200);
        
        // Мигание фона
        await ResultDetails.FadeTo(0.8, 100);
        await ResultDetails.FadeTo(1.0, 100);
        await ResultDetails.FadeTo(0.8, 100);
        await ResultDetails.FadeTo(1.0, 100);
    }

    private void OnProgressSaved(SearchProgress progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AddLog($"💾 Прогресс сохранен: {progress.CurrentIndex:N0}");
        });
    }

    private void OnSearchCompleted(bool success)
    {
        isSearching = false;
        updateTimer?.Stop();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;

        if (success)
        {
            AddLog("✅ Поиск завершен успешно!");
            StatusLabel.Text = "⏳ Статус: Завершен";
        }
        else
        {
            ResultLabel.Text = "⏹️ Поиск остановлен";
            StatusLabel.Text = "⏳ Статус: Остановлен";
            AddLog("⏹️ Поиск остановлен.");
        }
    }

    private void UpdateResultsDisplay()
    {
        if (foundResults.Any())
        {
            var latestResult = foundResults.Last();
            ResultLabel.Text = $"🎯 Приватный ключ найден!";
            ResultDetails.IsVisible = true;
            
            FoundKeyLabel.Text = $"🔑 Приватный ключ: {latestResult.PrivateKey}";
            AddressLabel.Text = $"📍 Bitcoin адрес: {latestResult.BitcoinAddress}";
            BalanceLabel.Text = $"💰 Баланс: {latestResult.Balance} BTC";
            ProcessingTimeLabel.Text = $"📅 Найдено: {latestResult.FoundAt:dd.MM.yyyy HH:mm:ss}";
        }
    }

    private void ShowResult(PrivateKeyResult result)
    {
        ResultLabel.Text = "🎉 Ключ найден!";
        ResultDetails.IsVisible = true;
        
        FoundKeyLabel.Text = $"🔑 Приватный ключ: {result.PrivateKey}";
        AddressLabel.Text = $"📍 Bitcoin адрес: {result.BitcoinAddress}";
        BalanceLabel.Text = $"💰 Баланс: {result.Balance} BTC";
        ProcessingTimeLabel.Text = $"⏱️ Время поиска: {result.ProcessingTime:hh\\:mm\\:ss}";
    }

    private void ClearResults()
    {
        ResultLabel.Text = "🎯 Результаты поиска";
        ResultDetails.IsVisible = false;
        ProgressBar.Progress = 0;
        CurrentKeyLabel.Text = "🔑 Ключ: -";
        CurrentAddressLabel.Text = "📍 Адрес: -";
        ProcessedKeysLabel.Text = "📈 Обработано: 0";
        SpeedLabel.Text = "⚡ Скорость: 0 к/с";
        ProgressLabel.Text = "📊 Прогресс: 0%";
        ElapsedTimeLabel.Text = "⏱️ Время: 00:00:00";
        StatusLabel.Text = "⏳ Статус: Ожидание";
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogLabel.Text += $"\n[{timestamp}] {message}";
    }

    private void OnQuickRangeClicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string range)
        {
            var parts = range.Split('-');
            if (parts.Length == 2 && long.TryParse(parts[0], out var start) && long.TryParse(parts[1], out var end))
            {
                StartIndexEntry.Text = start.ToString();
                EndIndexEntry.Text = end.ToString();
                
                AddLog($"🚀 Установлен диапазон: {start:N0} - {end:N0}");
                
                // Анимация кнопки
                AnimateQuickRangeButton(button);
            }
        }
    }

    private async void AnimateQuickRangeButton(Button button)
    {
        var originalColor = button.BackgroundColor;
        button.BackgroundColor = Color.FromHex("#4CAF50");
        button.TextColor = Colors.White;
        
        await Task.Delay(300);
        
        button.BackgroundColor = originalColor;
        button.TextColor = Color.FromHex("#1976D2");
    }
}

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
            LoadSavedProgress();
            SetupAnimations();
        }
        catch (Exception ex)
        {
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

    private void LoadSavedProgress()
    {
        try
        {
            var progress = progressManager.LoadProgress();
            if (progress != null)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var result = await DisplayAlert("🔄 Продолжить поиск?", 
                        $"Найдено сохранение:\n" +
                        $"📍 Позиция: {progress.CurrentIndex:N0}\n" +
                        $"⏱️ Время: {progress.ElapsedTime:hh\\:mm\\:ss}\n\n" +
                        $"Продолжить с этой позиции?", "✅ ПРОДОЛЖИТЬ", "🔄 ЗАНОВО");
                    
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
                });
            }
        }
        catch (Exception ex)
        {
            // Тихая ошибка загрузки
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки прогресса: {ex.Message}");
        }
    }

    private void RestoreProgress(SearchProgress progress)
    {
        // Восстанавливаем только адрес
        TargetAddressEntry.Text = progress.TargetAddress;
    }

    private async void OnStartClicked(object sender, EventArgs e)
    {
        if (isSearching)
        {
            await DisplayAlert("⚠️ Внимание", "Поиск уже выполняется", "OK");
            return;
        }

        try
        {
            var parameters = GetSearchParameters();
            
            if (parameters.TargetAddress == "")
            {
                return; // Ошибка уже показана в GetSearchParameters
            }
            
            isSearching = true;
            searchStartTime = DateTime.Now;
            cancellationTokenSource = new CancellationTokenSource();
            
            // Анимация запуска
            await AnimateStartSearch();
            
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            
            ClearResults();
            
            // Запускаем поиск
            _ = Task.Run(async () =>
            {
                var success = await backgroundSearchService.StartSearchAsync(parameters, "Автоматический поиск", cancellationTokenSource.Token);
                OnSearchCompleted(success);
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("❌ Ошибка", $"Ошибка запуска поиска: {ex.Message}", "OK");
            isSearching = false;
        }
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
        
        isSearching = false;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        
        StatusLabel.Text = "⏸️ Остановлено";
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
        var targetAddress = TargetAddressEntry.Text?.Trim() ?? "";
        
        if (string.IsNullOrEmpty(targetAddress))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlert("⚠️ Ошибка", "Введите Bitcoin адрес", "OK");
            });
            return new PrivateKeyParameters { TargetAddress = "" };
        }

        // Автоматические настройки
        return new PrivateKeyParameters
        {
            TargetAddress = targetAddress,
            StartIndex = 1, // Начинаем с 1
            EndIndex = long.MaxValue, // До бесконечности
            Format = KeyFormat.Decimal, // Всегда Decimal
            Network = NetworkType.Mainnet, // Всегда Mainnet
            ThreadCount = Environment.ProcessorCount // Автоопределение потоков
        };
    }

    private void OnProgressReported(ProgressInfo progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentKeyLabel.Text = $"🔑 Ключ: {progress.CurrentKey:N0}";
            ProcessedKeysLabel.Text = $"📈 Обработано: {progress.ProcessedKeys:N0}";
            SpeedLabel.Text = $"⚡ Скорость: {progress.KeysPerSecond:N0} к/с";
            
            // Обновляем прогресс-бар (относительно общего диапазона)
            var progressPercent = Math.Min((double)progress.ProcessedKeys / 1000000, 1.0); // Показываем прогресс до 1M
            ProgressBar.Progress = progressPercent;
            
            StatusLabel.Text = "🔍 Поиск...";
        });
    }

    private void OnLogMessage(string message)
    {
        // Убираем лог - не нужен в простом интерфейсе
        System.Diagnostics.Debug.WriteLine($"LOG: {message}");
    }

    private void OnFound(FoundResult result)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            foundResults.Add(result);
            progressManager.SaveFoundResults(foundResults);
            
            await AnimateFoundResult();
            ShowResult(result);
            UpdateResultsDisplay();
        });
    }

    private async Task AnimateFoundResult()
    {
        // Анимация найденного результата
        await ResultLabel.ScaleTo(1.2, 200);
        await ResultLabel.ScaleTo(1.0, 200);
        
        // Мигание результата
        await ResultDetails.FadeTo(0.3, 100);
        await ResultDetails.FadeTo(1.0, 100);
    }

    private void OnProgressSaved(SearchProgress progress)
    {
        // Автосохранение каждые 1000 ключей
        System.Diagnostics.Debug.WriteLine($"Прогресс сохранен: {progress.CurrentIndex:N0}");
    }

    private void OnSearchCompleted(bool success)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            isSearching = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            
            if (success)
            {
                StatusLabel.Text = "✅ Завершено";
            }
            else
            {
                StatusLabel.Text = "❌ Ошибка";
            }
        });
    }

    private void UpdateResultsDisplay()
    {
        if (foundResults.Any())
        {
            ResultDetails.IsVisible = true;
            var lastResult = foundResults.Last();
            ShowResult(lastResult);
        }
    }

    private void ShowResult(FoundResult result)
    {
        FoundKeyLabel.Text = $"🔑 Найден ключ: {result.PrivateKey}";
        AddressLabel.Text = $"📍 Адрес: {result.Address}";
        BalanceLabel.Text = $"💰 Баланс: {result.Balance} BTC";
        ProcessingTimeLabel.Text = $"⏱️ Время: {result.ProcessingTime:hh\\:mm\\:ss}";
    }

    private void ClearResults()
    {
        ResultDetails.IsVisible = false;
        CurrentKeyLabel.Text = "🔑 Ключ: -";
        ProcessedKeysLabel.Text = "📈 Обработано: 0";
        SpeedLabel.Text = "⚡ Скорость: 0 к/с";
        ProgressBar.Progress = 0;
        StatusLabel.Text = "⏳ Ожидание";
    }
}

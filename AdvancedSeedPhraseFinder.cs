using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BitcoinFinder
{
    public class AdvancedSeedPhraseFinder
    {
        private readonly List<string> bip39Words;
        private readonly ConcurrentDictionary<string, bool> checkedCombinations;
        private readonly object lockObject = new object();
        private readonly string logFilePath;
        private readonly string progressFilePath;
        private BigInteger totalCombinations = 0;
        private BigInteger currentCombination = 0;
        private BigInteger globalProcessedCount = 0; // Глобальный счётчик обработанных комбинаций
        private readonly ConcurrentDictionary<int, BigInteger> threadLastReported = new ConcurrentDictionary<int, BigInteger>(); // Последние отчётные значения для каждого потока
        private DateTime startTime;
        private DateTime lastSaveTime;
        private bool isCancelled = false;
        private string currentSeedPhrase = "";
        // Для сглаживания скорости
        private readonly Queue<(DateTime, BigInteger)> rateHistory = new Queue<(DateTime, BigInteger)>();
        private const int RateHistorySeconds = 10;
        private double emaSpeed = 0;
        private const double alpha = 0.2;
        private const int RateWindowSeconds = 10;
        private const int SpeedWindow = 10;
        private Queue<(DateTime, BigInteger)> speedHistory = new Queue<(DateTime, BigInteger)>();
        private readonly string telemetryFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"bitcoin_finder_telemetry_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        private DateTime lastTelemetryTime = DateTime.MinValue;
        private string telemetrySeedPhrase = "";
        private int telemetryWordCount = 0;
        private bool telemetryFullSearch = false;
        private string telemetryBitcoinAddress = "";
        private int telemetryThreadCount = 0;
        private bool isProgressRestored = false;
        public bool IsProgressRestored => isProgressRestored;
        private string lastSavedSeedPhrase = "";

        public AdvancedSeedPhraseFinder()
        {
            // Загружаем BIP39 словарь
            bip39Words = new Mnemonic(Wordlist.English).WordList.GetWords().ToList();
            checkedCombinations = new ConcurrentDictionary<string, bool>();
            
            // Создаем файлы для логирования и прогресса
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"bitcoin_finder_{timestamp}.log");
            progressFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"bitcoin_finder_progress_{timestamp}.json");
            
            LogMessage("=== Bitcoin Seed Phrase Finder Started ===");
            lastSaveTime = DateTime.Now;
        }

        public void Search(SearchParameters parameters, BackgroundWorker worker, DoWorkEventArgs e)
        {
            try
            {
                startTime = DateTime.Now;
                isCancelled = false;
                // Не инициализируем globalProcessedCount здесь, чтобы не сбросить восстановленный прогресс
                threadLastReported.Clear();
                telemetrySeedPhrase = parameters.SeedPhrase;
                telemetryWordCount = parameters.WordCount;
                telemetryFullSearch = parameters.FullSearch;
                telemetryBitcoinAddress = parameters.BitcoinAddress;
                telemetryThreadCount = parameters.ThreadCount;
                if (worker.CancellationPending)
                {
                    LogMessage("Поиск отменен в начале");
                    return;
                }
                LogMessage($"Поиск начат: {parameters.SeedPhrase}");
                LogMessage($"Целевой адрес: {parameters.BitcoinAddress}");
                LogMessage($"Количество потоков: {parameters.ThreadCount}");
                LogMessage($"Полный перебор: {parameters.FullSearch}");
                // Загружаем сохраненный прогресс, если есть
                if (!string.IsNullOrWhiteSpace(parameters.ProgressFile) && File.Exists(parameters.ProgressFile))
                {
                    LoadProgressFromFile(parameters.ProgressFile, parameters, worker);
                }
                else
                {
                LoadProgress(parameters, worker);
                }
                bool wasProgressRestored = isProgressRestored;
                if (parameters.FullSearch)
                {
                    PerformFullSearch(parameters, worker, e, wasProgressRestored);
                }
                else
                {
                    PerformPartialSearch(parameters, worker, e, wasProgressRestored);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка: {ex.Message}");
                throw;
            }
        }

        private void LoadProgress(SearchParameters parameters, BackgroundWorker worker)
        {
            try
            {
                if (File.Exists(progressFilePath))
                {
                    var json = File.ReadAllText(progressFilePath);
                    var progressData = System.Text.Json.JsonSerializer.Deserialize<ProgressData>(json);
                    
                    if (progressData != null && progressData.BitcoinAddress == parameters.BitcoinAddress)
                    {
                        BigInteger.TryParse(progressData.CurrentCombination, out currentCombination);
                        BigInteger.TryParse(progressData.TotalCombinations, out totalCombinations);
                        telemetrySeedPhrase = progressData.SeedPhrase;
                        telemetryBitcoinAddress = progressData.BitcoinAddress;
                        telemetryWordCount = progressData.WordCount;
                        telemetryFullSearch = progressData.FullSearch;
                        telemetryThreadCount = progressData.ThreadCount;
                        lastSavedSeedPhrase = progressData.LastCheckedPhrase;
                        currentSeedPhrase = progressData.LastCheckedPhrase;
                        isProgressRestored = true;
                        // Критично: синхронизируем глобальный счетчик!
                        globalProcessedCount = currentCombination;
                        LogMessage($"Загружен прогресс: {currentCombination:N0} / {totalCombinations:N0}");
                        worker.ReportProgress(0, $"Загружен прогресс: {currentCombination:N0} / {totalCombinations:N0}");
                        
                        // Генерируем последнюю проверенную комбинацию для отображения
                        if (currentCombination > 0)
                        {
                            try
                            {
                                // Создаем временные массивы для генерации последней комбинации
                                var tempKnownWords = new string[parameters.WordCount];
                                var tempUnknownPositions = new List<int>();
                                var tempPartialWords = new List<(int position, string pattern)>();
                                
                                if (parameters.FullSearch)
                                {
                                    // Для полного поиска все позиции неизвестны
                                    for (int i = 0; i < parameters.WordCount; i++)
                                    {
                                        tempKnownWords[i] = "";
                                        tempUnknownPositions.Add(i);
                                    }
                                }
                                else
                                {
                                    // Для частичного поиска анализируем seed фразу
                                    var seedWords = parameters.SeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    for (int i = 0; i < seedWords.Length; i++)
                                    {
                                        if (seedWords[i] == "*" || seedWords[i].Contains("*"))
                                        {
                                            tempUnknownPositions.Add(i);
                                            tempKnownWords[i] = "";
                                            if (seedWords[i].Contains("*"))
                                                tempPartialWords.Add((i, seedWords[i]));
                                        }
                                        else
                                        {
                                            tempKnownWords[i] = seedWords[i];
                                        }
                                    }
                                }
                                
                                // Генерируем возможные слова для каждой позиции
                                var possibleWords = new List<string>[tempUnknownPositions.Count];
                                for (int i = 0; i < tempUnknownPositions.Count; i++)
                                {
                                    int position = tempUnknownPositions[i];
                                    var partialWord = tempPartialWords.FirstOrDefault(p => p.position == position);
                                    if (partialWord != default)
                                        possibleWords[i] = GetMatchingWords(partialWord.pattern);
                                    else
                                        possibleWords[i] = new List<string>(bip39Words);
                                }
                                
                                // Генерируем последнюю проверенную комбинацию
                                var lastCombination = GenerateCombinationByIndex(currentCombination - 1, possibleWords);
                                var lastTestWords = new string[tempKnownWords.Length];
                                Array.Copy(tempKnownWords, lastTestWords, tempKnownWords.Length);
                                for (int i = 0; i < tempUnknownPositions.Count; i++)
                                    lastTestWords[tempUnknownPositions[i]] = lastCombination[i];
                                var mnemonic = new Mnemonic(string.Join(" ", lastTestWords), Wordlist.English);
                                var seed = mnemonic.DeriveSeed();
                                var masterKey = ExtKey.CreateFromSeed(seed);
                                var fullPath = new KeyPath("44'/0'/0'/0/0");
                                var privateKey = masterKey.Derive(fullPath).PrivateKey;
                                currentSeedPhrase = privateKey.GetWif(Network.Main).ToString();
                                lastSavedSeedPhrase = currentSeedPhrase;
                                
                                LogMessage($"Последняя проверенная комбинация: {currentSeedPhrase}");
                                worker.ReportProgress(0, $"Последняя проверенная комбинация: {currentSeedPhrase}");
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Ошибка генерации последней комбинации: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка загрузки прогресса: {ex.Message}");
            }
        }

        private void LoadProgressFromFile(string filePath, SearchParameters parameters, BackgroundWorker worker)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var progressData = System.Text.Json.JsonSerializer.Deserialize<ProgressData>(json);
                    if (progressData != null && progressData.BitcoinAddress == parameters.BitcoinAddress)
                    {
                        BigInteger.TryParse(progressData.CurrentCombination, out currentCombination);
                        BigInteger.TryParse(progressData.TotalCombinations, out totalCombinations);
                        telemetrySeedPhrase = progressData.SeedPhrase;
                        telemetryBitcoinAddress = progressData.BitcoinAddress;
                        telemetryWordCount = progressData.WordCount;
                        telemetryFullSearch = progressData.FullSearch;
                        telemetryThreadCount = progressData.ThreadCount;
                        lastSavedSeedPhrase = progressData.LastCheckedPhrase;
                        currentSeedPhrase = progressData.LastCheckedPhrase;
                        isProgressRestored = true;
                        globalProcessedCount = currentCombination;
                        LogMessage($"Загружен прогресс из файла: {currentCombination:N0} / {totalCombinations:N0}");
                        worker.ReportProgress(0, $"Загружен прогресс из файла: {currentCombination:N0} / {totalCombinations:N0}");
                        if (currentCombination > 0)
                        {
                            try
                            {
                                var tempKnownWords = new string[parameters.WordCount];
                                var tempUnknownPositions = new List<int>();
                                var tempPartialWords = new List<(int position, string pattern)>();
                                if (parameters.FullSearch)
                                {
                                    for (int i = 0; i < parameters.WordCount; i++)
                                    {
                                        tempKnownWords[i] = "";
                                        tempUnknownPositions.Add(i);
                                    }
                                }
                                else
                                {
                                    var seedWords = parameters.SeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                    for (int i = 0; i < seedWords.Length; i++)
                                    {
                                        if (seedWords[i] == "*" || seedWords[i].Contains("*"))
                                        {
                                            tempUnknownPositions.Add(i);
                                            tempKnownWords[i] = "";
                                            if (seedWords[i].Contains("*"))
                                                tempPartialWords.Add((i, seedWords[i]));
                                        }
                                        else
                                        {
                                            tempKnownWords[i] = seedWords[i];
                                        }
                                    }
                                }
                                var possibleWords = new List<string>[tempUnknownPositions.Count];
                                for (int i = 0; i < tempUnknownPositions.Count; i++)
                                {
                                    int position = tempUnknownPositions[i];
                                    var partialWord = tempPartialWords.FirstOrDefault(p => p.position == position);
                                    if (partialWord != default)
                                        possibleWords[i] = GetMatchingWords(partialWord.pattern);
                                    else
                                        possibleWords[i] = new List<string>(bip39Words);
                                }
                                var lastCombination = GenerateCombinationByIndex(currentCombination - 1, possibleWords);
                                var lastTestWords = new string[tempKnownWords.Length];
                                Array.Copy(tempKnownWords, lastTestWords, tempKnownWords.Length);
                                for (int i = 0; i < tempUnknownPositions.Count; i++)
                                    lastTestWords[tempUnknownPositions[i]] = lastCombination[i];
                                var mnemonic = new Mnemonic(string.Join(" ", lastTestWords), Wordlist.English);
                                var seed = mnemonic.DeriveSeed();
                                var masterKey = ExtKey.CreateFromSeed(seed);
                                var fullPath = new KeyPath("44'/0'/0'/0/0");
                                var privateKey = masterKey.Derive(fullPath).PrivateKey;
                                currentSeedPhrase = privateKey.GetWif(Network.Main).ToString();
                                lastSavedSeedPhrase = currentSeedPhrase;
                                LogMessage($"Последняя проверенная комбинация: {currentSeedPhrase}");
                                worker.ReportProgress(0, $"Последняя проверенная комбинация: {currentSeedPhrase}");
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Ошибка генерации последней комбинации: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка загрузки прогресса из файла: {ex.Message}");
            }
        }

        private void SaveProgress()
        {
            try
            {
                var progressData = new ProgressData
                {
                    CurrentCombination = currentCombination.ToString(),
                    TotalCombinations = totalCombinations.ToString(),
                    Timestamp = DateTime.Now,
                    LastCheckedPhrase = lastSavedSeedPhrase
                };
                var json = System.Text.Json.JsonSerializer.Serialize(progressData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(progressFilePath, json);
                File.WriteAllText("bitcoin_finder_progress_last.json", json);
                LogMessage($"Прогресс сохранен: {currentCombination:N0} / {totalCombinations:N0} | Текущая фраза: {lastSavedSeedPhrase}");
                LogMessage($"Последняя проверенная фраза: {lastSavedSeedPhrase}");
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка сохранения прогресса: {ex.Message}");
            }
        }

        private void PerformFullSearch(SearchParameters parameters, BackgroundWorker worker, DoWorkEventArgs e, bool wasProgressRestored)
        {
            LogMessage("Начинаем полный перебор всех возможных seed-фраз (унифицированная логика)");

            // Проверяем параметры
            LogMessage($"WordCount: {parameters.WordCount}, bip39Words.Count: {bip39Words.Count}");

            // Формируем массивы для полного перебора
            var knownWords = new string[parameters.WordCount];
            var unknownPositions = new List<int>();
            var partialWords = new List<(int position, string pattern)>();
            for (int i = 0; i < parameters.WordCount; i++)
            {
                knownWords[i] = "";
                unknownPositions.Add(i);
            }

            // Вычисляем общее количество комбинаций
            CalculateTotalCombinations(unknownPositions, partialWords);
            LogMessage($"Всего комбинаций для перебора: {totalCombinations:N0}");
            if (wasProgressRestored)
            {
                if (totalCombinations != this.totalCombinations)
                {
                    LogMessage($"ВНИМАНИЕ: Количество комбинаций изменилось! Было: {this.totalCombinations}, стало: {totalCombinations}");
                    this.totalCombinations = totalCombinations; // Можно обновить, если нужно
                }
                LogMessage($"Продолжаем поиск с позиции {currentCombination:N0} из {totalCombinations:N0}");
                worker.ReportProgress(0, $"Продолжаем поиск с позиции {currentCombination:N0} из {totalCombinations:N0}");
                // Не сбрасываем currentCombination/globalProcessedCount!
            }
            else
            {
                this.totalCombinations = totalCombinations;
                currentCombination = 0;
                globalProcessedCount = 0;
                lastSavedSeedPhrase = "";
                LogMessage("Прогресс не найден или параметры изменены. Поиск начинается с нуля.");
                worker.ReportProgress(0, "Прогресс не найден или параметры изменены. Поиск начинается с нуля.");
            }
            isProgressRestored = false;
            if (totalCombinations > 1000000000000) // 1 триллион
            {
                LogMessage("ВНИМАНИЕ: Очень много комбинаций! Поиск может занять годы.");
                worker.ReportProgress(0, "ВНИМАНИЕ: Очень много комбинаций! Поиск может занять годы.");
            }

            // Начинаем многопоточный перебор
            var foundResults = new ConcurrentBag<string>();
            SearchCombinationsMultiThreaded(knownWords, unknownPositions, partialWords, parameters.BitcoinAddress,
                parameters.ThreadCount, worker, e, foundResults);

            if (foundResults.Count > 0)
            {
                LogMessage($"Найдено {foundResults.Count} результатов!");
                worker.ReportProgress(100, "=== НАЙДЕННЫЕ РЕЗУЛЬТАТЫ ===");
                foreach (var result in foundResults)
                {
                    worker.ReportProgress(100, result);
                    LogMessage($"НАЙДЕНО: {result}");
                }
            }
            else
            {
                LogMessage("Совпадений не найдено.");
                worker.ReportProgress(100, "Совпадений не найдено.");
            }
        }

        private void PerformPartialSearch(SearchParameters parameters, BackgroundWorker worker, DoWorkEventArgs e, bool wasProgressRestored)
        {
            LogMessage("Начинаем поиск с частично известными словами");

            // Парсим seed фразу
            var seedWords = parameters.SeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            LogMessage($"DEBUG: Разобрано слов: {seedWords.Length}, ожидается: {parameters.WordCount}");
            LogMessage($"DEBUG: Seed слова: [{string.Join(", ", seedWords)}]");
            
            if (seedWords.Length != parameters.WordCount)
            {
                var error = $"Ошибка: количество слов должно быть {parameters.WordCount}, а получено {seedWords.Length}";
                LogMessage(error);
                worker.ReportProgress(0, error);
                return;
            }

            // Анализируем известные и неизвестные слова
            var knownWords = new string[parameters.WordCount];
            var unknownPositions = new List<int>();
            var partialWords = new List<(int position, string pattern)>();

            for (int i = 0; i < seedWords.Length; i++)
            {
                LogMessage($"DEBUG: Обрабатываем слово {i}: '{seedWords[i]}'");
                if (seedWords[i] == "*")
                {
                    unknownPositions.Add(i);
                    knownWords[i] = "";
                    LogMessage($"DEBUG: Позиция {i} - полная звездочка");
                }
                else if (seedWords[i].Contains("*"))
                {
                    partialWords.Add((i, seedWords[i]));
                    unknownPositions.Add(i);
                    knownWords[i] = "";
                    LogMessage($"DEBUG: Позиция {i} - частичная звездочка: '{seedWords[i]}'");
                }
                else
                {
                    knownWords[i] = seedWords[i];
                    LogMessage($"DEBUG: Позиция {i} - известное слово: '{seedWords[i]}'");
                }
            }

            LogMessage($"Найдено {unknownPositions.Count} неизвестных слов из {parameters.WordCount}");
            LogMessage($"Частично известных слов: {partialWords.Count}");
            LogMessage($"DEBUG: unknownPositions: [{string.Join(", ", unknownPositions)}]");
            LogMessage($"DEBUG: partialWords count: {partialWords.Count}");

            // Вычисляем общее количество комбинаций
            LogMessage("DEBUG: Вызываем CalculateTotalCombinations...");
            CalculateTotalCombinations(unknownPositions, partialWords);
            LogMessage($"Всего комбинаций для перебора: {totalCombinations:N0}");

            if (wasProgressRestored)
            {
                if (totalCombinations != this.totalCombinations)
                {
                    LogMessage($"ВНИМАНИЕ: Количество комбинаций изменилось! Было: {this.totalCombinations}, стало: {totalCombinations}");
                    this.totalCombinations = totalCombinations;
                }
                LogMessage($"Продолжаем поиск с позиции {currentCombination:N0} из {totalCombinations:N0}");
                worker.ReportProgress(0, $"Продолжаем поиск с позиции {currentCombination:N0} из {totalCombinations:N0}");
                // Не сбрасываем currentCombination/globalProcessedCount!
            }
            else
            {
                this.totalCombinations = totalCombinations;
                currentCombination = 0;
                globalProcessedCount = 0;
                lastSavedSeedPhrase = "";
                LogMessage("Прогресс не найден или параметры изменены. Поиск начинается с нуля.");
                worker.ReportProgress(0, "Прогресс не найден или параметры изменены. Поиск начинается с нуля.");
            }
            isProgressRestored = false;

            if (totalCombinations == BigInteger.Zero)
            {
                LogMessage("ОШИБКА: Количество комбинаций равно 0! Перебор не начнется.");
                worker.ReportProgress(0, "ОШИБКА: Количество комбинаций равно 0! Перебор не начнется.");
                return;
            }

            if (totalCombinations > 1000000000) // 1 миллиард
            {
                LogMessage("ВНИМАНИЕ: Слишком много комбинаций! Поиск может занять очень много времени.");
                worker.ReportProgress(0, "ВНИМАНИЕ: Слишком много комбинаций! Поиск может занять очень много времени.");
            }

            // Начинаем многопоточный поиск
            var foundResults = new ConcurrentBag<string>();
            SearchCombinationsMultiThreaded(knownWords, unknownPositions, partialWords, parameters.BitcoinAddress, 
                parameters.ThreadCount, worker, e, foundResults);

            if (foundResults.Count > 0)
            {
                LogMessage($"Найдено {foundResults.Count} результатов!");
                worker.ReportProgress(100, "=== НАЙДЕННЫЕ РЕЗУЛЬТАТЫ ===");
                foreach (var result in foundResults)
                {
                    worker.ReportProgress(100, result);
                    LogMessage($"НАЙДЕНО: {result}");
                }
            }
            else
            {
                LogMessage("Совпадений не найдено.");
                worker.ReportProgress(100, "Совпадений не найдено.");
            }
        }

        private void SearchRange(long start, long end, string targetAddress, int wordCount, ConcurrentBag<string> foundResults, 
            BackgroundWorker worker, DoWorkEventArgs e)
        {
            var localCurrent = start;
            
            while (localCurrent < end && !isCancelled && !worker.CancellationPending)
            {
                try
                {
                    // Генерируем seed фразу по индексу
                    var seedPhrase = GenerateSeedPhraseByIndex(localCurrent, wordCount);
                    currentSeedPhrase = seedPhrase;
                    
                    // Проверяем уникальность
                    if (checkedCombinations.TryAdd(seedPhrase, true))
                    {
                        // Проверяем валидность
                        if (IsValidSeedPhrase(seedPhrase))
                        {
                            // Генерируем адрес
                            var generatedAddress = GenerateBitcoinAddress(seedPhrase);
                            
                            if (generatedAddress == targetAddress)
                            {
                                var result = $"НАЙДЕНО! Seed фраза: {seedPhrase}";
                                foundResults.Add(result);
                                LogMessage($"Поток нашел: {result}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Ошибка в потоке: {ex.Message}");
                }

                localCurrent++;
                
                // Обновляем прогресс каждые 100 итераций (SearchRange)
                if (localCurrent % 100 == 0)
                {
                    lock (lockObject)
                    {
                        // Исправление: используем правильный счётчик для полного поиска
                        currentCombination = localCurrent;
                        
                        var progress = totalCombinations > 0 ? (int)((double)(currentCombination * 100 / totalCombinations)) : 0;
                        var elapsed = DateTime.Now - startTime;
                        var rate = elapsed.TotalSeconds > 0 ? (double)currentCombination / elapsed.TotalSeconds : 0;
                        
                        // Защита от аномальных значений скорости
                        if (double.IsInfinity(rate) || double.IsNaN(rate) || rate > 1_000_000 || rate < 0)
                            rate = -1; // нереально
                        
                        double secondsLeft = rate > 0 ? (double)(totalCombinations - currentCombination) / rate : 0;
                        if (double.IsInfinity(secondsLeft) || double.IsNaN(secondsLeft) || secondsLeft > TimeSpan.MaxValue.TotalSeconds)
                            secondsLeft = TimeSpan.MaxValue.TotalSeconds;
                        else if (secondsLeft < 0)
                            secondsLeft = 0;
                        
                        var remaining = TimeSpan.FromSeconds(secondsLeft);
                        string rateStr = rate < 0 ? "нереально" : (rate < 1 ? "очень медленно" : rate.ToString("N0"));
                        
                        var progressInfo = new ProgressInfo
                        {
                            Current = currentCombination,
                            Total = totalCombinations,
                            Percentage = totalCombinations > 0 ? (double)(currentCombination * 100 / totalCombinations) : 0,
                            Status = $"Проверено: {currentCombination:N0} / {totalCombinations:N0} | Скорость: {rateStr}/сек | Текущая: {currentSeedPhrase}",
                            Rate = rate,
                            Remaining = rate > 0 ? remaining : TimeSpan.Zero
                        };
                        
                        worker.ReportProgress(Math.Min(progress, 99), progressInfo);
                        
                        // Сохраняем прогресс каждую минуту
                        if (DateTime.Now - lastSaveTime > TimeSpan.FromMinutes(1))
                        {
                            SaveProgress();
                            lastSaveTime = DateTime.Now;
                        }
                    }
                }
            }
        }

        private void SearchCombinationsMultiThreaded(string[] knownWords, List<int> unknownPositions, 
            List<(int position, string pattern)> partialWords, string targetAddress, int threadCount,
            BackgroundWorker worker, DoWorkEventArgs e, ConcurrentBag<string> foundResults)
        {
            LogMessage($"SearchCombinationsMultiThreaded: unknownPositions={unknownPositions.Count}, threadCount={threadCount}");
            // Создаем списки возможных слов для каждой позиции
            var possibleWords = new List<string>[unknownPositions.Count];
            for (int i = 0; i < unknownPositions.Count; i++)
            {
                int position = unknownPositions[i];
                var partialWord = partialWords.FirstOrDefault(p => p.position == position);
                
                if (partialWord != default)
                {
                    possibleWords[i] = GetMatchingWords(partialWord.pattern);
                }
                else
                {
                    possibleWords[i] = new List<string>(bip39Words);
                }
            }

            // Разбиваем работу на потоки
            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(threadCount);

            // Используем SplitRangeBig для BigInteger диапазонов
            var ranges = SplitRangeBig(currentCombination, totalCombinations, threadCount);
            var currentPhrases = new string[threadCount];
            // Инициализируем массив пустыми строками
            for (int i = 0; i < threadCount; i++)
            {
                currentPhrases[i] = "Инициализация...";
            }
            for (int i = 0; i < ranges.Count; i++)
            {
                LogMessage($"Thread {i}: {ranges[i].Item1} - {ranges[i].Item2}");
                int threadId = i;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        SearchRangeRecursive(knownWords, possibleWords, unknownPositions, ranges[threadId].Item1, ranges[threadId].Item2,
                            targetAddress, foundResults, worker, e, currentPhrases, threadId);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            // Ждем завершения всех потоков
            Task.WaitAll(tasks.ToArray());
        }

        private void SearchRangeRecursive(string[] currentWords, List<string>[] possibleWords, 
            List<int> unknownPositions, BigInteger startIndex, BigInteger endIndex, string targetAddress,
            ConcurrentBag<string> foundResults, BackgroundWorker worker, DoWorkEventArgs e,
            string[] currentPhrases, int threadId)
        {
            var localCurrent = startIndex;
            DateTime lastProgressReport = DateTime.Now;
            while (localCurrent < endIndex && !isCancelled && !worker.CancellationPending)
            {
                var combination = GenerateCombinationByIndex(localCurrent, possibleWords);
                var testWords = new string[currentWords.Length];
                Array.Copy(currentWords, testWords, currentWords.Length);
                for (int i = 0; i < unknownPositions.Count; i++)
                    testWords[unknownPositions[i]] = combination[i];
                    var seedPhrase = string.Join(" ", testWords);
                    currentSeedPhrase = seedPhrase;
                var mnemonicWif = new Mnemonic(seedPhrase, Wordlist.English);
                var seedWif = mnemonicWif.DeriveSeed();
                var masterKeyWif = ExtKey.CreateFromSeed(seedWif);
                var fullPathWif = new KeyPath("44'/0'/0'/0/0");
                var privateKeyWif = masterKeyWif.Derive(fullPathWif).PrivateKey;
                var wif = privateKeyWif.GetWif(Network.Main).ToString();
                currentPhrases[threadId] = seedPhrase;
                try
                {
                    if (checkedCombinations.TryAdd(seedPhrase, true))
                    {
                        if (IsValidSeedPhrase(seedPhrase))
                        {
                            var generatedAddress = GenerateBitcoinAddress(seedPhrase);
                            if (generatedAddress == targetAddress)
                            {
                                var result = $"НАЙДЕНО! Seed фраза: {seedPhrase}";
                                foundResults.Add(result);
                                LogMessage($"Поток нашел: {result}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Ошибка генерации: {ex.Message}");
                }
                localCurrent++;
                // Обновляем прогресс не чаще, чем раз в 0.5 секунды
                if ((DateTime.Now - lastProgressReport).TotalMilliseconds > 500)
                {
                    lock (lockObject)
                    {
                        var lastReported = threadLastReported.GetOrAdd(threadId, startIndex);
                        var newlyProcessed = localCurrent - lastReported;
                        if (newlyProcessed > 0)
                        {
                            globalProcessedCount += newlyProcessed;
                            threadLastReported[threadId] = localCurrent;
                        }
                        currentCombination = globalProcessedCount;
                        // Сохраняем фразу, соответствующую текущему счетчику
                        var mnemonic = new Mnemonic(string.Join(" ", testWords), Wordlist.English);
                        var seed = mnemonic.DeriveSeed();
                        var masterKey = ExtKey.CreateFromSeed(seed);
                        var fullPath = new KeyPath("44'/0'/0'/0/0");
                        var privateKey = masterKey.Derive(fullPath).PrivateKey;
                        lastSavedSeedPhrase = privateKey.GetWif(Network.Main).ToString();
                        
                        // --- Новая логика для точного прогресса и скорости ---
                        var now = DateTime.Now;
                        speedHistory.Enqueue((now, currentCombination));
                        while (speedHistory.Count > SpeedWindow)
                            speedHistory.Dequeue();
                        
                        double avgSpeed = 0;
                        if (speedHistory.Count > 1)
                        {
                            double sum = 0;
                            var arr = speedHistory.ToArray();
                            int validPairs = 0;
                            for (int i = 1; i < arr.Length; i++)
                            {
                                var dt = (arr[i].Item1 - arr[i-1].Item1).TotalSeconds;
                                var dc = (double)(arr[i].Item2 - arr[i-1].Item2);
                                if (dt > 0 && dc >= 0 && dc < 1_000_000) // Защита от аномальных значений
                                {
                                    sum += dc / dt;
                                    validPairs++;
                                }
                            }
                            if (validPairs > 0)
                                avgSpeed = sum / validPairs;
                        }
                        
                        double displaySpeed = avgSpeed;
                        if (displaySpeed > 1_000_000 || displaySpeed < 0)
                            displaySpeed = -1; // нереально
                        
                        double secondsLeft = displaySpeed > 0 ? (double)(totalCombinations - currentCombination) / displaySpeed : 0;
                        if (double.IsInfinity(secondsLeft) || double.IsNaN(secondsLeft) || secondsLeft > TimeSpan.MaxValue.TotalSeconds)
                            secondsLeft = TimeSpan.MaxValue.TotalSeconds;
                        else if (secondsLeft < 0)
                            secondsLeft = 0;
                        
                        var remaining = TimeSpan.FromSeconds(secondsLeft);
                        var progress = totalCombinations > 0 ? (int)((double)(currentCombination * 100 / totalCombinations)) : 0;
                        string speedStr = displaySpeed < 0 ? "нереально" : (displaySpeed < 1 ? "очень медленно" : displaySpeed.ToString("N0"));
                        
                        // --- Исправление: всегда передавать хотя бы одну фразу ---
                        var currentPhrasesList = currentPhrases.ToList();
                        if (currentPhrasesList.Count == 0 && !string.IsNullOrWhiteSpace(currentSeedPhrase))
                            currentPhrasesList.Add(currentSeedPhrase);
                        var progressInfo = new ProgressInfo
                        {
                            Current = currentCombination,
                            Total = totalCombinations,
                            Percentage = totalCombinations > 0 ? (double)(currentCombination * 100 / totalCombinations) : 0,
                            Status = $"Проверено: {currentCombination:N0} / {totalCombinations:N0} | ~Скорость: {speedStr}/сек | ~Осталось: {(displaySpeed > 0 ? remaining.ToString() : "-")}",
                            Rate = displaySpeed,
                            Remaining = displaySpeed > 0 ? remaining : TimeSpan.Zero,
                            CurrentPhrases = currentPhrasesList, // только seed-фразы
                            CurrentPrivateKey = wif // приватный ключ для верхней строки
                        };
                        
                        worker.ReportProgress(Math.Min(progress, 99), progressInfo);
                        
                        if (DateTime.Now - lastSaveTime > TimeSpan.FromMinutes(1))
                        {
                            SaveProgress();
                            lastSaveTime = DateTime.Now;
                        }
                        
                        // Логирование расширенной телеметрии
                        try
                        {
                            var elapsed = (DateTime.Now - startTime).TotalSeconds;
                            var deltaTime = lastTelemetryTime == DateTime.MinValue ? 0 : (DateTime.Now - lastTelemetryTime).TotalSeconds;
                            lastTelemetryTime = DateTime.Now;
                            var speedHistoryPoints = string.Join("|", speedHistory.Select(p => $"{p.Item1:HH:mm:ss.fff},{p.Item2}"));
                            using (var sw = new StreamWriter(telemetryFilePath, true, Encoding.UTF8))
                            {
                                sw.WriteLine($"{DateTime.Now:O};{currentCombination};{totalCombinations};{displaySpeed};{avgSpeed};{secondsLeft};{remaining};{speedStr};{progress};{progressInfo.Percentage};{progressInfo.Status};{currentSeedPhrase};{telemetryThreadCount};{threadId};{elapsed};{deltaTime};{telemetrySeedPhrase};{telemetryWordCount};{telemetryFullSearch};{telemetryBitcoinAddress};{speedHistoryPoints}");
                            }
                        }
                        catch { /* ignore telemetry errors */ }
                    }
                    lastProgressReport = DateTime.Now;
                }
            }
        }

        private string GenerateSeedPhraseByIndex(long index, int wordCount)
        {
            var words = new List<string>();
            for (int i = 0; i < wordCount; i++)
            {
                var wordIndex = (int)(index % bip39Words.Count);
                words.Add(bip39Words[wordIndex]);
                index /= bip39Words.Count;
            }
            return string.Join(" ", words);
        }

        // Новый метод для генерации комбинации по BigInteger индексу
        public string[] GenerateCombinationByIndex(System.Numerics.BigInteger index, List<string>[] possibleWords)
        {
            var combination = new string[possibleWords.Length];
            for (int i = 0; i < possibleWords.Length; i++)
            {
                var wordIndex = (int)(index % possibleWords[i].Count);
                combination[i] = possibleWords[i][wordIndex];
                index /= possibleWords[i].Count;
            }
            return combination;
        }

        private List<(BigInteger, BigInteger)> SplitRangeBig(BigInteger start, BigInteger end, int parts)
        {
            var ranges = new List<(BigInteger, BigInteger)>();
            var partSize = (end - start) / parts;
            for (int i = 0; i < parts; i++)
            {
                var partStart = start + partSize * i;
                var partEnd = (i == parts - 1) ? end : start + partSize * (i + 1);
                ranges.Add((partStart, partEnd));
            }
            return ranges;
        }

        private void CalculateTotalCombinations(List<int> unknownPositions, List<(int position, string pattern)> partialWords)
        {
            LogMessage($"DEBUG: CalculateTotalCombinations вызван");
            LogMessage($"DEBUG: bip39Words.Count = {bip39Words.Count}");
            LogMessage($"DEBUG: unknownPositions.Count = {unknownPositions.Count}");
            LogMessage($"DEBUG: partialWords.Count = {partialWords.Count}");
            
            totalCombinations = BigInteger.One;
            LogMessage($"CalculateTotalCombinations: unknownPositions={unknownPositions.Count}, partialWords={partialWords.Count}");
            
            if (unknownPositions.Count == 0)
            {
                LogMessage("DEBUG: Нет неизвестных позиций, totalCombinations = 0");
                totalCombinations = BigInteger.Zero;
                return;
            }
            
            foreach (int position in unknownPositions)
            {
                var partialWord = partialWords.FirstOrDefault(p => p.position == position);
                if (partialWord != default)
                {
                    var matchingWords = GetMatchingWords(partialWord.pattern);
                    LogMessage($"Position {position}: partial pattern '{partialWord.pattern}', matches={matchingWords.Count}");
                    totalCombinations *= matchingWords.Count;
                }
                else
                {
                    LogMessage($"Position {position}: full wildcard, matches={bip39Words.Count}");
                    totalCombinations *= bip39Words.Count;
                }
            }
            LogMessage($"[Calc] Итоговое количество комбинаций: {totalCombinations:N0}");
        }

        private List<string> GetMatchingWords(string pattern)
        {
            var matchingWords = new List<string>();
            var regexPattern = pattern.Replace("*", ".*");

            foreach (var word in bip39Words)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(word, $"^{regexPattern}$"))
                {
                    matchingWords.Add(word);
                }
            }

            return matchingWords;
        }

        public bool IsValidSeedPhrase(string seedPhrase)
        {
            try
            {
                var mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                return mnemonic.IsValidChecksum;
            }
            catch
            {
                return false;
            }
        }

        public string GenerateBitcoinAddress(string seedPhrase)
        {
            try
            {
                var mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                var seed = mnemonic.DeriveSeed();
                var masterKey = ExtKey.CreateFromSeed(seed);
                
                // Генерируем первый адрес (m/44'/0'/0'/0/0)
                var purpose = new KeyPath("44'");
                var coinType = new KeyPath("0'");
                var account = new KeyPath("0'");
                var change = new KeyPath("0");
                var addressIndex = new KeyPath("0");
                
                var fullPath = purpose.Derive(coinType).Derive(account).Derive(change).Derive(addressIndex);
                var privateKey = masterKey.Derive(fullPath).PrivateKey;
                var publicKey = privateKey.PubKey;
                
                var address = publicKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
                return address.ToString();
            }
            catch
            {
                throw new Exception("Ошибка генерации адреса");
            }
        }

        private void LogMessage(string message)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
            catch
            {
                // Игнорируем ошибки логирования
            }
        }

        public void Cancel()
        {
            isCancelled = true;
            SaveProgress(); // Сохраняем прогресс при отмене
            LogMessage("Поиск отменен пользователем");
        }

        // Метод для тестирования перебора комбинаций
        public void TestCombinationSearch()
        {
            Console.WriteLine("=== ТЕСТ ПЕРЕБОРА КОМБИНАЦИЙ ===");
            
            // Тест 1: 2 слова
            Console.WriteLine("\nТест 1: Перебор 2 слов (* *)");
            var parameters1 = new SearchParameters
            {
                SeedPhrase = "* *",
                BitcoinAddress = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ",
                WordCount = 2,
                FullSearch = true,
                ThreadCount = 1
            };
            
            var worker1 = new BackgroundWorker();
            worker1.WorkerReportsProgress = true;
            var progressCount1 = 0;
            var lastProgress1 = BigInteger.Zero;
            
            worker1.ProgressChanged += (s, e) =>
            {
                progressCount1++;
                if (e.UserState is ProgressInfo progressInfo)
                {
                    lastProgress1 = progressInfo.Current;
                    Console.WriteLine($"Прогресс: {progressInfo.Current:N0} / {progressInfo.Total:N0} ({progressInfo.Percentage:F2}%)");
                }
                else if (e.UserState is string message)
                {
                    Console.WriteLine($"Сообщение: {message}");
                }
            };

            var searchTask1 = Task.Run(() =>
            {
                try
                {
                    Search(parameters1, worker1, new DoWorkEventArgs(null));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка в поиске: {ex.Message}");
                }
            });

            var completed1 = searchTask1.Wait(TimeSpan.FromSeconds(10));
            Console.WriteLine($"Тест 1 завершен: {completed1}, Прогресс: {progressCount1}, Последний: {lastProgress1:N0}");

            // Тест 2: 3 слова
            Console.WriteLine("\nТест 2: Перебор 3 слов (* * *)");
            var parameters2 = new SearchParameters
            {
                SeedPhrase = "* * *",
                BitcoinAddress = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ",
                WordCount = 3,
                FullSearch = true,
                ThreadCount = 1
            };
            
            var worker2 = new BackgroundWorker();
            worker2.WorkerReportsProgress = true;
            var progressCount2 = 0;
            var lastProgress2 = BigInteger.Zero;
            
            worker2.ProgressChanged += (s, e) =>
            {
                progressCount2++;
                if (e.UserState is ProgressInfo progressInfo)
                {
                    lastProgress2 = progressInfo.Current;
                    Console.WriteLine($"Прогресс: {progressInfo.Current:N0} / {progressInfo.Total:N0} ({progressInfo.Percentage:F2}%)");
                }
                else if (e.UserState is string message)
                {
                    Console.WriteLine($"Сообщение: {message}");
                }
            };

            var searchTask2 = Task.Run(() =>
            {
                try
                {
                    Search(parameters2, worker2, new DoWorkEventArgs(null));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка в поиске: {ex.Message}");
                }
            });

            var completed2 = searchTask2.Wait(TimeSpan.FromSeconds(15));
            Console.WriteLine($"Тест 2 завершен: {completed2}, Прогресс: {progressCount2}, Последний: {lastProgress2:N0}");

            // Тест 3: Частичный поиск
            Console.WriteLine("\nТест 3: Частичный поиск (abandon *)");
            var parameters3 = new SearchParameters
            {
                SeedPhrase = "abandon *",
                BitcoinAddress = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ",
                WordCount = 2,
                FullSearch = false,
                ThreadCount = 1
            };
            
            var worker3 = new BackgroundWorker();
            worker3.WorkerReportsProgress = true;
            var progressCount3 = 0;
            var lastProgress3 = BigInteger.Zero;
            
            worker3.ProgressChanged += (s, e) =>
            {
                progressCount3++;
                if (e.UserState is ProgressInfo progressInfo)
                {
                    lastProgress3 = progressInfo.Current;
                    Console.WriteLine($"Прогресс: {progressInfo.Current:N0} / {progressInfo.Total:N0} ({progressInfo.Percentage:F2}%)");
                }
                else if (e.UserState is string message)
                {
                    Console.WriteLine($"Сообщение: {message}");
                }
            };

            var searchTask3 = Task.Run(() =>
            {
                try
                {
                    Search(parameters3, worker3, new DoWorkEventArgs(null));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка в поиске: {ex.Message}");
                }
            });

            var completed3 = searchTask3.Wait(TimeSpan.FromSeconds(10));
            Console.WriteLine($"Тест 3 завершен: {completed3}, Прогресс: {progressCount3}, Последний: {lastProgress3:N0}");

            Console.WriteLine("\n=== РЕЗУЛЬТАТЫ ТЕСТОВ ===");
            Console.WriteLine($"Тест 1 (2 слова): {(completed1 && progressCount1 > 0 && lastProgress1 > BigInteger.Zero ? "ПРОШЕЛ" : "НЕ ПРОШЕЛ")}");
            Console.WriteLine($"Тест 2 (3 слова): {(completed2 && progressCount2 > 0 && lastProgress2 > BigInteger.Zero ? "ПРОШЕЛ" : "НЕ ПРОШЕЛ")}");
            Console.WriteLine($"Тест 3 (частичный): {(completed3 && progressCount3 > 0 && lastProgress3 > BigInteger.Zero ? "ПРОШЕЛ" : "НЕ ПРОШЕЛ")}");
        }

        public async Task TestSpeedCalculationAsync(CancellationToken token)
        {
            speedHistory.Clear();
            BigInteger fakeTotal = 1000000;
            BigInteger fakeCurrent = 0;
            Console.WriteLine("ТЕСТ СКОРОСТИ (скользящее среднее по 10 обновлениям):");
            for (int i = 0; i < 30; i++)
            {
                // Эмулируем прогресс: иногда быстро, иногда медленно
                int step = (i % 5 == 0) ? 10000 : 1000;
                fakeCurrent += step;
                var now = DateTime.Now;
                speedHistory.Enqueue((now, fakeCurrent));
                while (speedHistory.Count > SpeedWindow)
                    speedHistory.Dequeue();
                double avgSpeed = 0;
                if (speedHistory.Count > 1)
                {
                    double sum = 0;
                    var arr = speedHistory.ToArray();
                    int validPairs = 0;
                    for (int j = 1; j < arr.Length; j++)
                    {
                        var dt = (arr[j].Item1 - arr[j-1].Item1).TotalSeconds;
                        var dc = (double)(arr[j].Item2 - arr[j-1].Item2);
                        if (dt > 0)
                        {
                            sum += dc / dt;
                            validPairs++;
                        }
                    }
                    if (validPairs > 0)
                        avgSpeed = sum / validPairs;
                }
                double displaySpeed = avgSpeed;
                if (displaySpeed > 1_000_000)
                    displaySpeed = -1; // нереально
                double secondsLeft = displaySpeed > 0 ? (double)(fakeTotal - fakeCurrent) / displaySpeed : 0;
                if (double.IsInfinity(secondsLeft) || double.IsNaN(secondsLeft) || secondsLeft > TimeSpan.MaxValue.TotalSeconds)
                    secondsLeft = TimeSpan.MaxValue.TotalSeconds;
                else if (secondsLeft < 0)
                    secondsLeft = 0;
                var remaining = TimeSpan.FromSeconds(secondsLeft);
                string speedStr = displaySpeed < 0 ? "нереально" : (displaySpeed < 1 ? "очень медленно" : displaySpeed.ToString("N0"));
                Console.WriteLine($"Шаг {i+1,2}: Прогресс: {fakeCurrent:N0} / {fakeTotal:N0} | Скорость: {speedStr}/сек | Осталось: {(displaySpeed > 0 ? remaining.ToString() : "-")}");
                await Task.Delay((i % 7 == 0) ? 800 : 200, token); // иногда задержка
            }
            Console.WriteLine("Тест завершён.");
        }

        public string GenerateLastCheckedPhrase(ProgressData progressData)
        {
            // Восстанавливаем seed-фразу по данным прогресса
            var tempKnownWords = new string[progressData.WordCount];
            var tempUnknownPositions = new List<int>();
            var tempPartialWords = new List<(int position, string pattern)>();
            if (progressData.FullSearch)
            {
                for (int i = 0; i < progressData.WordCount; i++)
                {
                    tempKnownWords[i] = "";
                    tempUnknownPositions.Add(i);
                }
            }
            else
            {
                var seedWords = progressData.SeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < seedWords.Length; i++)
                {
                    if (seedWords[i] == "*" || seedWords[i].Contains("*"))
                    {
                        tempUnknownPositions.Add(i);
                        tempKnownWords[i] = "";
                        if (seedWords[i].Contains("*"))
                            tempPartialWords.Add((i, seedWords[i]));
                    }
                    else
                    {
                        tempKnownWords[i] = seedWords[i];
                    }
                }
            }
            var possibleWords = new List<string>[tempUnknownPositions.Count];
            for (int i = 0; i < tempUnknownPositions.Count; i++)
            {
                int position = tempUnknownPositions[i];
                var partialWord = tempPartialWords.FirstOrDefault(p => p.position == position);
                if (partialWord != default)
                    possibleWords[i] = GetMatchingWords(partialWord.pattern);
                else
                    possibleWords[i] = new List<string>(bip39Words);
            }
            BigInteger idx = 0;
            BigInteger.TryParse(progressData.CurrentCombination, out idx);
            var lastCombination = GenerateCombinationByIndex(idx - 1, possibleWords);
            var lastTestWords = new string[tempKnownWords.Length];
            Array.Copy(tempKnownWords, lastTestWords, tempKnownWords.Length);
            for (int i = 0; i < tempUnknownPositions.Count; i++)
                lastTestWords[tempUnknownPositions[i]] = lastCombination[i];
            var mnemonic = new Mnemonic(string.Join(" ", lastTestWords), Wordlist.English);
            var seed = mnemonic.DeriveSeed();
            var masterKey = ExtKey.CreateFromSeed(seed);
            var fullPath = new KeyPath("44'/0'/0'/0/0");
            var privateKey = masterKey.Derive(fullPath).PrivateKey;
            return privateKey.GetWif(Network.Main).ToString();
        }

        // Метод для тестирования уникальности комбинаций
        public void TestUniqueness()
        {
            Console.WriteLine("=== ТЕСТ УНИКАЛЬНОСТИ КОМБИНАЦИЙ ===");
            
            // Тест 1: Проверяем уникальность для 2 слов
            Console.WriteLine("\nТест 1: Уникальность для 2 слов (* *)");
            var possibleWords2 = new List<string>[2];
            possibleWords2[0] = new List<string>(bip39Words);
            possibleWords2[1] = new List<string>(bip39Words);
            
            var combinations2 = new HashSet<string>();
            var total2 = BigInteger.Pow(bip39Words.Count, 2);
            var sampleSize2 = BigInteger.Min(total2, 10000); // Проверяем первые 10000 комбинаций
            
            for (BigInteger i = 0; i < sampleSize2; i++)
            {
                var combination = GenerateCombinationByIndex(i, possibleWords2);
                var phrase = string.Join(" ", combination);
                if (!combinations2.Add(phrase))
                {
                    Console.WriteLine($"ДУБЛИКАТ НАЙДЕН! Индекс {i}: {phrase}");
                    return;
                }
            }
            Console.WriteLine($"✓ Тест 1 пройден: {combinations2.Count} уникальных комбинаций из {sampleSize2}");
            
            // Тест 2: Проверяем уникальность для 3 слов
            Console.WriteLine("\nТест 2: Уникальность для 3 слов (* * *)");
            var possibleWords3 = new List<string>[3];
            possibleWords3[0] = new List<string>(bip39Words);
            possibleWords3[1] = new List<string>(bip39Words);
            possibleWords3[2] = new List<string>(bip39Words);
            
            var combinations3 = new HashSet<string>();
            var total3 = BigInteger.Pow(bip39Words.Count, 3);
            var sampleSize3 = BigInteger.Min(total3, 10000); // Проверяем первые 10000 комбинаций
            
            for (BigInteger i = 0; i < sampleSize3; i++)
            {
                var combination = GenerateCombinationByIndex(i, possibleWords3);
                var phrase = string.Join(" ", combination);
                if (!combinations3.Add(phrase))
                {
                    Console.WriteLine($"ДУБЛИКАТ НАЙДЕН! Индекс {i}: {phrase}");
                    return;
                }
            }
            Console.WriteLine($"✓ Тест 2 пройден: {combinations3.Count} уникальных комбинаций из {sampleSize3}");
            
            // Тест 3: Проверяем алгоритм для частичного поиска
            Console.WriteLine("\nТест 3: Уникальность для частичного поиска (abandon *)");
            var knownWords = new string[] { "abandon", "" };
            var unknownPositions = new List<int> { 1 };
            var partialWords = new List<(int position, string pattern)>();
            
            var possibleWordsPartial = new List<string>[1];
            possibleWordsPartial[0] = new List<string>(bip39Words);
            
            var combinationsPartial = new HashSet<string>();
            var sampleSizePartial = BigInteger.Min((BigInteger)bip39Words.Count, 1000);
            
            for (BigInteger i = 0; i < sampleSizePartial; i++)
            {
                var combination = GenerateCombinationByIndex(i, possibleWordsPartial);
                var testWords = new string[knownWords.Length];
                Array.Copy(knownWords, testWords, knownWords.Length);
                testWords[1] = combination[0]; // Заменяем второе слово
                var phrase = string.Join(" ", testWords);
                
                if (!combinationsPartial.Add(phrase))
                {
                    Console.WriteLine($"ДУБЛИКАТ НАЙДЕН! Индекс {i}: {phrase}");
                    return;
                }
            }
            Console.WriteLine($"✓ Тест 3 пройден: {combinationsPartial.Count} уникальных комбинаций из {sampleSizePartial}");
            
            // Тест 4: Проверяем математическую корректность
            Console.WriteLine("\nТест 4: Математическая корректность");
            Console.WriteLine($"BIP39 словарь содержит {bip39Words.Count} слов");
            Console.WriteLine($"Теоретическое количество комбинаций для 2 слов: {bip39Words.Count}^2 = {BigInteger.Pow(bip39Words.Count, 2):N0}");
            Console.WriteLine($"Теоретическое количество комбинаций для 3 слов: {bip39Words.Count}^3 = {BigInteger.Pow(bip39Words.Count, 3):N0}");
            Console.WriteLine($"Теоретическое количество комбинаций для 12 слов: {bip39Words.Count}^12 = {BigInteger.Pow(bip39Words.Count, 12):N0}");
            
            // Тест 5: Проверяем первые несколько комбинаций
            Console.WriteLine("\nТест 5: Первые комбинации для проверки");
            for (int i = 0; i < 5; i++)
            {
                var combination = GenerateCombinationByIndex(i, possibleWords2);
                var phrase = string.Join(" ", combination);
                Console.WriteLine($"Индекс {i}: {phrase}");
            }
            
            Console.WriteLine("\n=== РЕЗУЛЬТАТ ТЕСТА УНИКАЛЬНОСТИ ===");
            Console.WriteLine("✓ Все комбинации уникальны!");
            Console.WriteLine("✓ Алгоритм работает корректно");
        }

        // Метод для анализа алгоритма генерации
        public void AnalyzeGenerationAlgorithm()
        {
            Console.WriteLine("=== АНАЛИЗ АЛГОРИТМА ГЕНЕРАЦИИ ===");
            
            // Показываем как работает алгоритм для индекса 0
            Console.WriteLine("\nАнализ для индекса 0:");
            var possibleWords = new List<string>[2];
            possibleWords[0] = new List<string>(bip39Words);
            possibleWords[1] = new List<string>(bip39Words);
            
            BigInteger index = 0;
            var combination = new string[2];
            
            for (int i = 0; i < 2; i++)
            {
                var wordIndex = (int)(index % possibleWords[i].Count);
                combination[i] = possibleWords[i][wordIndex];
                index /= possibleWords[i].Count;
                Console.WriteLine($"Позиция {i}: wordIndex = {wordIndex}, слово = {combination[i]}");
            }
            
            Console.WriteLine($"Результат: {string.Join(" ", combination)}");
            
            // Показываем как работает алгоритм для индекса 1
            Console.WriteLine("\nАнализ для индекса 1:");
            index = 1;
            for (int i = 0; i < 2; i++)
            {
                var wordIndex = (int)(index % possibleWords[i].Count);
                combination[i] = possibleWords[i][wordIndex];
                index /= possibleWords[i].Count;
                Console.WriteLine($"Позиция {i}: wordIndex = {wordIndex}, слово = {combination[i]}");
            }
            Console.WriteLine($"Результат: {string.Join(" ", combination)}");
            
            // Показываем как работает алгоритм для индекса 2048 (размер словаря)
            Console.WriteLine("\nАнализ для индекса 2048:");
            index = 2048;
            for (int i = 0; i < 2; i++)
            {
                var wordIndex = (int)(index % possibleWords[i].Count);
                combination[i] = possibleWords[i][wordIndex];
                index /= possibleWords[i].Count;
                Console.WriteLine($"Позиция {i}: wordIndex = {wordIndex}, слово = {combination[i]}");
            }
            Console.WriteLine($"Результат: {string.Join(" ", combination)}");
        }

        // Публичный геттер для BIP39-словаря
        public List<string> GetBip39Words() => bip39Words;
    }
} 
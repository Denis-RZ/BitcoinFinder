using BitcoinFinderWebServer.Models;
using NBitcoin;
using System.Text;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System;
using System.Numerics;

namespace BitcoinFinderWebServer.Services
{
    public class SeedPhraseFinder
    {
        private readonly ILogger<SeedPhraseFinder> _logger;
        private readonly string[] _englishWords;
        private readonly ConcurrentQueue<string> _lastSeedPhrases = new ConcurrentQueue<string>();
        private readonly object _progressLock = new object();
        private DateTime _lastProgressSave = DateTime.MinValue;
        private const string ProgressFile = "progress_last.json";
        private long _lastSavedIndex = 0;
        private string _lastSavedPhrase = "";

        public SeedPhraseFinder(ILogger<SeedPhraseFinder> logger)
        {
            _logger = logger;
            _englishWords = Wordlist.English.GetWords().ToArray();
        }

        public async Task<long> CalculateTotalCombinationsAsync(SearchParameters parameters)
        {
            return await Task.Run(() =>
            {
                if (_englishWords.Length == 0)
                {
                    _logger.LogError("BIP39 словарь пуст! Невозможно вычислить комбинации.");
                    return 0L;
                }
                var knownWords = parameters.KnownWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var unknownPositions = parameters.WordCount - knownWords.Length;
                
                if (unknownPositions <= 0) return 1;
                
                var total = (long)Math.Pow(_englishWords.Length, unknownPositions);
                _logger.LogInformation($"Вычисление комбинаций: словарь={_englishWords.Length}, неизвестных позиций={unknownPositions}, всего={total}");
                return total;
            });
        }

        public async Task<bool> ValidateSeedPhraseAsync(string seedPhrase)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var words = seedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length != 12 && words.Length != 24) return false;

                    foreach (var word in words)
                    {
                        if (!_englishWords.Contains(word.ToLower()))
                            return false;
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<string> GenerateBitcoinAddressAsync(string seedPhrase)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var mnemonic = new Mnemonic(seedPhrase);
                    var masterKey = mnemonic.DeriveExtKey();
                    var account = masterKey.Derive(new KeyPath("m/44'/0'/0'"));
                    var change = account.Derive(new KeyPath("0/0"));
                    var address = change.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
                    
                    return address.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при генерации адреса для seed-фразы");
                    return "";
                }
            });
        }

        public async Task<SearchResult> SearchSeedPhraseAsync(SearchParameters parameters, IDatabaseService? dbService = null)
        {
            return await Task.Run(() =>
            {
                var result = new SearchResult
                {
                    Success = false,
                    ProcessedCombinations = 0,
                    ProcessingTime = TimeSpan.Zero
                };
                try
                {
                    var startTime = DateTime.UtcNow;
                    var wordCount = parameters.WordCount;
                    var batchSize = parameters.BatchSize > 0 ? parameters.BatchSize : 1000000;
                    var threads = parameters.Threads > 0 ? parameters.Threads : 1;
                    var maxCombinations = (long)Math.Pow(_englishWords.Length, wordCount);
                    var found = false;
                    var foundSeed = "";
                    var foundAddress = "";
                    var processed = 0L;
                    var locker = new object();
                    Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
                    {
                        for (long i = t; i < batchSize && !found; i += threads)
                        {
                            var temp = i;
                            var words = new string[wordCount];
                            for (int j = 0; j < wordCount; j++)
                            {
                                words[j] = _englishWords[temp % _englishWords.Length];
                                temp /= _englishWords.Length;
                            }
                            var seedPhrase = string.Join(" ", words);
                            // Добавляем фразу в очередь последних
                            lock (_progressLock) {
                                _lastSeedPhrases.Enqueue(seedPhrase);
                                while (_lastSeedPhrases.Count > 10) _lastSeedPhrases.TryDequeue(out _);
                            }
                            var address = GenerateBitcoinAddressAsync(seedPhrase).Result;
                            lock (locker) { processed++; }
                            if (address.Equals(parameters.TargetAddress, StringComparison.OrdinalIgnoreCase))
                            {
                                lock (locker)
                                {
                                    found = true;
                                    foundSeed = seedPhrase;
                                    foundAddress = address;
                                }
                                break;
                            }
                        }
                    });
                    result.ProcessedCombinations = processed;
                    result.ProcessingTime = DateTime.UtcNow - startTime;
                    if (found)
                    {
                        result.Success = true;
                        result.FoundSeedPhrase = foundSeed;
                        result.FoundAddress = foundAddress;
                        // Сохраняем результат в БД
                        dbService?.SaveFoundSeed(parameters.TargetAddress, foundSeed, foundAddress, result.ProcessedCombinations, result.ProcessingTime);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при поиске seed-фразы");
                }
                return result;
            });
        }

        private IEnumerable<string[]> GenerateCombinations(int positions, long startIndex, long endIndex)
        {
            var currentIndex = 0L;
            var maxCombinations = (long)Math.Pow(_englishWords.Length, positions);
            
            if (endIndex <= 0 || endIndex > maxCombinations)
                endIndex = maxCombinations;
            
            for (long i = startIndex; i < endIndex; i++)
            {
                var combination = new string[positions];
                var temp = i;
                
                for (int j = 0; j < positions; j++)
                {
                    combination[j] = _englishWords[temp % _englishWords.Length];
                    temp /= _englishWords.Length;
                }
                
                yield return combination;
            }
        }

        public List<string> GetLastSeedPhrases() {
            lock (_progressLock) {
                return _lastSeedPhrases.ToList();
            }
        }
        private void SaveProgress(long index, string phrase) {
            try {
                var obj = new {
                    Timestamp = DateTime.UtcNow,
                    CurrentIndex = index,
                    LastCheckedPhrase = phrase,
                    WordCount = 12
                };
                File.WriteAllText(ProgressFile, System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                _lastSavedIndex = index;
                _lastSavedPhrase = phrase;
            } catch {}
        }
        public (long, string) LoadProgress() {
            try {
                if (File.Exists(ProgressFile)) {
                    var json = File.ReadAllText(ProgressFile);
                    var doc = System.Text.Json.JsonDocument.Parse(json);
                    var idx = doc.RootElement.GetProperty("CurrentIndex").GetInt64();
                    var phrase = doc.RootElement.GetProperty("LastCheckedPhrase").GetString() ?? "";
                    return (idx, phrase);
                }
            } catch {}
            return (0, "");
        }

        // Возвращает список диапазонов (start, end), каждый <= long.MaxValue
        public List<(long start, long end)> GetSafeRanges(int wordCount, long blockSize)
        {
            var ranges = new List<(long, long)>();
            var total = BigInteger.Pow(_englishWords.Length, wordCount);
            BigInteger start = 0;
            while (start < total)
            {
                BigInteger end = BigInteger.Min(start + blockSize - 1, total - 1);
                ranges.Add(((long)start, (long)end));
                start = end + 1;
            }
            return ranges;
        }
        public async Task<BigInteger> CalculateTotalCombinationsBigAsync(SearchParameters parameters)
        {
            return await Task.Run(() =>
            {
                if (_englishWords.Length == 0) return BigInteger.Zero;
                var knownWords = parameters.KnownWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var unknownPositions = parameters.WordCount - knownWords.Length;
                if (unknownPositions <= 0) return BigInteger.One;
                return BigInteger.Pow(_englishWords.Length, unknownPositions);
            });
        }
        public string GetEnglishWordByIndex(int idx) {
            if (idx < 0 || idx >= _englishWords.Length) return "";
            return _englishWords[idx];
        }
        public void AddLastSeedPhrase(string phrase) {
            lock (_progressLock) {
                _lastSeedPhrases.Enqueue(phrase);
                while (_lastSeedPhrases.Count > 10) _lastSeedPhrases.TryDequeue(out _);
            }
        }
    }

    public class SearchResult
    {
        public bool Success { get; set; }
        public long ProcessedCombinations { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public string? FoundSeedPhrase { get; set; }
        public string? FoundAddress { get; set; }
    }

    public class BackgroundSeedTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SeedPhrase { get; set; } = "";
        public string? ExpectedAddress { get; set; }
        public int Threads { get; set; } = 1;
        public string Status { get; set; } = "Pending";
        public CancellationTokenSource? Cts { get; set; }
        public long CurrentIndex { get; set; } = 0; // Индекс последней обработанной комбинации
        public long TotalCombinations { get; set; } = 0;
    }

    public class BackgroundSeedTaskManager
    {
        private readonly ConcurrentDictionary<string, BackgroundSeedTask> _tasks = new();
        private int _defaultThreads = 1;
        private readonly string _progressFile = "background_seed_progress.json";

        public BackgroundSeedTaskManager()
        {
            LoadProgress();
        }

        public void AddTask(string seed, string? address = null, int? threads = null)
        {
            var id = Guid.NewGuid().ToString();
            _tasks[id] = new BackgroundSeedTask
            {
                Id = id,
                SeedPhrase = seed,
                ExpectedAddress = address,
                Threads = threads ?? _defaultThreads,
                Status = "Pending",
                CurrentIndex = 0
            };
            SaveProgress();
        }

        public void StartTask(string id)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                if (task.Status == "Running") return;
                task.Cts = new CancellationTokenSource();
                task.Status = "Running";
                SaveProgress();
                Task.Run(() => RunTask(task, task.Cts.Token));
            }
        }

        public void StopTask(string id)
        {
            if (_tasks.TryGetValue(id, out var task) && task.Cts != null)
            {
                task.Cts.Cancel();
                task.Status = "Stopped";
                SaveProgress();
            }
        }

        public void SetThreads(string id, int threads)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                task.Threads = threads;
                SaveProgress();
            }
        }

        public IEnumerable<BackgroundSeedTask> GetTasks() => _tasks.Values;

        private void RunTask(BackgroundSeedTask task, CancellationToken token)
        {
            try
            {
                // Получаем параметры для генерации комбинаций
                int wordCount = 12; // TODO: получать из task/параметров
                var englishWords = Wordlist.English.GetWords().ToArray();
                long totalCombinations = (long)Math.Pow(englishWords.Length, wordCount);
                task.TotalCombinations = totalCombinations;
                long startIndex = task.CurrentIndex;
                for (long i = startIndex; i < totalCombinations && !token.IsCancellationRequested; i++)
                {
                    // Генерация seed-фразы по индексу i (пример, реальную логику подставить)
                    var temp = i;
                    var words = new string[wordCount];
                    for (int j = 0; j < wordCount; j++)
                    {
                        words[j] = englishWords[temp % englishWords.Length];
                        temp /= englishWords.Length;
                    }
                    // ... здесь логика проверки адреса ...
                    task.CurrentIndex = i + 1;
                    if (i % 100 == 0) SaveProgress(); // Периодически сохраняем прогресс
                }
                task.Status = token.IsCancellationRequested ? "Stopped" : "Completed";
                SaveProgress();
            }
            catch
            {
                task.Status = "Error";
                SaveProgress();
            }
        }

        private void SaveProgress()
        {
            try
            {
                var json = JsonSerializer.Serialize(_tasks.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_progressFile, json);
            }
            catch { }
        }

        private void LoadProgress()
        {
            try
            {
                if (File.Exists(_progressFile))
                {
                    var json = File.ReadAllText(_progressFile);
                    var list = JsonSerializer.Deserialize<List<BackgroundSeedTask>>(json);
                    if (list != null)
                    {
                        _tasks.Clear();
                        foreach (var t in list)
                            _tasks[t.Id] = t;
                    }
                }
            }
            catch { }
        }
    }
} 
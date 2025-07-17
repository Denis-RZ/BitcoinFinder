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
    public class SeedPhraseLogEntry
    {
        public long Index { get; set; }
        public string Phrase { get; set; } = "";
        public string? Address { get; set; }
        public bool? IsValid { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? TaskId { get; set; }
        public string? Status { get; set; } // "checked", "valid", "invalid", "found"
    }

    public class SeedPhraseFinder
    {
        private readonly ILogger<SeedPhraseFinder> _logger;
        private readonly string[] _englishWords;
        private readonly ConcurrentQueue<SeedPhraseLogEntry> _lastSeedPhrases = new ConcurrentQueue<SeedPhraseLogEntry>();
        private readonly object _progressLock = new object();
        private DateTime _lastProgressSave = DateTime.MinValue;
        private const string ProgressFile = "progress_last.json";
        private long _lastSavedIndex = 0;
        private string _lastSavedPhrase = "";
        private readonly int _maxLogEntries = 1000; // Максимум записей в логе (увеличено для лайв журнала)

        public SeedPhraseFinder(ILogger<SeedPhraseFinder> logger)
        {
            _logger = logger;
            _englishWords = Wordlist.English.GetWords().ToArray();
        }

        // Правильный метод генерации seed-фразы по индексу
        public string GenerateSeedPhraseByIndex(long index, int wordCount = 12)
        {
            if (index < 0 || _englishWords.Length == 0) return "";
            
            var words = new string[wordCount];
            var temp = index;
            
            for (int i = 0; i < wordCount; i++)
            {
                words[i] = _englishWords[temp % _englishWords.Length];
                temp /= _englishWords.Length;
            }
            
            return string.Join(" ", words);
        }

        // Метод для получения следующей фразы по индексу
        public string GetNextSeedPhrase(long currentIndex, int wordCount = 12)
        {
            return GenerateSeedPhraseByIndex(currentIndex, wordCount);
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

        public async Task<SearchResult> SearchSeedPhraseAsync(SearchParameters parameters, IDatabaseService? dbService = null, string? taskId = null)
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
                            var seedPhrase = GenerateSeedPhraseByIndex(i, wordCount);
                            
                            // Добавляем фразу в лог с деталями
                            AddLastSeedPhrase(i, seedPhrase, null, null, "checked", taskId);
                            
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

        // Улучшенный метод добавления фразы в лог с ротацией
        public void AddLastSeedPhrase(long index, string phrase, string? address = null, bool? isValid = null, string? status = "checked", string? taskId = null)
        {
            if (string.IsNullOrWhiteSpace(phrase)) 
            {
                _logger.LogWarning($"[LIVE-LOG] Skipping empty phrase | Index: {index} | Status: {status}");
                return;
            }
            
            lock (_progressLock)
            {
                var entry = new SeedPhraseLogEntry
                {
                    Index = index,
                    Phrase = phrase,
                    Address = address,
                    IsValid = isValid,
                    Status = status,
                    TaskId = taskId,
                    Timestamp = DateTime.UtcNow
                };
                
                var beforeCount = _lastSeedPhrases.Count;
                _lastSeedPhrases.Enqueue(entry);
                var afterCount = _lastSeedPhrases.Count;
                
                // Ротация: удаляем старые записи, оставляем только последние N
                var removedCount = 0;
                while (_lastSeedPhrases.Count > _maxLogEntries)
                {
                    if (_lastSeedPhrases.TryDequeue(out _))
                    {
                        removedCount++;
                    }
                }
                
                _logger.LogInformation($"[LIVE-LOG] Added phrase to log | Index: {index} | Phrase: {phrase} | Status: {status} | Queue: {beforeCount}->{afterCount} | Removed: {removedCount} | Instance ID: {this.GetHashCode()}");
            }
        }

        // Обратная совместимость - старый метод
        public void AddLastSeedPhrase(string phrase)
        {
            AddLastSeedPhrase(0, phrase, null, null, "checked");
        }

        public async Task<SearchResult> SearchInRangeAsync(SearchParameters parameters, long startIndex, long endIndex, IDatabaseService? dbService = null, string? taskId = null)
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
                    var threads = parameters.Threads > 0 ? parameters.Threads : 1;
                    var found = false;
                    var foundSeed = "";
                    var foundAddress = "";
                    var processed = 0L;
                    var locker = new object();
                    
                    Parallel.For(startIndex, endIndex + 1, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
                    {
                        var seedPhrase = GenerateSeedPhraseByIndex(i, wordCount);
                        
                        // Добавляем фразу в лог с деталями
                        AddLastSeedPhrase(i, seedPhrase, null, null, "checked", taskId);
                        
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
                        }
                    });
                    
                    result.ProcessedCombinations = processed;
                    result.ProcessingTime = DateTime.UtcNow - startTime;
                    if (found)
                    {
                        result.Success = true;
                        result.FoundSeedPhrase = foundSeed;
                        result.FoundAddress = foundAddress;
                        dbService?.SaveFoundSeed(parameters.TargetAddress, foundSeed, foundAddress, result.ProcessedCombinations, result.ProcessingTime);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при поиске seed-фразы в диапазоне");
                }
                
                return result;
            });
        }

        public List<SeedPhraseLogEntry> GetLastSeedPhrases() {
            lock (_progressLock) {
                var phrases = _lastSeedPhrases.ToList();
                
                // Сортируем по времени (новые сначала)
                phrases = phrases.OrderByDescending(p => p.Timestamp).ToList();
                
                _logger.LogInformation($"[LIVE-LOG] GetLastSeedPhrases called | Queue count: {_lastSeedPhrases.Count} | Returning {phrases.Count} phrases | Instance ID: {this.GetHashCode()}");
                
                // Детальное логирование для отладки
                if (phrases.Count > 0)
                {
                    _logger.LogInformation($"[LIVE-LOG] First phrase in result: Index={phrases[0].Index}, Phrase={phrases[0].Phrase}, Status={phrases[0].Status}, Time={phrases[0].Timestamp}");
                    if (phrases.Count > 1)
                    {
                        _logger.LogInformation($"[LIVE-LOG] Last phrase in result: Index={phrases[phrases.Count-1].Index}, Phrase={phrases[phrases.Count-1].Phrase}, Status={phrases[phrases.Count-1].Status}, Time={phrases[phrases.Count-1].Timestamp}");
                    }
                }
                else
                {
                    _logger.LogWarning($"[LIVE-LOG] No phrases in queue! Queue count: {_lastSeedPhrases.Count}");
                }
                
                return phrases;
            }
        }

        public List<SeedPhraseLogEntry> GetLiveLog() {
            return _lastSeedPhrases.ToList();
        }

        public async Task<BatchResult> ProcessBatchAsync(string targetAddress, string knownWords, int wordCount, string language, long startIndex, long endIndex, CancellationToken cancellationToken, string? taskId = null)
        {
            return await Task.Run(() =>
            {
                var result = new BatchResult();
                
                for (long i = startIndex; i < endIndex && !cancellationToken.IsCancellationRequested; i++)
                {
                    var seedPhrase = GenerateSeedPhraseByIndex(i, wordCount);
                    
                    // Добавляем фразу в лог
                    AddLastSeedPhrase(i, seedPhrase, null, null, "checked", taskId);
                    
                    try
                    {
                        var address = GenerateBitcoinAddressAsync(seedPhrase).Result;
                        
                        if (address.Equals(targetAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            result.FoundSeedPhrase = seedPhrase;
                            result.FoundAddress = address;
                            AddLastSeedPhrase(i, seedPhrase, address, true, "found", taskId);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error generating address for phrase at index {i}");
                    }
                }
                
                return result;
            }, cancellationToken);
        }

        private void SaveProgress(long index, string phrase) {
            lock (_progressLock) {
                if (DateTime.UtcNow - _lastProgressSave > TimeSpan.FromMinutes(1)) {
                    try {
                        var progress = new { Index = index, Phrase = phrase, Timestamp = DateTime.UtcNow };
                        var json = JsonSerializer.Serialize(progress);
                        File.WriteAllText(ProgressFile, json);
                        _lastProgressSave = DateTime.UtcNow;
                        _lastSavedIndex = index;
                        _lastSavedPhrase = phrase;
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Ошибка при сохранении прогресса");
                    }
                }
            }
        }

        public (long, string) LoadProgress() {
            try {
                if (File.Exists(ProgressFile)) {
                    var json = File.ReadAllText(ProgressFile);
                    var progress = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    if (progress != null && progress.ContainsKey("Index") && progress.ContainsKey("Phrase")) {
                        var index = Convert.ToInt64(progress["Index"]);
                        var phrase = progress["Phrase"].ToString() ?? "";
                        return (index, phrase);
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Ошибка при загрузке прогресса");
            }
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
            var task = new BackgroundSeedTask
            {
                SeedPhrase = seed,
                ExpectedAddress = address,
                Threads = threads ?? _defaultThreads
            };
            _tasks.TryAdd(task.Id, task);
        }

        public void StartTask(string id)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                task.Status = "Running";
                task.Cts = new CancellationTokenSource();
                var token = task.Cts.Token;
                Task.Run(() => RunTask(task, token));
            }
        }

        public void StopTask(string id)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                task.Status = "Stopped";
                task.Cts?.Cancel();
            }
        }

        public void SetThreads(string id, int threads)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                task.Threads = threads;
            }
        }

        public IEnumerable<BackgroundSeedTask> GetTasks() => _tasks.Values;

        private void RunTask(BackgroundSeedTask task, CancellationToken token)
        {
            try
            {
                // Здесь должна быть логика поиска
                // Пока просто заглушка
                task.Status = "Completed";
            }
            catch (OperationCanceledException)
            {
                task.Status = "Cancelled";
            }
            catch (Exception)
            {
                task.Status = "Error";
            }
        }

        private void SaveProgress()
        {
            try
            {
                var tasks = _tasks.Values.Select(t => new { t.Id, t.Status, t.CurrentIndex }).ToList();
                var json = System.Text.Json.JsonSerializer.Serialize(tasks);
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
                    var tasks = System.Text.Json.JsonSerializer.Deserialize<List<dynamic>>(json);
                    // Восстановление состояния задач
                }
            }
            catch { }
        }
    }

    public class BatchResult
    {
        public string? FoundSeedPhrase { get; set; }
        public string? FoundAddress { get; set; }
    }
} 
using BitcoinFinderWebServer.Models;
using NBitcoin;
using System.Text;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

namespace BitcoinFinderWebServer.Services
{
    public class SeedPhraseFinder
    {
        private readonly ILogger<SeedPhraseFinder> _logger;
        private readonly string[] _englishWords;

        public SeedPhraseFinder(ILogger<SeedPhraseFinder> logger)
        {
            _logger = logger;
            _englishWords = Wordlist.English.GetWords().ToArray();
        }

        public async Task<long> CalculateTotalCombinationsAsync(SearchParameters parameters)
        {
            return await Task.Run(() =>
            {
                var knownWords = parameters.KnownWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var unknownPositions = parameters.WordCount - knownWords.Length;
                
                if (unknownPositions <= 0) return 1;
                
                return (long)Math.Pow(_englishWords.Length, unknownPositions);
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

        public async Task<SearchResult> SearchSeedPhraseAsync(SearchParameters parameters)
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
                    var knownWords = parameters.KnownWords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var unknownPositions = parameters.WordCount - knownWords.Length;

                    if (unknownPositions <= 0)
                    {
                        // Все слова известны, проверяем одну комбинацию
                        var seedPhrase = string.Join(" ", knownWords);
                        var address = GenerateBitcoinAddressAsync(seedPhrase).Result;
                        
                        result.ProcessedCombinations = 1;
                        result.ProcessingTime = DateTime.UtcNow - startTime;
                        
                        if (address.Equals(parameters.TargetAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Success = true;
                            result.FoundSeedPhrase = seedPhrase;
                            result.FoundAddress = address;
                        }
                        
                        return result;
                    }

                    // Генерируем комбинации для неизвестных позиций
                    var combinations = GenerateCombinations(unknownPositions, parameters.StartIndex, parameters.EndIndex);
                    
                    foreach (var combination in combinations)
                    {
                        result.ProcessedCombinations++;
                        
                        // Создаем seed-фразу, подставляя известные слова
                        var seedWords = new string[parameters.WordCount];
                        var knownIndex = 0;
                        var unknownIndex = 0;
                        
                        for (int i = 0; i < parameters.WordCount; i++)
                        {
                            // Здесь нужно логику для определения позиций известных слов
                            // Пока просто чередуем известные и неизвестные
                            if (knownIndex < knownWords.Length && i % 2 == 0)
                            {
                                seedWords[i] = knownWords[knownIndex++];
                            }
                            else
                            {
                                seedWords[i] = combination[unknownIndex++];
                            }
                        }
                        
                        var seedPhrase = string.Join(" ", seedWords);
                        var address = GenerateBitcoinAddressAsync(seedPhrase).Result;
                        
                        if (address.Equals(parameters.TargetAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Success = true;
                            result.FoundSeedPhrase = seedPhrase;
                            result.FoundAddress = address;
                            break;
                        }
                        
                        // Ограничиваем количество комбинаций для демонстрации
                        if (result.ProcessedCombinations >= parameters.BatchSize)
                            break;
                    }
                    
                    result.ProcessingTime = DateTime.UtcNow - startTime;
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
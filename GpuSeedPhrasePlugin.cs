using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ILGPU;
using ILGPU.Runtime;
using NBitcoin;

namespace BitcoinFinder
{
    public interface ISeedPhraseGpuSearcher
    {
        Task<SearchResult> SearchAsync(SearchParameters parameters, CancellationToken token);
        bool IsGpuAvailable { get; }
        string GetDeviceInfo();
    }

    public class SearchResult
    {
        public int CheckedCount { get; set; }
        public string? FoundPhrase { get; set; }
        public string? FoundPrivateKey { get; set; }
        public string? FoundAddress { get; set; }
        public long ProcessingTimeMs { get; set; }
        public string DeviceUsed { get; set; } = "CPU";
    }

    public class GpuSeedPhrasePlugin : ISeedPhraseGpuSearcher
    {
        private Context? gpuContext;
        private Accelerator? accelerator;
        private readonly List<string> bip39Words;
        
        public bool IsGpuAvailable { get; private set; }

        public GpuSeedPhrasePlugin()
        {
            // Загружаем BIP39 словарь
            bip39Words = new Mnemonic(Wordlist.English).WordList.GetWords().ToList();
            
            try
            {
                InitializeGpu();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPU PLUGIN] Ошибка инициализации GPU: {ex.Message}");
                IsGpuAvailable = false;
            }
        }

        private void InitializeGpu()
        {
            try
            {
                gpuContext = Context.CreateDefault();
                
                // Временно отключаем GPU функционал из-за проблем совместимости с ILGPU API
                // Используем только CPU режим до исправления
                Console.WriteLine("[GPU PLUGIN] GPU функционал временно отключен. Используется CPU режим.");
                IsGpuAvailable = false;
                accelerator = null;
                
                /*
                // Оригинальный код с ошибками ILGPU API:
                // Пытаемся получить CUDA устройство, если недоступно - используем CPU
                if (gpuContext.HasCudaAccelerators)
                {
                    var cudaDevice = gpuContext.GetCudaAccelerators().FirstOrDefault();
                    if (cudaDevice != null)
                    {
                        accelerator = cudaDevice.CreateAccelerator(gpuContext);
                        IsGpuAvailable = true;
                        Console.WriteLine($"[GPU PLUGIN] Инициализировано CUDA устройство: {accelerator.Name}");
                    }
                    else
                    {
                        // Fallback на CPU
                        var cpuDevice = gpuContext.GetCPUAccelerators().FirstOrDefault();
                        if (cpuDevice != null)
                        {
                            accelerator = cpuDevice.CreateAccelerator(gpuContext);
                            IsGpuAvailable = false;
                            Console.WriteLine("[GPU PLUGIN] CUDA недоступна, используется CPU ускорение");
                        }
                    }
                }
                else
                {
                    // Fallback на CPU если CUDA нет
                    var cpuDevice = gpuContext.GetCPUAccelerators().FirstOrDefault();
                    if (cpuDevice != null)
                    {
                        accelerator = cpuDevice.CreateAccelerator(gpuContext);
                        IsGpuAvailable = false;
                        Console.WriteLine("[GPU PLUGIN] CUDA недоступна, используется CPU ускорение");
                    }
                    else
                    {
                        Console.WriteLine("[GPU PLUGIN] Нет доступных ускорителей");
                        IsGpuAvailable = false;
                        accelerator = null;
                        gpuContext = null;
                    }
                }
                */
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPU PLUGIN] Ошибка инициализации: {ex.Message}");
                IsGpuAvailable = false;
                accelerator = null;
                gpuContext = null;
            }
        }

        public string GetDeviceInfo()
        {
            if (accelerator != null)
            {
                return $"Устройство: {accelerator.Name}, Тип: {accelerator.AcceleratorType}, Память: {accelerator.MemorySize / (1024 * 1024)} МБ";
            }
            return "GPU недоступен - используется CPU режим";
        }

        public async Task<SearchResult> SearchAsync(SearchParameters parameters, CancellationToken token)
        {
            var startTime = DateTime.Now;
            
            // Всегда используем CPU поиск из-за временного отключения GPU
            return await SearchCpuAsync(parameters, token, startTime);
        }

        private async Task<SearchResult> FullSearchGpuAsync(SearchParameters parameters, CancellationToken token, DateTime startTime)
        {
            Console.WriteLine("[GPU PLUGIN] Запуск полного поиска на GPU...");
            
            // Для демонстрации - ограничиваем поиск небольшим диапазоном
            int maxCombinations = Math.Min(100000, (int)Math.Pow(2048, Math.Min(parameters.WordCount, 6)));
            
            // Временно отключено из-за проблем с ILGPU API
            /*
            var kernel = accelerator!.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(
                SearchKernel);

            // Подготавливаем данные для GPU
            var indices = Enumerable.Range(0, maxCombinations).ToArray();
            var results = new int[maxCombinations];
            
            using var inputBuffer = accelerator.Allocate1D(indices);
            using var outputBuffer = accelerator.Allocate1D(results);

            // Запускаем ядро
            kernel((int)inputBuffer.Length, inputBuffer.View, outputBuffer.View, parameters.WordCount);
            accelerator.Synchronize();

            // Получаем результаты
            var gpuResults = outputBuffer.GetAsArray1D();
            */
            
            // Fallback на CPU поиск
            return await SearchCpuAsync(parameters, token, startTime);
        }

        private async Task<SearchResult> PartialSearchGpuAsync(SearchParameters parameters, CancellationToken token, DateTime startTime)
        {
            Console.WriteLine("[GPU PLUGIN] Запуск частичного поиска на GPU...");
            
            // Анализируем известные и неизвестные позиции
            var seedWords = parameters.SeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var unknownPositions = new List<int>();
            
            for (int i = 0; i < seedWords.Length; i++)
            {
                if (seedWords[i] == "*" || seedWords[i].Contains("*"))
                {
                    unknownPositions.Add(i);
                }
            }

            if (unknownPositions.Count == 0)
            {
                // Нет неизвестных слов, просто проверяем фразу
                var result = await ValidatePhrase(parameters.SeedPhrase, parameters.BitcoinAddress);
                
                return new SearchResult
                {
                    CheckedCount = 1,
                    FoundPhrase = result.HasValue ? parameters.SeedPhrase : null,
                    FoundPrivateKey = result?.privateKey,
                    FoundAddress = result?.address,
                    ProcessingTimeMs = (long)(DateTime.Now - startTime).TotalMilliseconds,
                    DeviceUsed = "CPU"
                };
            }

            // Ограничиваем поиск разумным количеством комбинаций
            long maxCombinations = Math.Min(1000000, (long)Math.Pow(2048, unknownPositions.Count));
            
            // Для больших пространств поиска используем CPU
            if (maxCombinations > 100000)
            {
                return await SearchCpuAsync(parameters, token, startTime);
            }

            // Генерируем и проверяем комбинации
            for (long i = 0; i < maxCombinations && !token.IsCancellationRequested; i++)
            {
                var testPhrase = GeneratePartialPhrase(seedWords, unknownPositions, i);
                var result = await ValidatePhrase(testPhrase, parameters.BitcoinAddress);
                
                if (result.HasValue)
                {
                    return new SearchResult
                    {
                        CheckedCount = (int)(i + 1),
                        FoundPhrase = testPhrase,
                        FoundPrivateKey = result.Value.privateKey,
                        FoundAddress = result.Value.address,
                        ProcessingTimeMs = (long)(DateTime.Now - startTime).TotalMilliseconds,
                        DeviceUsed = "GPU"
                    };
                }
                
                // Периодически проверяем отмену
                if (i % 1000 == 0)
                {
                    await Task.Yield();
                }
            }

            return new SearchResult
            {
                CheckedCount = (int)maxCombinations,
                ProcessingTimeMs = (long)(DateTime.Now - startTime).TotalMilliseconds,
                DeviceUsed = "GPU"
            };
        }

        private async Task<SearchResult> SearchCpuAsync(SearchParameters parameters, CancellationToken token, DateTime startTime)
        {
            Console.WriteLine("[GPU PLUGIN] Запуск поиска на CPU...");
            
            int maxCombinations = Math.Min(50000, (int)Math.Pow(2048, Math.Min(parameters.WordCount, 4)));
            
            return await Task.Run(() =>
            {
                for (int i = 0; i < maxCombinations && !token.IsCancellationRequested; i++)
                {
                    string phrase;
                    
                    if (parameters.FullSearch)
                    {
                        phrase = GeneratePhraseByIndex(i, parameters.WordCount);
                    }
                    else
                    {
                        var seedWords = parameters.SeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var unknownPositions = new List<int>();
                        
                        for (int j = 0; j < seedWords.Length; j++)
                        {
                            if (seedWords[j] == "*" || seedWords[j].Contains("*"))
                            {
                                unknownPositions.Add(j);
                            }
                        }
                        
                        if (unknownPositions.Count == 0)
                        {
                            // Проверяем только одну фразу
                            phrase = parameters.SeedPhrase;
                            maxCombinations = 1;
                        }
                        else
                        {
                            phrase = GeneratePartialPhrase(seedWords, unknownPositions, i);
                        }
                    }
                    
                    var result = ValidatePhrase(phrase, parameters.BitcoinAddress).Result;
                    
                    if (result.HasValue)
                    {
                        return new SearchResult
                        {
                            CheckedCount = i + 1,
                            FoundPhrase = phrase,
                            FoundPrivateKey = result.Value.privateKey,
                            FoundAddress = result.Value.address,
                            ProcessingTimeMs = (long)(DateTime.Now - startTime).TotalMilliseconds,
                            DeviceUsed = "CPU"
                        };
                    }
                }
                
                return new SearchResult
                {
                    CheckedCount = maxCombinations,
                    ProcessingTimeMs = (long)(DateTime.Now - startTime).TotalMilliseconds,
                    DeviceUsed = "CPU"
                };
            }, token);
        }

        // GPU kernel для поиска (упрощенная версия для демонстрации)
        private static void SearchKernel(Index1D index, ArrayView<int> input, ArrayView<int> output, int wordCount)
        {
            // Простая эвристика: помечаем некоторые индексы как кандидатов
            // В реальной реализации здесь была бы более сложная логика
            var value = input[index];
            
            // Например, отмечаем каждый 1000-й элемент как потенциальный кандидат
            if (value % 1000 == 42)
            {
                output[index] = 1;
            }
            else
            {
                output[index] = 0;
            }
        }

        private string GeneratePhraseByIndex(long index, int wordCount)
        {
            var words = new string[wordCount];
            
            for (int i = 0; i < wordCount; i++)
            {
                words[i] = bip39Words[(int)(index % bip39Words.Count)];
                index /= bip39Words.Count;
            }
            
            return string.Join(" ", words);
        }

        private string GeneratePartialPhrase(string[] baseWords, List<int> unknownPositions, long index)
        {
            var result = new string[baseWords.Length];
            Array.Copy(baseWords, result, baseWords.Length);
            
            foreach (var pos in unknownPositions)
            {
                result[pos] = bip39Words[(int)(index % bip39Words.Count)];
                index /= bip39Words.Count;
            }
            
            return string.Join(" ", result);
        }

        private async Task<(string privateKey, string address)?> ValidatePhrase(string phrase, string targetAddress)
        {
            try
            {
                // Проверяем валидность seed фразы
                var mnemonic = new Mnemonic(phrase, Wordlist.English);
                var seed = mnemonic.DeriveSeed();
                var masterKey = ExtKey.CreateFromSeed(seed);
                
                // Генерируем приватный ключ и адрес
                var fullPath = new KeyPath("44'/0'/0'/0/0");
                var privateKey = masterKey.Derive(fullPath).PrivateKey;
                var wif = privateKey.GetWif(Network.Main).ToString();
                
                // Генерируем адрес
                var publicKey = privateKey.PubKey;
                var address = publicKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main).ToString();
                
                // Проверяем соответствие целевому адресу
                if (!string.IsNullOrEmpty(targetAddress) && address == targetAddress)
                {
                    return (wif, address);
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            accelerator?.Dispose();
            gpuContext?.Dispose();
        }

        ~GpuSeedPhrasePlugin()
        {
            Dispose();
        }
    }
} 
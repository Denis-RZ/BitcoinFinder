#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;

namespace BitcoinFinderAndroidNew.Services
{
    public class BackgroundSearchService
    {
        private readonly ProgressManager progressManager;
        private readonly object lockObject = new object();
        
        private SearchProgress? currentProgress;
        private CancellationTokenSource? cancellationTokenSource;
        private bool isRunning = false;
        private DateTime startTime;
        private long totalProcessed = 0;
        private string targetAddress = "";
        private long lastSavedIndex = 0;

        public event Action<ProgressInfo>? ProgressReported;
        public event Action<string>? LogMessage;
        public event Action<FoundResult>? Found;
        public event Action<SearchProgress>? ProgressSaved;

        public BackgroundSearchService(ProgressManager progressManager)
        {
            this.progressManager = progressManager;
        }

        public async Task<bool> StartSearchAsync(PrivateKeyParameters parameters, string taskName, CancellationToken cancellationToken = default)
        {
            if (isRunning)
            {
                LogMessage?.Invoke("Поиск уже выполняется");
                return false;
            }

            if (string.IsNullOrWhiteSpace(parameters.TargetAddress))
            {
                LogMessage?.Invoke("Ошибка: Не указан целевой адрес");
                return false;
            }

            try
            {
                targetAddress = parameters.TargetAddress.Trim();
                
                // Создаем новый прогресс или загружаем существующий
                currentProgress = progressManager.LoadProgress();
                if (currentProgress == null || currentProgress.TargetAddress != targetAddress)
                {
                    currentProgress = new SearchProgress
                    {
                        TaskId = Guid.NewGuid().ToString(),
                        TaskName = taskName,
                        TargetAddress = targetAddress,
                        StartIndex = parameters.StartIndex,
                        EndIndex = parameters.EndIndex,
                        Format = parameters.Format,
                        Network = parameters.Network,
                        CurrentIndex = parameters.StartIndex, // Начинаем с указанного индекса
                        IsActive = true
                    };
                }

                cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                isRunning = true;
                startTime = DateTime.Now;
                totalProcessed = 0;
                lastSavedIndex = currentProgress.CurrentIndex;

                LogMessage?.Invoke($"Начинаем поиск: {targetAddress}");
                LogMessage?.Invoke($"Начальная позиция: {currentProgress.CurrentIndex:N0}");

                var tasks = new List<Task>();
                FoundResult? foundResult = null;
                var foundResultLock = new object();

                // Разделяем диапазон на потоки
                var keysPerThread = (currentProgress.EndIndex - currentProgress.CurrentIndex) / parameters.ThreadCount;
                
                for (int i = 0; i < parameters.ThreadCount; i++)
                {
                    var threadId = i;
                    var threadStart = currentProgress.CurrentIndex + (keysPerThread * i);
                    var threadEnd = i == parameters.ThreadCount - 1 
                        ? currentProgress.EndIndex 
                        : currentProgress.CurrentIndex + (keysPerThread * (i + 1));

                    tasks.Add(Task.Run(async () =>
                    {
                        var result = await SearchRangeAsync(threadStart, threadEnd, parameters.Format, parameters.Network, 
                            cancellationTokenSource.Token, threadId);
                        
                        if (result != null)
                        {
                            lock (foundResultLock)
                            {
                                if (foundResult == null)
                                {
                                    foundResult = result;
                                    // Останавливаем все остальные потоки
                                    cancellationTokenSource?.Cancel();
                                }
                            }
                        }
                    }, cancellationTokenSource.Token));
                }

                // Запускаем задачу сохранения прогресса
                var saveProgressTask = Task.Run(async () =>
                {
                    await SaveProgressPeriodicallyAsync(cancellationTokenSource.Token);
                }, cancellationTokenSource.Token);

                // Ждем завершения всех задач
                await Task.WhenAll(tasks);
                saveProgressTask.Dispose();

                // Сохраняем найденный результат
                if (foundResult != null)
                {
                    progressManager.SaveFoundResult(foundResult);
                    Found?.Invoke(foundResult);
                    
                    // Обновляем прогресс с найденным результатом
                    if (currentProgress != null)
                    {
                        currentProgress.FoundResult = new PrivateKeyResult
                        {
                            Found = true,
                            PrivateKey = foundResult.PrivateKey,
                            BitcoinAddress = foundResult.BitcoinAddress,
                            Balance = foundResult.Balance,
                            FoundAt = foundResult.FoundAt,
                            ProcessingTime = DateTime.Now - startTime,
                            ProcessedKeys = totalProcessed,
                            FoundAtIndex = foundResult.FoundAtIndex
                        };
                        progressManager.SaveProgress(currentProgress);
                    }
                }

                // Очищаем прогресс только если поиск завершен успешно
                if (foundResult != null)
                {
                    progressManager.ClearProgress();
                    LogMessage?.Invoke("🎉 ПРИВАТНЫЙ КЛЮЧ НАЙДЕН!");
                }
                else
                {
                    LogMessage?.Invoke("Поиск завершен - ключ не найден");
                }
                
                return foundResult != null;
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke("Поиск остановлен");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка поиска: {ex.Message}");
                return false;
            }
            finally
            {
                isRunning = false;
                cancellationTokenSource?.Dispose();
            }
        }

        public void StopSearch()
        {
            cancellationTokenSource?.Cancel();
            isRunning = false;
        }

        public bool IsRunning => isRunning;

        public SearchProgress? GetCurrentProgress()
        {
            return progressManager.LoadProgress();
        }

        public List<FoundResult> GetFoundResults()
        {
            return progressManager.LoadFoundResults();
        }

        private async Task<FoundResult?> SearchRangeAsync(long startIndex, long endIndex, KeyFormat format, NetworkType network,
            CancellationToken cancellationToken, int threadId)
        {
            var localCurrent = startIndex;

            while (localCurrent < endIndex && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var privateKey = BitcoinKeyGenerator.GeneratePrivateKey(localCurrent, format);
                    var bitcoinAddress = BitcoinKeyGenerator.GenerateBitcoinAddress(privateKey, network);

                    if (!string.IsNullOrEmpty(bitcoinAddress))
                    {
                        // Проверяем, совпадает ли адрес с целевым
                        if (bitcoinAddress.Equals(targetAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = new FoundResult
                            {
                                PrivateKey = privateKey,
                                Address = bitcoinAddress,
                                Balance = 0, // Баланс будет проверен отдельно
                                FoundAt = DateTime.Now,
                                FoundAtIndex = localCurrent,
                                ProcessingTime = DateTime.Now - startTime
                            };

                            LogMessage?.Invoke($"🎯 НАЙДЕН ПРИВАТНЫЙ КЛЮЧ!");
                            LogMessage?.Invoke($"🔑 Ключ: {privateKey}");
                            LogMessage?.Invoke($"📍 Адрес: {bitcoinAddress}");
                            LogMessage?.Invoke($"📊 Индекс: {localCurrent:N0}");
                            
                            return result;
                        }
                    }

                    localCurrent++;
                    Interlocked.Increment(ref totalProcessed);

                    // Обновляем прогресс
                    if (currentProgress != null)
                    {
                        currentProgress.CurrentIndex = localCurrent;
                    }

                    // Автосохранение каждые 1000 ключей
                    if (localCurrent - lastSavedIndex >= 1000)
                    {
                        lastSavedIndex = localCurrent;
                        if (currentProgress != null)
                        {
                            currentProgress.ElapsedTime = DateTime.Now - startTime;
                            currentProgress.LastSaved = DateTime.Now;
                            progressManager.SaveProgress(currentProgress);
                            ProgressSaved?.Invoke(currentProgress);
                        }
                    }

                    // Отправляем отчет о прогрессе каждые 100 ключей
                    if (localCurrent % 100 == 0)
                    {
                        ReportProgress(threadId, localCurrent.ToString(), bitcoinAddress);
                        await Task.Delay(1, cancellationToken); // Небольшая пауза для UI
                    }
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Ошибка в потоке {threadId}: {ex.Message}");
                    localCurrent++;
                }
            }

            return null;
        }

        private async Task SaveProgressPeriodicallyAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000, cancellationToken); // Сохраняем каждые 10 секунд как резерв
                    
                    if (currentProgress != null)
                    {
                        currentProgress.ElapsedTime = DateTime.Now - startTime;
                        currentProgress.LastSaved = DateTime.Now;
                        progressManager.SaveProgress(currentProgress);
                        ProgressSaved?.Invoke(currentProgress);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Ошибка сохранения прогресса: {ex.Message}");
                }
            }
        }

        private void ReportProgress(int threadId, string currentKey, string currentAddress)
        {
            var elapsed = DateTime.Now - startTime;
            var speed = totalProcessed / Math.Max(elapsed.TotalSeconds, 1);
            
            var progressInfo = new ProgressInfo
            {
                CurrentKey = currentKey,
                CurrentAddress = currentAddress,
                ProcessedKeys = totalProcessed,
                TotalKeys = currentProgress?.EndIndex - currentProgress?.StartIndex ?? 0,
                Progress = 0, // Не показываем процент для бесконечного поиска
                KeysPerSecond = (long)speed,
                ElapsedTime = elapsed,
                EstimatedTimeRemaining = TimeSpan.Zero, // Не показываем для бесконечного поиска
                Status = "Поиск выполняется",
                TargetAddress = targetAddress
            };

            ProgressReported?.Invoke(progressInfo);
        }

        private decimal CheckKnownAddresses(string address)
        {
            // Словарь известных адресов с балансом (для демонстрации)
            var knownAddresses = new Dictionary<string, decimal>
            {
                {"1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", 68.0m}, // Genesis block
                {"12c6DSiU4Rq3P4ZxziKxzrL5LmMBrzjrJX", 0.0m},  // Пример
                {"1HLoD9E4SDFFPDiYfNYnkBLQ85Y51J3Zb1", 0.0m},  // Пример
            };

            return knownAddresses.TryGetValue(address, out var balance) ? balance : 0;
        }
    }
} 
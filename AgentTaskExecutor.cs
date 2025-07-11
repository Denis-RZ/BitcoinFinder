using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Text;
using System.Numerics;

namespace BitcoinFinder
{
    public class AgentTaskExecutor
    {
        private readonly AdvancedSeedPhraseFinder finder;
        private readonly string progressFile;
        private readonly string configFile;
        private AgentTask? currentTask;
        private CancellationTokenSource? cancellationTokenSource;
        private Task? executionTask;
        private bool isExecuting = false;
        
        // События для обновления UI
        public event Action<string>? OnLog;
        public event Action<AgentProgress>? OnProgressUpdate;
        public event Action<AgentTaskResult>? OnTaskCompleted;
        public event Action<string>? OnError;
        
        public AgentTaskExecutor()
        {
            finder = new AdvancedSeedPhraseFinder();
            progressFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent_progress.json");
            configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent_config.json");
        }
        
        public bool IsExecuting => isExecuting;
        public AgentTask? CurrentTask => currentTask;
        
        /// <summary>
        /// Запуск выполнения задания
        /// </summary>
        public async Task StartTaskExecution(AgentTask task, CancellationToken cancellationToken)
        {
            if (isExecuting)
            {
                OnError?.Invoke("Задание уже выполняется");
                return;
            }
            
            try
            {
                currentTask = task;
                isExecuting = true;
                cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                // Сохраняем задание в конфиг
                await SaveTaskToConfig(task);
                
                OnLog?.Invoke($"🎯 Начало выполнения задания {task.BlockId}");
                OnLog?.Invoke($"📊 Диапазон: {task.StartIndex:N0} - {task.EndIndex:N0}");
                OnLog?.Invoke($"🎯 Целевой адрес: {task.TargetAddress}");
                OnLog?.Invoke($"📝 Количество слов: {task.WordCount}");
                
                executionTask = Task.Run(() => ExecuteTaskInternal(task, cancellationTokenSource.Token));
                await executionTask;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Ошибка запуска задания: {ex.Message}");
                isExecuting = false;
            }
        }
        
        /// <summary>
        /// Остановка выполнения задания
        /// </summary>
        public async Task StopTaskExecution()
        {
            if (!isExecuting) return;
            
            try
            {
                cancellationTokenSource?.Cancel();
                if (executionTask != null)
                {
                    await executionTask;
                }
            }
            catch (OperationCanceledException)
            {
                OnLog?.Invoke("⏹️ Выполнение задания остановлено");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Ошибка остановки задания: {ex.Message}");
            }
            finally
            {
                isExecuting = false;
                currentTask = null;
            }
        }
        
        /// <summary>
        /// Восстановление незавершенного задания при запуске
        /// </summary>
        public async Task<AgentTask?> RestoreUnfinishedTask()
        {
            try
            {
                if (File.Exists(configFile))
                {
                    var json = await File.ReadAllTextAsync(configFile);
                    var task = JsonSerializer.Deserialize<AgentTask>(json);
                    
                    if (task != null && !task.IsCompleted)
                    {
                        OnLog?.Invoke($"🔄 Найдено незавершенное задание {task.BlockId}");
                        OnLog?.Invoke($"📊 Прогресс: {task.CurrentIndex:N0} / {task.EndIndex:N0}");
                        return task;
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Ошибка восстановления задания: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Внутреннее выполнение задания
        /// </summary>
        private async Task ExecuteTaskInternal(AgentTask task, CancellationToken cancellationToken)
        {
            var progressTimer = new System.Threading.Timer(_ => SaveProgress(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            var lastProgressUpdate = DateTime.Now;
            
            try
            {
                var currentIndex = task.CurrentIndex;
                var totalCombinations = task.EndIndex - task.StartIndex;
                var processedCount = 0;
                
                OnLog?.Invoke($"🚀 Начинаем поиск с позиции {currentIndex:N0}");
                
                while (currentIndex < task.EndIndex && !cancellationToken.IsCancellationRequested)
                {
                    // Выполняем поиск в текущем диапазоне
                    var result = await finder.SearchInRangeAsync(
                        task.TargetAddress, 
                        task.WordCount, 
                        currentIndex, 
                        Math.Min(currentIndex + 1000, task.EndIndex), // Блоки по 1000
                        cancellationToken
                    );
                    
                    if (result.Found)
                    {
                        var taskResult = new AgentTaskResult
                        {
                            BlockId = task.BlockId,
                            Found = true,
                            SeedPhrase = result.SeedPhrase,
                            PrivateKey = result.PrivateKey,
                            BitcoinAddress = result.BitcoinAddress,
                            FoundAtIndex = currentIndex
                        };
                        
                        OnTaskCompleted?.Invoke(taskResult);
                        OnLog?.Invoke($"🎉 НАЙДЕНО! Seed: {result.SeedPhrase}");
                        break;
                    }
                    
                    currentIndex += 1000;
                    processedCount += 1000;
                    task.CurrentIndex = currentIndex;
                    
                    // Обновляем прогресс каждые 10 секунд
                    if (DateTime.Now - lastProgressUpdate > TimeSpan.FromSeconds(10))
                    {
                        var progress = new AgentProgress
                        {
                            BlockId = task.BlockId,
                            CurrentIndex = currentIndex,
                            ProcessedCount = processedCount,
                            TotalCount = totalCombinations,
                            ProgressPercent = (double)(currentIndex - task.StartIndex) / totalCombinations * 100,
                            Speed = processedCount / (DateTime.Now - lastProgressUpdate).TotalSeconds
                        };
                        
                        OnProgressUpdate?.Invoke(progress);
                        lastProgressUpdate = DateTime.Now;
                        processedCount = 0;
                    }
                }
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    // Задание завершено без находок
                    var taskResult = new AgentTaskResult
                    {
                        BlockId = task.BlockId,
                        Found = false,
                        CompletedAt = DateTime.Now
                    };
                    
                    OnTaskCompleted?.Invoke(taskResult);
                    OnLog?.Invoke($"✅ Задание {task.BlockId} завершено без находок");
                }
            }
            catch (OperationCanceledException)
            {
                OnLog?.Invoke("⏹️ Выполнение задания прервано");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Ошибка выполнения задания: {ex.Message}");
            }
            finally
            {
                progressTimer.Dispose();
                await SaveProgress();
            }
        }
        
        /// <summary>
        /// Сохранение прогресса в файл
        /// </summary>
        private async Task SaveProgress()
        {
            if (currentTask == null) return;
            
            try
            {
                var progressData = new AgentProgressData
                {
                    Task = currentTask,
                    LastUpdate = DateTime.Now,
                    IsExecuting = isExecuting
                };
                
                var json = JsonSerializer.Serialize(progressData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(progressFile, json);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Ошибка сохранения прогресса: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Сохранение задания в конфиг
        /// </summary>
        private async Task SaveTaskToConfig(AgentTask task)
        {
            try
            {
                var json = JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(configFile, json);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Ошибка сохранения задания: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Очистка файлов прогресса
        /// </summary>
        public void CleanupProgressFiles()
        {
            try
            {
                if (File.Exists(progressFile))
                    File.Delete(progressFile);
                if (File.Exists(configFile))
                    File.Delete(configFile);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Ошибка очистки файлов: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Задание для агента
    /// </summary>
    public class AgentTask
    {
        public int BlockId { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
        public long CurrentIndex { get; set; }
        public string TargetAddress { get; set; } = "";
        public int WordCount { get; set; }
        public DateTime AssignedAt { get; set; }
        public bool IsCompleted { get; set; }
    }
    
    /// <summary>
    /// Результат выполнения задания
    /// </summary>
    public class AgentTaskResult
    {
        public int BlockId { get; set; }
        public bool Found { get; set; }
        public string? SeedPhrase { get; set; }
        public string? PrivateKey { get; set; }
        public string? BitcoinAddress { get; set; }
        public long FoundAtIndex { get; set; }
        public DateTime CompletedAt { get; set; }
    }
    
    /// <summary>
    /// Прогресс выполнения задания
    /// </summary>
    public class AgentProgress
    {
        public int BlockId { get; set; }
        public long CurrentIndex { get; set; }
        public long ProcessedCount { get; set; }
        public long TotalCount { get; set; }
        public double ProgressPercent { get; set; }
        public double Speed { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
    
    /// <summary>
    /// Данные прогресса для сохранения в файл
    /// </summary>
    public class AgentProgressData
    {
        public AgentTask? Task { get; set; }
        public DateTime LastUpdate { get; set; }
        public bool IsExecuting { get; set; }
    }
} 
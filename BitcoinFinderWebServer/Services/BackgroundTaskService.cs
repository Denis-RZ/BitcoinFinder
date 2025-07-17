using BitcoinFinderWebServer.Models;
using System.Collections.Concurrent;

namespace BitcoinFinderWebServer.Services
{
    public class BackgroundTaskService : IHostedService, IBackgroundTaskService, IDisposable
    {
        private readonly TaskManager _taskManager;
        private readonly SeedPhraseFinder _seedPhraseFinder;
        private readonly ILogger<BackgroundTaskService> _logger;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();
        private readonly ConcurrentDictionary<string, Task> _taskWorkers = new();
        private Timer? _monitorTimer;

        public BackgroundTaskService(TaskManager taskManager, SeedPhraseFinder seedPhraseFinder, ILogger<BackgroundTaskService> logger)
        {
            _taskManager = taskManager;
            _seedPhraseFinder = seedPhraseFinder;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("BackgroundTaskService starting...");
            
            // Запускаем мониторинг каждые 5 секунд
            _monitorTimer = new Timer(MonitorTasks, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            
            // Восстанавливаем запущенные задачи
            await RestoreRunningTasks();
            
            _logger.LogInformation("BackgroundTaskService started");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("BackgroundTaskService stopping...");
            
            _monitorTimer?.Change(Timeout.Infinite, 0);
            
            // Останавливаем все запущенные задачи
            foreach (var taskId in _runningTasks.Keys.ToList())
            {
                await StopTaskAsync(taskId);
            }
            
            _logger.LogInformation("BackgroundTaskService stopped");
        }

        public async Task StartTaskAsync(string taskId)
        {
            try
            {
                var task = await _taskManager.GetTaskAsync(taskId);
                if (task == null)
                {
                    throw new ArgumentException($"Task {taskId} not found");
                }

                if (_runningTasks.ContainsKey(taskId))
                {
                    _logger.LogWarning($"Task {taskId} is already running");
                    return;
                }

                var cts = new CancellationTokenSource();
                _runningTasks.TryAdd(taskId, cts);

                var workerTask = Task.Run(async () => await RunTaskWorkerAsync(task, cts.Token), cts.Token);
                _taskWorkers.TryAdd(taskId, workerTask);

                task.Status = "Running";
                task.StartedAt = DateTime.UtcNow;
                await _taskManager.SaveTaskAsync(task);

                _logger.LogInformation($"Started background task: {taskId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting task {taskId}");
                throw;
            }
        }

        public async Task StopTaskAsync(string taskId)
        {
            try
            {
                if (_runningTasks.TryRemove(taskId, out var cts))
                {
                    cts.Cancel();
                    
                    if (_taskWorkers.TryRemove(taskId, out var workerTask))
                    {
                        try
                        {
                            await workerTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Ожидаемое исключение при отмене
                        }
                    }

                    var task = await _taskManager.GetTaskAsync(taskId);
                    if (task != null)
                    {
                        task.Status = "Stopped";
                        task.StoppedAt = DateTime.UtcNow;
                        await _taskManager.SaveTaskAsync(task);
                    }

                    _logger.LogInformation($"Stopped background task: {taskId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error stopping task {taskId}");
                throw;
            }
        }

        public async Task PauseTaskAsync(string taskId)
        {
            try
            {
                var task = await _taskManager.GetTaskAsync(taskId);
                if (task != null)
                {
                    task.Status = "Paused";
                    await _taskManager.SaveTaskAsync(task);
                    _logger.LogInformation($"Paused task: {taskId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error pausing task {taskId}");
                throw;
            }
        }

        public bool IsTaskRunning(string taskId)
        {
            return _runningTasks.ContainsKey(taskId);
        }

        public List<string> GetRunningTaskIds()
        {
            return _runningTasks.Keys.ToList();
        }

        private async Task RestoreRunningTasks()
        {
            try
            {
                var tasks = await _taskManager.GetAllTasksAsync();
                foreach (var task in tasks.Where(t => t.Status == "Running"))
                {
                    _logger.LogInformation($"Restoring running task: {task.Id}");
                    await StartTaskAsync(task.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring running tasks");
            }
        }

        private async Task RunTaskWorkerAsync(SearchTask task, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation($"Starting worker for task: {task.Id}");
                
                var parameters = task.SearchParameters;
                var totalCombinations = task.TotalCombinations;
                var processedCount = 0L;
                
                // Получаем блоки для обработки
                var blocks = task.Blocks.Where(b => b.Status == "Pending").ToList();
                
                foreach (var block in blocks)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        block.Status = "Processing";
                        block.StartedAt = DateTime.UtcNow;
                        await _taskManager.SaveTaskAsync(task);

                        var result = await ProcessBlockAsync(block, parameters, cancellationToken, task.Id);
                        
                        if (result.FoundSeedPhrase != null)
                        {
                            // Найдена фраза!
                            task.FoundSeedPhrase = result.FoundSeedPhrase;
                            task.FoundAddress = result.FoundAddress;
                            task.Status = "Completed";
                            task.CompletedAt = DateTime.UtcNow;
                            await _taskManager.SaveTaskAsync(task);
                            
                            _logger.LogInformation($"Task {task.Id} completed with result: {result.FoundSeedPhrase}");
                            return;
                        }

                        block.Status = "Completed";
                        block.CompletedAt = DateTime.UtcNow;
                        processedCount += block.EndIndex - block.StartIndex;
                        
                        // Обновляем прогресс
                        task.ProcessedCombinations = processedCount;
                        await _taskManager.SaveTaskAsync(task);
                        
                        _logger.LogDebug($"Completed block {block.BlockId} for task {task.Id}");
                    }
                    catch (OperationCanceledException)
                    {
                        block.Status = "Cancelled";
                        await _taskManager.SaveTaskAsync(task);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing block {block.BlockId} for task {task.Id}");
                        block.Status = "Error";
                        block.ErrorMessage = ex.Message;
                        await _taskManager.SaveTaskAsync(task);
                    }
                }

                // Все блоки обработаны
                if (!cancellationToken.IsCancellationRequested)
                {
                    task.Status = "Completed";
                    task.CompletedAt = DateTime.UtcNow;
                    await _taskManager.SaveTaskAsync(task);
                    _logger.LogInformation($"Task {task.Id} completed without finding result");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"Task {task.Id} was cancelled");
                var currentTask = await _taskManager.GetTaskAsync(task.Id);
                if (currentTask != null)
                {
                    currentTask.Status = "Stopped";
                    await _taskManager.SaveTaskAsync(currentTask);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in task worker for {task.Id}");
                var currentTask = await _taskManager.GetTaskAsync(task.Id);
                if (currentTask != null)
                {
                    currentTask.Status = "Error";
                    currentTask.ErrorMessage = ex.Message;
                    await _taskManager.SaveTaskAsync(currentTask);
                }
            }
            finally
            {
                _runningTasks.TryRemove(task.Id, out _);
                _taskWorkers.TryRemove(task.Id, out _);
            }
        }

        private async Task<BackgroundBlockResult> ProcessBlockAsync(SearchBlock block, SearchParameters parameters, CancellationToken cancellationToken, string taskId)
        {
            var result = new BackgroundBlockResult();
            var currentIndex = block.StartIndex;
            
            while (currentIndex < block.EndIndex && !cancellationToken.IsCancellationRequested)
            {
                var batchSize = Math.Min(parameters.BatchSize, (int)(block.EndIndex - currentIndex));
                var endIndex = Math.Min(currentIndex + batchSize, block.EndIndex);
                
                try
                {
                    var batchResult = await _seedPhraseFinder.ProcessBatchAsync(
                        parameters.TargetAddress,
                        parameters.KnownWords,
                        parameters.WordCount,
                        parameters.Language,
                        currentIndex,
                        endIndex,
                        cancellationToken,
                        taskId // Передаем taskId
                    );
                    
                    if (batchResult.FoundSeedPhrase != null)
                    {
                        result.FoundSeedPhrase = batchResult.FoundSeedPhrase;
                        result.FoundAddress = batchResult.FoundAddress;
                        return result;
                    }
                    
                    currentIndex = endIndex;
                    
                    // Обновляем прогресс блока
                    block.CurrentIndex = currentIndex;
                    await _taskManager.UpdateBlockProgressAsync(block.BlockId, currentIndex);
                    
                    // Небольшая задержка для снижения нагрузки
                    await Task.Delay(10, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing batch {currentIndex}-{endIndex} in block {block.BlockId}");
                    currentIndex = endIndex; // Продолжаем со следующего индекса
                }
            }
            
            return result;
        }

        private void MonitorTasks(object? state)
        {
            try
            {
                // Проверяем завершенные задачи
                var completedTasks = _taskWorkers
                    .Where(kvp => kvp.Value.IsCompleted)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var taskId in completedTasks)
                {
                    _runningTasks.TryRemove(taskId, out _);
                    _taskWorkers.TryRemove(taskId, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in task monitor");
            }
        }

        public void Dispose()
        {
            _monitorTimer?.Dispose();
            
            foreach (var cts in _runningTasks.Values)
            {
                cts.Dispose();
            }
        }
    }

    public class BackgroundBlockResult
    {
        public string? FoundSeedPhrase { get; set; }
        public string? FoundAddress { get; set; }
    }
} 
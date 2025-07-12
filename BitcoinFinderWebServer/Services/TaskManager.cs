using BitcoinFinderWebServer.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace BitcoinFinderWebServer.Services
{
    public class TaskManager
    {
        private readonly ConcurrentDictionary<string, SearchTask> _tasks = new();
        private readonly ConcurrentQueue<SearchTask> _pendingTasks = new();
        private readonly ConcurrentQueue<SearchBlock> _pendingBlocks = new();
        private readonly ConcurrentDictionary<int, SearchBlock> _assignedBlocks = new();
        private readonly SeedPhraseFinder _seedPhraseFinder;
        private readonly ILogger<TaskManager> _logger;
        
        // Статистика и мониторинг
        private readonly ConcurrentDictionary<string, AgentStats> _agentStats = new();
        private readonly List<string> _foundResults = new();
        private DateTime _searchStartTime;
        private long _totalProcessed = 0;
        private int _completedBlocks = 0;
        private bool _isRunning = false;
        
        // Собственный поиск сервера
        private bool _enableServerSearch = true;
        private int _serverThreads = 2;
        private long _serverProcessedCount = 0;
        private Task? _serverSearchTask;
        private CancellationTokenSource? _serverCts;

        public TaskManager(SeedPhraseFinder seedPhraseFinder, ILogger<TaskManager> logger)
        {
            _seedPhraseFinder = seedPhraseFinder;
            _logger = logger;
        }

        public async Task<SearchTask> CreateTaskAsync(string name, SearchParameters parameters)
        {
            var task = new SearchTask
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                Parameters = parameters,
                EnableServerSearch = _enableServerSearch,
                ServerThreads = _serverThreads
            };

            // Вычисляем общее количество комбинаций
            task.TotalCombinations = await _seedPhraseFinder.CalculateTotalCombinationsAsync(parameters);

            // Генерируем блоки заданий для совместимости с WinForms
            await GenerateSearchBlocksAsync(task, parameters);

            _tasks.TryAdd(task.Id, task);
            _pendingTasks.Enqueue(task);

            _logger.LogInformation($"Создана задача {task.Id}: {name} с {task.Blocks.Count} блоками");
            return task;
        }

        private async Task GenerateSearchBlocksAsync(SearchTask task, SearchParameters parameters)
        {
            var blockSize = parameters.BlockSize;
            var totalCombinations = task.TotalCombinations;
            var currentBlockId = 1;

            for (long startIndex = 0; startIndex < totalCombinations; startIndex += blockSize)
            {
                var endIndex = Math.Min(startIndex + blockSize - 1, totalCombinations - 1);
                
                var block = new SearchBlock
                {
                    BlockId = currentBlockId++,
                    StartIndex = startIndex,
                    EndIndex = endIndex,
                    WordCount = parameters.WordCount,
                    TargetAddress = parameters.TargetAddress,
                    Status = BlockStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Priority = CalculateBlockPriority(startIndex, endIndex)
                };

                task.Blocks.Add(block);
                _pendingBlocks.Enqueue(block);
            }

            _logger.LogInformation($"Сгенерировано {task.Blocks.Count} блоков для задачи {task.Id}");
        }

        private int CalculateBlockPriority(long startIndex, long endIndex)
        {
            // Приоритет: чем ближе к началу, тем выше приоритет
            var blockSize = endIndex - startIndex + 1;
            var priority = (int)(startIndex / blockSize);
            return Math.Max(1, priority);
        }

        public async Task<SearchTask?> GetNextTaskAsync(string agentName)
        {
            if (_pendingTasks.TryDequeue(out var task))
            {
                task.Status = "Running";
                task.StartedAt = DateTime.UtcNow;
                task.AssignedTo = agentName;

                _logger.LogInformation($"Задача {task.Id} назначена агенту {agentName}");
                return await System.Threading.Tasks.Task.FromResult(task);
            }

            return null;
        }

        public async Task<SearchBlock?> GetNextBlockAsync(string agentName)
        {
            if (_pendingBlocks.TryDequeue(out var block))
            {
                block.Status = BlockStatus.Assigned;
                block.AssignedTo = agentName;
                block.AssignedAt = DateTime.UtcNow;
                block.CurrentIndex = block.StartIndex;

                _assignedBlocks.TryAdd(block.BlockId, block);

                _logger.LogInformation($"Блок {block.BlockId} назначен агенту {agentName}");
                return await System.Threading.Tasks.Task.FromResult(block);
            }

            return null;
        }

        public async Task ProcessTaskResultAsync(string agentName, object result)
        {
            _logger.LogInformation($"Получен результат от агента {agentName}");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task ProcessBlockResultAsync(string agentName, BlockResult result)
        {
            if (_assignedBlocks.TryGetValue(result.BlockId, out var block))
            {
                block.Status = BlockStatus.Completed;
                block.CompletedAt = DateTime.UtcNow;
                block.CurrentIndex = result.ProcessedCombinations;

                _completedBlocks++;
                _totalProcessed += result.ProcessedCombinations;

                // Обновляем статистику агента
                await UpdateAgentStatsAsync(agentName, result.ProcessedCombinations, 1);

                // Если найдено решение
                if (!string.IsNullOrEmpty(result.FoundSeedPhrase))
                {
                    var foundResult = $"Найдено! Seed: {result.FoundSeedPhrase}, Address: {result.FoundAddress}, Index: {result.FoundIndex}";
                    _foundResults.Add(foundResult);
                    _logger.LogWarning($"НАЙДЕНО РЕШЕНИЕ: {foundResult}");
                }

                _logger.LogInformation($"Блок {result.BlockId} завершен агентом {agentName}");
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task UpdateBlockProgressAsync(int blockId, long currentIndex)
        {
            if (_assignedBlocks.TryGetValue(blockId, out var block))
            {
                block.CurrentIndex = currentIndex;
                block.LastProgressAt = DateTime.UtcNow;
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private async Task UpdateAgentStatsAsync(string agentName, long processed, int completedBlocks)
        {
            var stats = _agentStats.GetOrAdd(agentName, new AgentStats { AgentId = agentName });
            stats.ProcessedCount += processed;
            stats.CompletedBlocks += completedBlocks;
            stats.TotalProcessed += processed;
            stats.LastUpdate = DateTime.UtcNow;

            // Вычисляем скорость (простая реализация)
            var timeDiff = (DateTime.UtcNow - stats.LastUpdate).TotalSeconds;
            if (timeDiff > 0)
            {
                stats.CurrentRate = processed / timeDiff;
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task<List<SearchTask>> GetAllTasksAsync()
        {
            return await System.Threading.Tasks.Task.FromResult(_tasks.Values.ToList());
        }

        public async Task<SearchTask?> GetTaskAsync(string taskId)
        {
            _tasks.TryGetValue(taskId, out var task);
            return await System.Threading.Tasks.Task.FromResult(task);
        }

        public async Task UpdateTaskProgressAsync(string taskId, long processedCombinations)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.ProcessedCombinations = processedCombinations;
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task CompleteTaskAsync(string taskId, string? foundSeedPhrase = null, string? foundAddress = null)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Status = "Completed";
                task.CompletedAt = DateTime.UtcNow;
                task.FoundSeedPhrase = foundSeedPhrase;
                task.FoundAddress = foundAddress;

                _logger.LogInformation($"Задача {taskId} завершена");
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        // Методы для совместимости с WinForms
        public async Task StartServerSearchAsync()
        {
            if (_isRunning) return;

            _isRunning = true;
            _searchStartTime = DateTime.UtcNow;
            _serverCts = new CancellationTokenSource();

            if (_enableServerSearch)
            {
                _serverSearchTask = RunServerSearchAsync(_serverCts.Token);
                _logger.LogInformation($"Запущен собственный поиск сервера с {_serverThreads} потоками");
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task StopServerSearchAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _serverCts?.Cancel();
            _logger.LogInformation("Собственный поиск сервера остановлен");

            await System.Threading.Tasks.Task.CompletedTask;
        }

        private async Task RunServerSearchAsync(CancellationToken token)
        {
            var tasks = new List<Task>();
            
            for (int i = 0; i < _serverThreads; i++)
            {
                var threadId = i;
                tasks.Add(ServerSearchWorkerAsync(threadId, token));
            }

            await Task.WhenAll(tasks);
        }

        private async Task ServerSearchWorkerAsync(int threadId, CancellationToken token)
        {
            _logger.LogInformation($"Запущен поток поиска сервера {threadId}");

            while (!token.IsCancellationRequested)
            {
                var block = GetNextBlockForServer();
                if (block == null)
                {
                    await Task.Delay(1000, token);
                    continue;
                }

                // Здесь должна быть логика поиска
                // Пока просто помечаем блок как завершенный
                block.Status = BlockStatus.Completed;
                block.CompletedAt = DateTime.UtcNow;
                _completedBlocks++;

                await Task.Delay(100, token); // Имитация работы
            }
        }

        private SearchBlock? GetNextBlockForServer()
        {
            if (_pendingBlocks.TryDequeue(out var block))
            {
                block.Status = BlockStatus.Assigned;
                block.AssignedTo = "SERVER";
                block.AssignedAt = DateTime.UtcNow;
                return block;
            }
            return null;
        }

        public ServerStats GetServerStats()
        {
            return new ServerStats
            {
                ConnectedAgents = _agentStats.Count,
                PendingBlocks = _pendingBlocks.Count,
                AssignedBlocks = _assignedBlocks.Count,
                CompletedBlocks = _completedBlocks,
                TotalProcessed = _totalProcessed,
                TotalCombinations = _tasks.Values.Sum(t => t.TotalCombinations),
                FoundResults = _foundResults.Count,
                Uptime = _isRunning ? DateTime.UtcNow - _searchStartTime : TimeSpan.Zero,
                AgentStats = _agentStats.Values.ToList()
            };
        }

        public List<string> GetFoundResults()
        {
            return new List<string>(_foundResults);
        }
    }
} 
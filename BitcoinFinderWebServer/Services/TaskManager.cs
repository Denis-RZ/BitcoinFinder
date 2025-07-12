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

        // Система сохранения состояния
        private readonly ConcurrentDictionary<string, AgentState> _agentStates = new();
        private readonly string _stateFile = "server_state.json";
        private readonly string _agentStatesFile = "agent_states.json";
        private readonly Timer _autoSaveTimer;
        private readonly object _saveLock = new object();

        public TaskManager(SeedPhraseFinder seedPhraseFinder, ILogger<TaskManager> logger)
        {
            _seedPhraseFinder = seedPhraseFinder;
            _logger = logger;
            
            // Загружаем сохраненное состояние
            LoadServerState();
            LoadAgentStates();
            
            // Таймер автосохранения каждые 30 секунд
            _autoSaveTimer = new Timer(SaveStateCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private void SaveStateCallback(object? state)
        {
            try
            {
                SaveServerState();
                SaveAgentStates();
                _logger.LogDebug("Состояние сервера автоматически сохранено");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при автосохранении состояния");
            }
        }

        private void SaveServerState()
        {
            lock (_saveLock)
            {
                try
                {
                    var serverState = new ServerState
                    {
                        LastSaveTime = DateTime.UtcNow,
                        TotalProcessed = _totalProcessed,
                        CompletedBlocksCount = _completedBlocks,
                        TotalBlocks = _pendingBlocks.Count + _assignedBlocks.Count + _completedBlocks,
                        LastProcessedIndex = GetLastProcessedIndex(),
                        FoundResults = new List<string>(_foundResults),
                        AgentStates = _agentStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                        PendingBlocks = _pendingBlocks.ToList(),
                        AssignedBlocks = _assignedBlocks.Values.ToList(),
                        CompletedBlocks = GetCompletedBlocks()
                    };

                    var json = JsonSerializer.Serialize(serverState, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_stateFile, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка сохранения состояния сервера");
                }
            }
        }

        private void LoadServerState()
        {
            try
            {
                if (File.Exists(_stateFile))
                {
                    var json = File.ReadAllText(_stateFile);
                    var serverState = JsonSerializer.Deserialize<ServerState>(json);
                    
                    if (serverState != null)
                    {
                        _totalProcessed = serverState.TotalProcessed;
                        _completedBlocks = serverState.CompletedBlocksCount;
                        _foundResults.Clear();
                        _foundResults.AddRange(serverState.FoundResults);
                        
                        // Восстанавливаем агентов
                        foreach (var agentState in serverState.AgentStates)
                        {
                            _agentStates.TryAdd(agentState.Key, agentState.Value);
                        }
                        
                        _logger.LogInformation($"Загружено состояние сервера: {_totalProcessed:N0} обработано, {_completedBlocks} блоков завершено");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки состояния сервера");
            }
        }

        private void SaveAgentStates()
        {
            lock (_saveLock)
            {
                try
                {
                    var agentStates = _agentStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    var json = JsonSerializer.Serialize(agentStates, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_agentStatesFile, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка сохранения состояний агентов");
                }
            }
        }

        private void LoadAgentStates()
        {
            try
            {
                if (File.Exists(_agentStatesFile))
                {
                    var json = File.ReadAllText(_agentStatesFile);
                    var agentStates = JsonSerializer.Deserialize<Dictionary<string, AgentState>>(json);
                    
                    if (agentStates != null)
                    {
                        foreach (var agentState in agentStates)
                        {
                            _agentStates.TryAdd(agentState.Key, agentState.Value);
                        }
                        
                        _logger.LogInformation($"Загружены состояния {agentStates.Count} агентов");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки состояний агентов");
            }
        }

        private long GetLastProcessedIndex()
        {
            var lastIndex = 0L;
            foreach (var block in _assignedBlocks.Values)
            {
                if (block.CurrentIndex > lastIndex)
                    lastIndex = block.CurrentIndex;
            }
            return lastIndex;
        }

        private List<SearchBlock> GetCompletedBlocks()
        {
            // Возвращаем только последние 100 завершенных блоков для экономии памяти
            return _assignedBlocks.Values
                .Where(b => b.Status == BlockStatus.Completed)
                .OrderByDescending(b => b.CompletedAt)
                .Take(100)
                .ToList();
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
            // Проверяем, есть ли у агента незавершенный блок
            var agentState = GetOrCreateAgentState(agentName);
            if (agentState.LastBlockId.HasValue && agentState.LastIndex.HasValue)
            {
                // Агент переподключился, возвращаем его последний блок
                if (_assignedBlocks.TryGetValue(agentState.LastBlockId.Value, out var lastBlock))
                {
                    if (lastBlock.AssignedTo == agentName && lastBlock.Status == BlockStatus.Assigned)
                    {
                        _logger.LogInformation($"Агент {agentName} продолжает работу с блока {lastBlock.BlockId} с индекса {agentState.LastIndex.Value}");
                        return lastBlock;
                    }
                }
            }

            // Выдаем следующий блок по приоритету
            var nextBlock = GetNextBlockByPriority();
            if (nextBlock != null)
            {
                nextBlock.Status = BlockStatus.Assigned;
                nextBlock.AssignedTo = agentName;
                nextBlock.AssignedAt = DateTime.UtcNow;
                // Не сбрасываем прогресс, если этот блок уже частично обработан
                if (nextBlock.CurrentIndex < nextBlock.StartIndex)
                {
                    nextBlock.CurrentIndex = nextBlock.StartIndex;
                }

                _assignedBlocks.TryAdd(nextBlock.BlockId, nextBlock);

                // Обновляем состояние агента
                agentState.LastBlockId = nextBlock.BlockId;
                agentState.LastIndex = nextBlock.CurrentIndex;
                agentState.LastSeen = DateTime.UtcNow;
                agentState.IsConnected = true;

                _logger.LogInformation($"Блок {nextBlock.BlockId} назначен агенту {agentName}");
                return await System.Threading.Tasks.Task.FromResult(nextBlock);
            }

            return null;
        }

        private SearchBlock? GetNextBlockByPriority()
        {
            // Получаем все блоки из очереди и сортируем по приоритету
            var blocks = new List<SearchBlock>();
            while (_pendingBlocks.TryDequeue(out var block))
            {
                blocks.Add(block);
            }

            if (blocks.Count == 0) return null;

            // Сортируем по приоритету (меньший номер = выше приоритет)
            blocks.Sort((a, b) => a.BlockId.CompareTo(b.BlockId));

            // Возвращаем первый блок, остальные возвращаем в очередь
            var nextBlock = blocks[0];
            for (int i = 1; i < blocks.Count; i++)
            {
                _pendingBlocks.Enqueue(blocks[i]);
            }

            return nextBlock;
        }

        private AgentState GetOrCreateAgentState(string agentName)
        {
            return _agentStates.GetOrAdd(agentName, new AgentState
            {
                AgentId = Guid.NewGuid().ToString(),
                AgentName = agentName,
                LastSeen = DateTime.UtcNow,
                IsConnected = true,
                ConnectedAt = DateTime.UtcNow
            });
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

                // Обновляем состояние агента
                var agentState = GetOrCreateAgentState(agentName);
                agentState.CompletedBlocks++;
                agentState.TotalProcessed += result.ProcessedCombinations;
                agentState.LastSeen = DateTime.UtcNow;
                agentState.LastBlockId = null; // Блок завершен
                agentState.LastIndex = null;

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

                // Обновляем состояние агента
                var agentState = GetOrCreateAgentState(block.AssignedTo ?? "");
                agentState.LastIndex = currentIndex;
                agentState.LastSeen = DateTime.UtcNow;
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task AgentDisconnectedAsync(string agentName)
        {
            var agentState = GetOrCreateAgentState(agentName);
            agentState.IsConnected = false;
            agentState.DisconnectedAt = DateTime.UtcNow;
            
            _logger.LogInformation($"Агент {agentName} отключен. Последний блок: {agentState.LastBlockId}, индекс: {agentState.LastIndex}");

            // Возвращаем блок в очередь, если он был назначен агенту
            if (agentState.LastBlockId.HasValue)
            {
                await ReleaseBlockAsync(agentState.LastBlockId.Value, agentName, agentState.LastIndex ?? agentState.LastIndex.GetValueOrDefault(0));
            }
            
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task AgentReconnectedAsync(string agentName)
        {
            var agentState = GetOrCreateAgentState(agentName);
            agentState.IsConnected = true;
            agentState.ConnectedAt = DateTime.UtcNow;
            agentState.LastSeen = DateTime.UtcNow;
            
            _logger.LogInformation($"Агент {agentName} переподключен");
            
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
            
            // Сохраняем состояние перед остановкой
            SaveServerState();
            SaveAgentStates();
            
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
                ConnectedAgents = _agentStates.Values.Count(a => a.IsConnected),
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

        public List<AgentState> GetAgentStates()
        {
            return _agentStates.Values.ToList();
        }

        public void Dispose()
        {
            _autoSaveTimer?.Dispose();
            SaveServerState();
            SaveAgentStates();
        }

        // Методы для Keep-Alive API
        public string GetStatus()
        {
            var pendingTasks = _pendingTasks.Count;
            var completedTasks = _tasks.Values.Count(t => t.Status == "Completed");
            return $"Pending: {pendingTasks}, Completed: {completedTasks}";
        }

        public int GetPendingTasksCount()
        {
            return _pendingTasks.Count;
        }

        public int GetCompletedTasksCount()
        {
            return _tasks.Values.Count(t => t.Status == "Completed");
        }

        public bool IsHealthy()
        {
            return true; // Упрощенная проверка
        }

        public async Task ActivateAsync()
        {
            // Активация менеджера задач
            _isRunning = true;
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public double GetTasksPerSecond()
        {
            // Упрощенная реализация
            return 0.0;
        }

        /// <summary>
        /// Возврат блока обратно в очередь (например при отключении агента)
        /// </summary>
        public async Task ReleaseBlockAsync(int blockId, string agentName, long currentIndex = 0)
        {
            if (_assignedBlocks.TryRemove(blockId, out var block))
            {
                block.Status = BlockStatus.Pending;
                block.AssignedTo = null;
                block.AssignedAt = null;
                block.CurrentIndex = currentIndex > 0 ? currentIndex : block.CurrentIndex;
                _pendingBlocks.Enqueue(block);
                _logger.LogInformation($"Блок {blockId} возвращён в очередь сервером после отключения агента {agentName}");
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Изменение количества потоков собственного поиска сервера
        /// </summary>
        public async Task SetServerThreadsAsync(int threads)
        {
            if (threads <= 0) return;
            if (threads == _serverThreads) return;

            _logger.LogInformation($"Изменение количества потоков сервера: {_serverThreads} -> {threads}");
            _serverThreads = threads;

            if (_isRunning)
            {
                await StopServerSearchAsync();
                await StartServerSearchAsync();
            }
        }
    }
} 
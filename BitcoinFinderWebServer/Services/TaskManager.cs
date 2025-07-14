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
                _logger.LogDebug("Server state automatically saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during state auto-save");
            }
        }

        private void SaveServerState()
        {
            lock (_saveLock)
            {
                try
                {
                    // Ограничиваем количество сериализуемых блоков для экономии памяти
                    var pendingBlocks = _pendingBlocks.ToList();
                    var assignedBlocks = _assignedBlocks.Values.ToList();
                    var completedBlocks = GetCompletedBlocks();

                    // Сохраняем только последние 100 блоков из каждой коллекции
                    var pendingBlocksShort = pendingBlocks
                        .OrderByDescending(b => b.CreatedAt)
                        .Take(100)
                        .Select(b => new SearchBlock { BlockId = b.BlockId, CurrentIndex = b.CurrentIndex, StartIndex = b.StartIndex, EndIndex = b.EndIndex, Status = b.Status })
                        .ToList();
                    var assignedBlocksShort = assignedBlocks
                        .OrderByDescending(b => b.AssignedAt)
                        .Take(100)
                        .Select(b => new SearchBlock { BlockId = b.BlockId, CurrentIndex = b.CurrentIndex, StartIndex = b.StartIndex, EndIndex = b.EndIndex, Status = b.Status })
                        .ToList();
                    var completedBlocksShort = completedBlocks
                        .OrderByDescending(b => b.CompletedAt)
                        .Take(100)
                        .Select(b => new SearchBlock { BlockId = b.BlockId, CurrentIndex = b.CurrentIndex, StartIndex = b.StartIndex, EndIndex = b.EndIndex, Status = b.Status })
                        .ToList();

                    var serverState = new ServerState
                    {
                        LastSaveTime = DateTime.UtcNow,
                        TotalProcessed = _totalProcessed,
                        CompletedBlocksCount = _completedBlocks,
                        TotalBlocks = pendingBlocks.Count + assignedBlocks.Count + _completedBlocks,
                        LastProcessedIndex = GetLastProcessedIndex(),
                        FoundResults = new List<string>(_foundResults),
                        AgentStates = _agentStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                        PendingBlocks = pendingBlocksShort,
                        AssignedBlocks = assignedBlocksShort,
                        CompletedBlocks = completedBlocksShort
                    };

                    var json = JsonSerializer.Serialize(serverState, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_stateFile, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving server state");
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
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        _logger.LogWarning($"State file '{_stateFile}' is empty. Deleting and starting fresh.");
                        File.Delete(_stateFile);
                        return;
                    }
                    ServerState? serverState = null;
                    try {
                        serverState = JsonSerializer.Deserialize<ServerState>(json);
                    } catch (Exception ex) {
                        _logger.LogWarning($"State file '{_stateFile}' is invalid/corrupted: {ex.Message}. Deleting and starting fresh.");
                        File.Delete(_stateFile);
                        return;
                    }
                    if (serverState != null)
                    {
                        // Очищаем невалидные блоки из PendingBlocks, AssignedBlocks, CompletedBlocks
                        serverState.PendingBlocks = serverState.PendingBlocks?.Where(b => !string.IsNullOrWhiteSpace(b.TargetAddress) && b.WordCount > 0).ToList() ?? new List<SearchBlock>();
                        serverState.AssignedBlocks = serverState.AssignedBlocks?.Where(b => !string.IsNullOrWhiteSpace(b.TargetAddress) && b.WordCount > 0).ToList() ?? new List<SearchBlock>();
                        serverState.CompletedBlocks = serverState.CompletedBlocks?.Where(b => !string.IsNullOrWhiteSpace(b.TargetAddress) && b.WordCount > 0).ToList() ?? new List<SearchBlock>();
                        // Если после очистки нет ни одного блока — просто не создавать тестовую задачу, а ждать действий пользователя
                        if (serverState.PendingBlocks.Count == 0 && serverState.AssignedBlocks.Count == 0 && serverState.CompletedBlocks.Count == 0)
                        {
                            _logger.LogWarning("No valid blocks after loading state. Waiting for user to create a new task.");
                            return;
                        }
                        _totalProcessed = serverState.TotalProcessed;
                        _completedBlocks = serverState.CompletedBlocksCount;
                        _foundResults.Clear();
                        _foundResults.AddRange(serverState.FoundResults);
                        // Восстанавливаем агентов
                        foreach (var agentState in serverState.AgentStates)
                        {
                            _agentStates.TryAdd(agentState.Key, agentState.Value);
                        }
                        // --- Новый код: удаляем невалидные блоки и создаём тестовую задачу, если все блоки невалидны ---
                        bool allInvalid = true;
                        foreach (var b in serverState.PendingBlocks)
                        {
                            if (!string.IsNullOrWhiteSpace(b.TargetAddress) && b.WordCount > 0)
                            {
                                allInvalid = false;
                                break;
                            }
                        }
                        if (allInvalid)
                        {
                            _logger.LogWarning("All blocks are invalid after loading state. Creating a test task.");
                            // Очищаем очереди
                            while (_pendingBlocks.TryDequeue(out _)) { }
                            // Создаём тестовую задачу
                            var testParams = new SearchParameters {
                                TargetAddress = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ",
                                WordCount = 4, // Меньше слов для теста
                                KnownWords = "",
                                Language = "english",
                                StartIndex = 0,
                                EndIndex = 0,
                                BatchSize = 1000,
                                BlockSize = 1000,
                                Threads = 1
                            };
                            var task = new SearchTask {
                                Id = Guid.NewGuid().ToString(),
                                Name = "TestTask",
                                Status = "Pending",
                                CreatedAt = DateTime.UtcNow,
                                Parameters = testParams,
                                EnableServerSearch = true,
                                ServerThreads = 1
                            };
                            GenerateSearchBlocksAsync(task, testParams).GetAwaiter().GetResult();
                            _tasks.TryAdd(task.Id, task);
                            _pendingTasks.Enqueue(task);
                            _logger.LogInformation($"Created test task {task.Id} with {task.Blocks.Count} blocks");
                        }
                        // --- Конец нового кода ---
                        _logger.LogInformation($"Loaded server state: {_totalProcessed:N0} processed, {_completedBlocks} blocks completed");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading server state");
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
                    _logger.LogError(ex, "Error saving agent states");
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
                        
                        _logger.LogInformation($"Loaded {agentStates.Count} agent states");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading agent states");
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
            _logger.LogInformation($"CreateTaskAsync called with name='{name}'");
            _logger.LogInformation($"Parameters validation: TargetAddress='{parameters.TargetAddress}' (IsNullOrWhiteSpace: {string.IsNullOrWhiteSpace(parameters.TargetAddress)}), WordCount={parameters.WordCount} (<=0: {parameters.WordCount <= 0})");
            // Валидация параметров
            if (string.IsNullOrWhiteSpace(parameters.TargetAddress) || parameters.WordCount <= 0)
            {
                var errorMsg = $"Task creation error: TargetAddress is empty or WordCount <= 0 (TargetAddress='{parameters.TargetAddress}', WordCount={parameters.WordCount})";
                _logger.LogError(errorMsg);
                throw new ArgumentException(errorMsg);
            }
            _logger.LogInformation($"Parameters validation passed, creating task...");
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
            // Новый расчёт общего количества комбинаций через BigInteger
            System.Numerics.BigInteger totalCombinationsBig = 0;
            try
            {
                totalCombinationsBig = await _seedPhraseFinder.CalculateTotalCombinationsBigAsync(parameters);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Overflow or error in CalculateTotalCombinationsBigAsync: {ex.Message}. Останавливаю создание задачи.");
                throw;
            }
            if (totalCombinationsBig <= 0)
            {
                _logger.LogWarning($"Total combinations is too small or invalid ({totalCombinationsBig}), задача не будет создана.");
                throw new InvalidOperationException($"Invalid total combinations: {totalCombinationsBig}");
            }
            // Для совместимости: если слишком много, сохраняем long.MaxValue, но не блокируем задачу
            task.TotalCombinations = totalCombinationsBig > long.MaxValue ? long.MaxValue : (long)totalCombinationsBig;
            _logger.LogInformation($"Calculated total combinations (Big): {totalCombinationsBig:N0}");
            // Генерируем блоки заданий для совместимости с WinForms
            await GenerateSearchBlocksAsync(task, parameters);
            _tasks.TryAdd(task.Id, task);
            _pendingTasks.Enqueue(task);
            _logger.LogInformation($"Created task {task.Id}: {name} with {task.Blocks.Count} blocks");
            // После добавления задачи — всегда проверяем и запускаем серверный воркер
            if (!_isRunning || _serverSearchTask == null || _serverSearchTask.IsCompleted)
            {
                _logger.LogWarning("(Re)starting server search after task creation");
                await StartServerSearchAsync();
            }
            return task;
        }

        private async Task GenerateSearchBlocksAsync(SearchTask task, SearchParameters parameters)
        {
            var blockSize = parameters.BlockSize;
            var wordCount = parameters.WordCount;
            var ranges = new List<(long start, long end)>();
            const int MaxInitialBlocks = 10000;
            // Считаем BigInteger total местно, чтобы понять размеры
            var totalBig = await _seedPhraseFinder.CalculateTotalCombinationsBigAsync(parameters);
            if (totalBig <= long.MaxValue)
            {
                ranges = _seedPhraseFinder.GetSafeRanges(wordCount, blockSize);
            }
            else
            {
                // Ограничиваемся первыми MaxInitialBlocks диапазонами
                long currentStart = 0;
                for (int i = 0; i < MaxInitialBlocks; i++)
                {
                    long end = currentStart + blockSize - 1;
                    ranges.Add((currentStart, end));
                    currentStart = end + 1;
                }
                _logger.LogWarning($"Total combinations огромные. Сгенерировано только {MaxInitialBlocks} стартовых блоков. Будут добавляться по мере обработки.");
            }
            var currentBlockId = 1;
            foreach (var (startIndex, endIndex) in ranges)
            {
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
            _logger.LogInformation($"Generated {task.Blocks.Count} blocks for task {task.Id}");
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

                _logger.LogInformation($"Task {task.Id} assigned to agent {agentName}");
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
                        _logger.LogInformation($"Agent {agentName} continues with block {lastBlock.BlockId} from index {agentState.LastIndex.Value}");
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

                _logger.LogInformation($"Block {nextBlock.BlockId} assigned to agent {agentName}");
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
            _logger.LogInformation($"Received result from agent {agentName}");
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
                    var foundResult = $"Found! Seed: {result.FoundSeedPhrase}, Address: {result.FoundAddress}, Index: {result.FoundIndex}";
                    _foundResults.Add(foundResult);
                    _logger.LogWarning($"FOUND SOLUTION: {foundResult}");
                }

                _logger.LogInformation($"Block {result.BlockId} completed by agent {agentName}");
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

                // --- Новый код: обновляем ProcessedCombinations у задачи ---
                // Найти задачу, которой принадлежит этот блок
                var task = _tasks.Values.FirstOrDefault(t => t.Parameters.TargetAddress == block.TargetAddress);
                if (task != null)
                {
                    // Суммировать CurrentIndex по всем её блокам
                    task.ProcessedCombinations = task.Blocks.Sum(b => b.CurrentIndex - b.StartIndex + 1);
                }
                // --- Конец нового кода ---
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task AgentDisconnectedAsync(string agentName)
        {
            var agentState = GetOrCreateAgentState(agentName);
            agentState.IsConnected = false;
            agentState.DisconnectedAt = DateTime.UtcNow;
            
            _logger.LogInformation($"Agent {agentName} disconnected. Last block: {agentState.LastBlockId}, index: {agentState.LastIndex}");

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
            
            _logger.LogInformation($"Agent {agentName} reconnected");
            
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

                _logger.LogInformation($"Task {taskId} completed");
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
                _logger.LogInformation($"Started server search with {_serverThreads} threads");
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
            
            _logger.LogInformation("Stopped server search");

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
            _logger.LogInformation($"Started server search worker thread {threadId}");

            while (!token.IsCancellationRequested)
            {
                var block = GetNextBlockForServer();
                if (block == null)
                {
                    await Task.Delay(1000, token);
                    continue;
                }

                // Find parent task for this block
                SearchTask? parentTask = null;
                foreach (var t in _tasks.Values)
                {
                    if (t.Blocks.Any(b => b.BlockId == block.BlockId))
                    {
                        parentTask = t;
                        break;
                    }
                }

                long processed = 0;
                for (long i = block.StartIndex; i <= block.EndIndex && !token.IsCancellationRequested; i++)
                {
                    block.CurrentIndex = i;
                    processed++;
                    // Формируем seed-фразу и добавляем в очередь последних фраз
                    var temp = i;
                    var words = new string[block.WordCount];
                    for (int j = 0; j < block.WordCount; j++)
                    {
                        words[j] = _seedPhraseFinder.GetEnglishWordByIndex((int)(temp % 2048));
                        temp /= 2048;
                    }
                    var seedPhrase = string.Join(" ", words);
                    _seedPhraseFinder.AddLastSeedPhrase(seedPhrase);
                    if (parentTask != null)
                    {
                        parentTask.ProcessedCombinations++;
                    }
                    if (processed % 1000 == 0)
                    {
                        await UpdateBlockProgressAsync(block.BlockId, block.CurrentIndex);
                    }
                }
                block.Status = BlockStatus.Completed;
                block.CompletedAt = DateTime.UtcNow;
                _completedBlocks++;
                await UpdateBlockProgressAsync(block.BlockId, block.EndIndex);
            }
        }

        private SearchBlock? GetNextBlockForServer()
        {
            // Сначала пробуем взять из очереди
            while (_pendingBlocks.TryDequeue(out var block))
            {
                if (string.IsNullOrWhiteSpace(block.TargetAddress) || block.WordCount <= 0)
                {
                    _logger.LogWarning($"Block {block.BlockId} skipped: TargetAddress is empty or WordCount <= 0");
                    continue;
                }
                block.Status = BlockStatus.Assigned;
                block.AssignedTo = "SERVER";
                block.AssignedAt = DateTime.UtcNow;
                _assignedBlocks.TryAdd(block.BlockId, block);
                return block;
            }
            // Если очередь пуста — ищем незавершённый блок, назначенный серверу
            var assigned = _assignedBlocks.Values.FirstOrDefault(b => b.AssignedTo == "SERVER" && b.Status != BlockStatus.Completed);
            if (assigned != null)
                return assigned;
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
                _logger.LogInformation($"Block {blockId} returned to queue by server after agent {agentName} disconnected");
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

            _logger.LogInformation($"Changing server threads: {_serverThreads} -> {threads}");
            _serverThreads = threads;

            if (_isRunning)
            {
                await StopServerSearchAsync();
                await StartServerSearchAsync();
            }
        }
    }
} 
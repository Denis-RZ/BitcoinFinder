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
        private readonly TaskStorageService _taskStorage;
        private readonly ILogger<TaskManager> _logger;
        private readonly object _tasksLock = new();
        private readonly ConcurrentDictionary<string, AgentState> _agentStates = new();
        private readonly object _agentStatesLock = new();
        private ServerState _serverState = new();
        private readonly object _serverStateLock = new();
        private readonly string _serverStateFile = "server_state.json";
        private readonly string _agentStatesFile = "agent_states.json";
        private readonly Timer _autoSaveTimer;
        private readonly object _saveLock = new object();
        
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

        public TaskManager(SeedPhraseFinder seedPhraseFinder, TaskStorageService taskStorage, ILogger<TaskManager> logger)
        {
            _seedPhraseFinder = seedPhraseFinder;
            _taskStorage = taskStorage;
            _logger = logger;
            _logger.LogInformation($"[DIAG] TaskManager constructor called | SeedPhraseFinder instance ID: {_seedPhraseFinder.GetHashCode()}");
            
            // Загружаем сохраненное состояние
            LoadServerState();
            LoadAgentStates();
            
            // Загружаем задачи синхронно и с обработкой ошибок
            try {
                var load = Task.Run(async () => await LoadSavedTasksAsync());
                load.Wait();
            } catch (Exception ex) {
                _logger.LogError(ex, "[FIX] Ошибка загрузки задач из tasks_config.json, файл будет удалён и задачи не будут восстановлены");
                try { System.IO.File.Delete("tasks_config.json"); } catch { }
            }
            
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
                    File.WriteAllText(_serverStateFile, json);
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
                if (File.Exists(_serverStateFile))
                {
                    var json = File.ReadAllText(_serverStateFile);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        _logger.LogWarning($"State file '{_serverStateFile}' is empty. Deleting and starting fresh.");
                        File.Delete(_serverStateFile);
                        return;
                    }
                    ServerState? serverState = null;
                    try {
                        serverState = JsonSerializer.Deserialize<ServerState>(json);
                    } catch (Exception ex) {
                        _logger.LogWarning($"State file '{_serverStateFile}' is invalid/corrupted: {ex.Message}. Deleting and starting fresh.");
                        File.Delete(_serverStateFile);
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
                        // Проверяем валидность блоков и очищаем невалидные
                        var validPendingBlocks = serverState.PendingBlocks?.Where(b => !string.IsNullOrWhiteSpace(b.TargetAddress) && b.WordCount > 0).ToList() ?? new List<SearchBlock>();
                        var validAssignedBlocks = serverState.AssignedBlocks?.Where(b => !string.IsNullOrWhiteSpace(b.TargetAddress) && b.WordCount > 0).ToList() ?? new List<SearchBlock>();
                        var validCompletedBlocks = serverState.CompletedBlocks?.Where(b => !string.IsNullOrWhiteSpace(b.TargetAddress) && b.WordCount > 0).ToList() ?? new List<SearchBlock>();
                        
                        if (validPendingBlocks.Count == 0 && validAssignedBlocks.Count == 0 && validCompletedBlocks.Count == 0)
                        {
                            _logger.LogWarning("No valid blocks after loading state. Waiting for user to create a new task.");
                        }
                        else
                        {
                            // Восстанавливаем валидные блоки
                            foreach (var block in validPendingBlocks)
                            {
                                _pendingBlocks.Enqueue(block);
                            }
                            foreach (var block in validAssignedBlocks)
                            {
                                _assignedBlocks.TryAdd(block.BlockId, block);
                            }
                            _logger.LogInformation($"Restored {validPendingBlocks.Count} pending, {validAssignedBlocks.Count} assigned, {validCompletedBlocks.Count} completed blocks");
                        }
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

        private async Task LoadSavedTasksAsync()
        {
            try
            {
                var savedTasks = await _taskStorage.LoadTasksAsync();
                foreach (var task in savedTasks)
                {
                    _tasks.TryAdd(task.Id, task);
                    
                    // Восстанавливаем блоки в соответствующие очереди
                    if (task.Blocks != null)
                    {
                        foreach (var block in task.Blocks)
                        {
                            switch (block.Status.ToString().ToLower())
                            {
                                case "pending":
                                    _pendingBlocks.Enqueue(block);
                                    break;
                                case "assigned":
                                    _assignedBlocks.TryAdd(block.BlockId, block);
                                    break;
                                case "completed":
                                    _completedBlocks++;
                                    break;
                            }
                        }
                    }
                }
                _logger.LogInformation($"Loaded {savedTasks.Count} saved tasks");
                
                // BackgroundTaskService автоматически восстановит запущенные задачи
                // при своем старте через метод RestoreRunningTasks()
                
                // Если нет задач, создаем тестовую для демонстрации
                if (savedTasks.Count == 0)
                {
                    try
                    {
                        _logger.LogInformation("Creating demo task for testing...");
                        var demoParams = new SearchParameters
                        {
                            TargetAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", // Genesis block address
                            KnownWords = "",
                            WordCount = 12,
                            Language = "english",
                            StartIndex = 0,
                            EndIndex = 0,
                            BatchSize = 10000,
                            BlockSize = 1000,
                            Threads = 2
                        };
                        
                        var demoTask = await CreateTaskAsync("Demo Task", demoParams);
                        _logger.LogInformation($"Created demo task: {demoTask.Id}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not create demo task: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading saved tasks");
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
                .Where(b => b.Status == "Completed")
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
                SearchParameters = parameters,
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
            // Ограничиваем размер блока для больших задач
            if (totalCombinationsBig > 1000000000) // Если больше 1 миллиарда
            {
                parameters.BlockSize = Math.Min(parameters.BlockSize, 10000); // Ограничиваем размер блока
                _logger.LogInformation($"Large task detected, limiting block size to {parameters.BlockSize}");
            }
            // Генерируем блоки заданий для совместимости с WinForms
            await GenerateSearchBlocksAsync(task, parameters);
            _tasks.TryAdd(task.Id, task);
            _pendingTasks.Enqueue(task);
            
            // Сохраняем задачу в файл
            _logger.LogInformation($"About to save task {task.Id} to storage...");
            await _taskStorage.SaveTaskAsync(task);
            _logger.LogInformation($"Task {task.Id} saved to storage successfully");
            
            _logger.LogInformation($"Created task {task.Id}: {name} with {task.Blocks.Count} blocks");
            
            // BackgroundTaskService автоматически подхватит новую задачу через мониторинг
            
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
            try
            {
                _logger.LogInformation($"Generating search blocks for task {task.Id}");

                var totalCombinations = await _seedPhraseFinder.CalculateTotalCombinationsAsync(parameters);
                // Ограничиваем для больших задач
                if (totalCombinations > 1000000000)
                {
                    totalCombinations = 1000000000;
                    _logger.LogWarning($"Total combinations too large, limiting to {totalCombinations}");
                }
                task.TotalCombinations = totalCombinations;

                var blockSize = parameters.BlockSize;
                var blockCount = (int)Math.Ceiling((double)totalCombinations / blockSize);
                // Ограничиваем количество блоков
                if (blockCount > 1000)
                {
                    blockCount = 1000;
                    blockSize = (long)Math.Ceiling((double)totalCombinations / blockCount);
                    _logger.LogWarning($"Too many blocks, limiting to {blockCount} blocks of size {blockSize}");
                }
                var blocks = new List<SearchBlock>();

                for (int i = 0; i < blockCount; i++)
                {
                    var startIndex = i * blockSize;
                    var endIndex = Math.Min(startIndex + blockSize, totalCombinations);

                    var block = new SearchBlock
                    {
                        BlockId = i + 1,
                        StartIndex = startIndex,
                        EndIndex = endIndex,
                        WordCount = parameters.WordCount,
                        TargetAddress = parameters.TargetAddress,
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow,
                        Priority = CalculateBlockPriority(startIndex, endIndex)
                    };

                    blocks.Add(block);
                }

                task.Blocks = blocks;
                
                // Добавляем блоки в очередь для обработки
                foreach (var block in blocks)
                {
                    _pendingBlocks.Enqueue(block);
                }
                
                await SaveTaskAsync(task);

                _logger.LogInformation($"Generated {blocks.Count} blocks for task {task.Id} and added to pending queue");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error generating search blocks for task {task.Id}");
                throw;
            }
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
                    if (lastBlock.AssignedTo == agentName && lastBlock.Status == "Assigned")
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
                nextBlock.Status = "Assigned";
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
            try
            {
                _logger.LogInformation($"Processing block result from {agentName}: BlockId={result.BlockId}, Status={result.Status}");

                // Находим блок по ID
                if (_assignedBlocks.TryGetValue(result.BlockId, out var block))
                {
                    // Находим задачу по блоку
                    var task = _tasks.Values.FirstOrDefault(t => t.Blocks.Any(b => b.BlockId == result.BlockId));
                    if (task == null)
                    {
                        _logger.LogWarning($"No task found for block {result.BlockId} from {agentName}");
                        return;
                    }

                    // Обновляем статистику агента
                    await UpdateAgentStatsAsync(agentName, result.ProcessedCombinations, 1);

                    // Если найдена фраза, сохраняем результат
                    if (!string.IsNullOrEmpty(result.FoundSeedPhrase))
                    {
                        task.FoundSeedPhrase = result.FoundSeedPhrase;
                        task.FoundAddress = result.FoundAddress;
                        task.Status = "Completed";
                        task.CompletedAt = DateTime.UtcNow;
                        
                        _foundResults.Add($"{result.FoundSeedPhrase} -> {result.FoundAddress}");
                        _logger.LogInformation($"Found seed phrase: {result.FoundSeedPhrase} -> {result.FoundAddress}");
                    }

                    // Обновляем прогресс задачи
                    task.ProcessedCombinations += result.ProcessedCombinations;
                    await SaveTaskAsync(task);

                    _logger.LogDebug($"Updated task {task.Id} progress: {task.ProcessedCombinations}/{task.TotalCombinations}");
                }
                else
                {
                    _logger.LogWarning($"Block {result.BlockId} not found in assigned blocks");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing block result from {agentName}");
            }
        }

        public async Task UpdateBlockProgressAsync(int blockId, long currentIndex)
        {
            _logger.LogInformation($"[DIAG] UpdateBlockProgressAsync called for block {blockId}, currentIndex={currentIndex}");
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
                var task = _tasks.Values.FirstOrDefault(t => t.SearchParameters.TargetAddress == block.TargetAddress);
                if (task != null)
                {
                    // Суммировать CurrentIndex по всем её блокам
                    task.ProcessedCombinations = task.Blocks.Sum(b => b.CurrentIndex - b.StartIndex + 1);
                    _logger.LogInformation($"[DIAG] Task {task.Id} ProcessedCombinations updated: {task.ProcessedCombinations}");
                }
                // --- Конец нового кода ---
            }
            else
            {
                _logger.LogWarning($"[DIAG] UpdateBlockProgressAsync: block {blockId} not found in _assignedBlocks");
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

        public async Task SaveTaskAsync(SearchTask task)
        {
            await _taskStorage.SaveTaskAsync(task);
        }

        public async Task DeleteTaskAsync(string taskId)
        {
            if (_tasks.TryRemove(taskId, out var task))
            {
                await _taskStorage.DeleteTaskAsync(taskId);
                _logger.LogInformation($"Deleted task {taskId}");
            }
        }

        public async Task UpdateTaskProgressAsync(string taskId, long processedCombinations)
        {
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.ProcessedCombinations = processedCombinations;
                await _taskStorage.SaveTaskAsync(task);
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

                await _taskStorage.SaveTaskAsync(task);
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
            _logger.LogInformation($"[DIAG] Started server search worker thread {threadId}");

            while (!token.IsCancellationRequested)
            {
                var block = GetNextBlockForServer();
                if (block == null)
                {
                    _logger.LogInformation($"[DIAG] No block available for thread {threadId}, sleeping...");
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
                _logger.LogInformation($"[DIAG] Thread {threadId} starts processing block {block.BlockId} ({block.StartIndex}-{block.EndIndex})");
                try
                {
                    for (long i = block.StartIndex; i <= block.EndIndex && !token.IsCancellationRequested; i++)
                    {
                        block.CurrentIndex = i;
                        processed++;

                        // Используем правильный метод генерации seed-фразы
                        var seedPhrase = _seedPhraseFinder.GenerateSeedPhraseByIndex(i, block.WordCount);
                        
                        // Логируем операцию в лайв журнал
                        _seedPhraseFinder.AddLastSeedPhrase(i, seedPhrase, null, null, "processing", parentTask?.Id);
                        
                        _logger.LogDebug($"[LIVE-LOG] Thread {threadId} processing index {i}: {seedPhrase}");

                        // Генерируем Bitcoin адрес для проверки
                        var address = await _seedPhraseFinder.GenerateBitcoinAddressAsync(seedPhrase);
                        
                        // Проверяем, найден ли целевой адрес
                        if (!string.IsNullOrEmpty(address) && address.Equals(block.TargetAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning($"FOUND SOLUTION in block {block.BlockId}! Seed: {seedPhrase}, Address: {address}");
                            _foundResults.Add($"Found! Seed: {seedPhrase}, Address: {address}, Index: {i}");

                            // Добавляем найденную фразу в лог с деталями
                            _seedPhraseFinder.AddLastSeedPhrase(i, seedPhrase, address, true, "found", parentTask?.Id);

                            // Обновляем родительскую задачу
                            if (parentTask != null)
                            {
                                parentTask.Status = "Completed";
                                parentTask.FoundSeedPhrase = seedPhrase;
                                parentTask.FoundAddress = address;
                            }

                            block.Status = "Completed";
                            block.CompletedAt = DateTime.UtcNow;
                            _completedBlocks++;
                            await UpdateBlockProgressAsync(block.BlockId, block.CurrentIndex);
                            return; // Выходим из цикла, так как решение найдено
                        }
                        else
                        {
                            // Логируем проверенную фразу (не совпала с целевым адресом)
                            _seedPhraseFinder.AddLastSeedPhrase(i, seedPhrase, address, false, "checked", parentTask?.Id);
                        }

                        if (parentTask != null)
                        {
                            parentTask.ProcessedCombinations++;
                        }

                        // Обновляем прогресс каждые 1000 операций
                        if (processed % 1000 == 0)
                        {
                            _logger.LogInformation($"[DIAG] Thread {threadId} block {block.BlockId}: processed {processed} (CurrentIndex={block.CurrentIndex})");
                            await UpdateBlockProgressAsync(block.BlockId, block.CurrentIndex);
                        }
                    }
                    
                    block.Status = "Completed";
                    block.CompletedAt = DateTime.UtcNow;
                    _completedBlocks++;
                    _logger.LogInformation($"[DIAG] Thread {threadId} finished block {block.BlockId}, total processed: {processed}");
                    await UpdateBlockProgressAsync(block.BlockId, block.EndIndex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[DIAG] Exception in server worker thread {threadId} while processing block {block?.BlockId}");
                }
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
                block.Status = "Assigned";
                block.AssignedTo = "SERVER";
                block.AssignedAt = DateTime.UtcNow;
                _assignedBlocks.TryAdd(block.BlockId, block);
                return block;
            }
            // Если очередь пуста — ищем незавершённый блок, назначенный серверу
            var assigned = _assignedBlocks.Values.FirstOrDefault(b => b.AssignedTo == "SERVER" && b.Status != "Completed");
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
                block.Status = "Pending";
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

        public long GetProcessedCombinations(SearchTask task)
        {
            long sum = 0;
            foreach (var block in task.Blocks)
            {
                if (block.Status == "Completed")
                    sum += block.EndIndex - block.StartIndex + 1;
                else if (block.Status == "Assigned")
                    sum += block.CurrentIndex - block.StartIndex;
            }
            return sum;
        }
    }
} 
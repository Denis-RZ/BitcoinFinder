using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using NBitcoin; // Для работы с Bitcoin

namespace BitcoinFinder
{
    public class DistributedServer
    {
        private readonly int port;
        private readonly ConcurrentDictionary<string, AgentConnection> connectedAgents;
        private readonly ConcurrentQueue<SearchBlock> pendingBlocks;
        private readonly ConcurrentDictionary<int, SearchBlock> assignedBlocks;
        private readonly List<string> foundResults;
        private TcpListener? tcpListener;
        private CancellationTokenSource? serverCts;
        private bool isRunning = false;
        private int currentBlockId = 1;
        private readonly object lockObject = new object();
        
        // Параметры поиска
        private string targetAddress = "";
        private int wordCount = 12;
        private long totalCombinations = 0;
        private long blockSize = 100000;
        // PUBLIC: Позволяет форме сервера настроить размер блока перед запуском
        public long BlockSize
        {
            get => blockSize;
            set
            {
                if (value > 0)
                {
                    blockSize = value;
                }
            }
        }
        private DateTime searchStartTime;
        
        // Статистика
        private long totalProcessed = 0;
        private int completedBlocks = 0;
        private readonly ConcurrentDictionary<string, AgentStats> agentStats;
        
        // Собственный поиск сервера
        private bool enableServerSearch = true;
        private int serverThreads = 2; // Количество потоков для поиска на сервере
        private long serverProcessedCount = 0;
        private Task? serverSearchTask;
        private AdvancedSeedPhraseFinder? serverFinder;
        
        public event Action<string>? OnLog;
        public event Action<string>? OnFoundResult;
        public event Action<ServerStats>? OnStatsUpdate;

        public DistributedServer(int port = 5000)
        {
            this.port = port;
            connectedAgents = new ConcurrentDictionary<string, AgentConnection>();
            pendingBlocks = new ConcurrentQueue<SearchBlock>();
            assignedBlocks = new ConcurrentDictionary<int, SearchBlock>();
            foundResults = new List<string>();
            agentStats = new ConcurrentDictionary<string, AgentStats>();
        }

        public async Task StartAsync(string targetBitcoinAddress, int wordCount, long? totalCombinations = null, bool enableServerSearch = true, int serverThreads = 2)
        {
            if (isRunning) return;
            
            this.targetAddress = targetBitcoinAddress;
            this.wordCount = wordCount;
            this.searchStartTime = DateTime.Now;
            this.enableServerSearch = enableServerSearch;
            this.serverThreads = Math.Max(1, Math.Min(serverThreads, Environment.ProcessorCount));
            
            // Инициализируем поисковый движок сервера
            if (this.enableServerSearch)
            {
                serverFinder = new AdvancedSeedPhraseFinder();
            }
            
            // Вычисляем общее количество комбинаций если не задано
            if (totalCombinations.HasValue)
            {
                this.totalCombinations = totalCombinations.Value;
            }
            else
            {
                var finder = new AdvancedSeedPhraseFinder();
                var bip39Words = finder.GetBip39Words();
                this.totalCombinations = (long)Math.Pow(bip39Words.Count, wordCount);
                if (this.totalCombinations <= 0) // Overflow protection
                    this.totalCombinations = long.MaxValue;
            }
            
            Log($"[SERVER] Запуск сервера на порту {port}");
            Log($"[SERVER] Целевой адрес: {targetAddress}");
            Log($"[SERVER] Количество слов: {wordCount}");
            Log($"[SERVER] Всего комбинаций: {this.totalCombinations:N0}");
            Log($"[SERVER] Размер блока: {blockSize:N0}");
            Log($"[SERVER] Собственный поиск сервера: {(this.enableServerSearch ? $"ВКЛ ({this.serverThreads} потоков)" : "ВЫКЛ")}");
            
            // Генерируем блоки заданий
            GenerateSearchBlocks();
            
            serverCts = new CancellationTokenSource();
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            isRunning = true;
            
            Log($"[SERVER] Сервер запущен. Ожидание подключений...");
            
            // Запускаем задачи сервера
            var acceptTask = AcceptClientsAsync(serverCts.Token);
            var monitorTask = MonitorAgentsAsync(serverCts.Token);
            var statsTask = UpdateStatsAsync(serverCts.Token);
            
            var allTasks = new List<Task> { acceptTask, monitorTask, statsTask };
            
            // Запускаем собственный поиск сервера
            if (this.enableServerSearch)
            {
                serverSearchTask = RunServerSearchAsync(serverCts.Token);
                allTasks.Add(serverSearchTask);
                Log($"[SERVER] Запущен собственный поиск с {this.serverThreads} потоками");
            }
            
            await Task.WhenAny(allTasks);
        }
        
        public void Stop()
        {
            if (!isRunning) return;
            
            Log("[SERVER] Остановка сервера...");
            isRunning = false;
            
            // Отправляем команду SHUTDOWN всем агентам
            foreach (var agent in connectedAgents.Values)
            {
                try
                {
                    var shutdownMsg = JsonSerializer.Serialize(new { command = "SHUTDOWN" });
                    agent.Writer?.WriteLine(shutdownMsg);
                }
                catch { }
            }
            
            serverCts?.Cancel();
            tcpListener?.Stop();
            
            Log("[SERVER] Сервер остановлен");
        }
        
        private void GenerateSearchBlocks()
        {
            Log("[SERVER] Генерация блоков заданий...");
            
            long blocksGenerated = 0;
            
            // Улучшенная адаптивная система размеров блоков
            long adaptiveBlockSize = CalculateOptimalBlockSize();
            
            Log($"[SERVER] Используется размер блока: {adaptiveBlockSize:N0}");
            
            // Генерируем больше блоков, убираем ограничение в 10000
            for (long startIndex = 0; startIndex < totalCombinations; startIndex += adaptiveBlockSize)
            {
                long endIndex = Math.Min(startIndex + adaptiveBlockSize - 1, totalCombinations - 1);
                
                var block = new SearchBlock
                {
                    BlockId = currentBlockId++,
                    StartIndex = startIndex,
                    EndIndex = endIndex,
                    WordCount = wordCount,
                    TargetAddress = targetAddress,
                    Status = BlockStatus.Pending,
                    CreatedAt = DateTime.Now,
                    CurrentIndex = startIndex,
                    Priority = CalculateBlockPriority(startIndex, endIndex)
                };
                
                pendingBlocks.Enqueue(block);
                blocksGenerated++;
                
                // Увеличиваем лимит блоков в памяти и добавляем динамическую генерацию
                if (blocksGenerated >= 50000) // Увеличено с 10000
                {
                    Log($"[SERVER] Сгенерировано {blocksGenerated:N0} блоков (предварительная партия)");
                    Log($"[SERVER] Покрыто {endIndex + 1:N0} из {totalCombinations:N0} комбинаций");
                    break;
                }
            }
            
            Log($"[SERVER] Генерация завершена: {blocksGenerated:N0} блоков, размер блока: {adaptiveBlockSize:N0}");
        }
        
        private long CalculateOptimalBlockSize()
        {
            // Базовый размер блока
            long baseSize = blockSize;
            
            // Адаптируем по общему объему задачи
            if (totalCombinations > 10_000_000_000) // Более 10 млрд
            {
                baseSize = Math.Min(blockSize * 50, 5_000_000); // Очень большие блоки
            }
            else if (totalCombinations > 1_000_000_000) // Более 1 млрд
            {
                baseSize = Math.Min(blockSize * 10, 1_000_000); // Большие блоки
            }
            else if (totalCombinations < 100_000) // Менее 100к
            {
                baseSize = Math.Max(blockSize / 10, 1000); // Маленькие блоки
            }
            else if (totalCombinations < 1_000_000) // Менее 1млн
            {
                baseSize = Math.Max(blockSize / 5, 5000); // Небольшие блоки
            }
            
            return baseSize;
        }
        
        private int CalculateBlockPriority(long startIndex, long endIndex)
        {
            // Приоритет на основе позиции в поиске (чем раньше, тем выше приоритет)
            var progress = (double)startIndex / totalCombinations;
            
            if (progress < 0.1) return 10; // Высокий приоритет для первых 10%
            if (progress < 0.3) return 8;  // Высокий для первых 30%
            if (progress < 0.5) return 6;  // Средний для первых 50%
            if (progress < 0.8) return 4;  // Низкий для 50-80%
            return 2; // Очень низкий для последних 20%
        }
        
        private async Task RunServerSearchAsync(CancellationToken token)
        {
            Log("[SERVER] Начинаем собственный поиск сервера...");
            
            try
            {
                var tasks = new List<Task>();
                
                // Запускаем потоки поиска
                for (int threadId = 0; threadId < serverThreads; threadId++)
                {
                    int localThreadId = threadId;
                    var task = Task.Run(async () => await ServerSearchWorkerAsync(localThreadId, token), token);
                    tasks.Add(task);
                }
                
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                Log("[SERVER] Поиск сервера остановлен");
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка в поиске сервера: {ex.Message}");
            }
        }
        
        private async Task ServerSearchWorkerAsync(int threadId, CancellationToken token)
        {
            Log($"[SERVER] Поток сервера {threadId} запущен");
            
            var finder = new AdvancedSeedPhraseFinder();
            var bip39Words = finder.GetBip39Words();
            
            while (!token.IsCancellationRequested)
            {
                // Получаем блок для обработки
                SearchBlock? block = GetNextBlockForServer();
                if (block == null)
                {
                    await Task.Delay(5000, token); // Ждем новых блоков
                    continue;
                }
                
                Log($"[SERVER] Поток {threadId} обрабатывает блок {block.BlockId} ({block.StartIndex}-{block.EndIndex})");
                
                try
                {
                    await ProcessServerBlockAsync(block, finder, bip39Words, threadId, token);
                    
                    // Помечаем блок как завершенный
                    lock (lockObject)
                    {
                        block.Status = BlockStatus.Completed;
                        block.CompletedAt = DateTime.Now;
                        block.AssignedTo = $"SERVER_THREAD_{threadId}";
                        completedBlocks++;
                    }
                    
                    Log($"[SERVER] Поток {threadId} завершил блок {block.BlockId}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[SERVER] Ошибка обработки блока {block.BlockId} в потоке {threadId}: {ex.Message}");
                    
                    // Возвращаем блок в очередь при ошибке
                    lock (lockObject)
                    {
                        block.Status = BlockStatus.Pending;
                        block.AssignedTo = null;
                        block.AssignedAt = null;
                    }
                    pendingBlocks.Enqueue(block);
                }
            }
        }
        
        private SearchBlock? GetNextBlockForServer()
        {
            lock (lockObject)
            {
                if (pendingBlocks.TryDequeue(out SearchBlock? block))
                {
                    block.Status = BlockStatus.Assigned;
                    block.AssignedTo = "SERVER";
                    block.AssignedAt = DateTime.Now;
                    assignedBlocks.TryAdd(block.BlockId, block);
                    return block;
                }
            }
            return null;
        }
        
        private async Task ProcessServerBlockAsync(SearchBlock block, AdvancedSeedPhraseFinder finder, 
            List<string> bip39Words, int threadId, CancellationToken token)
        {
            long processed = 0;
            var startTime = DateTime.Now;
            
            for (long i = block.StartIndex; i <= block.EndIndex && !token.IsCancellationRequested; i++)
            {
                try
                {
                    // Генерируем seed-фразу по индексу
                    string seedPhrase = GenerateSeedPhraseByIndex(i, bip39Words, block.WordCount);
                    
                    // Проверяем валидность
                    if (!finder.IsValidSeedPhrase(seedPhrase))
                        continue;
                    
                    // Генерируем адрес и проверяем совпадение
                    string generatedAddress = finder.GenerateBitcoinAddress(seedPhrase);
                    if (generatedAddress == block.TargetAddress)
                    {
                        // Найдено совпадение!
                        string? privateKey = null;
                        try
                        {
                            var mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                            var seed = mnemonic.DeriveSeed();
                            var masterKey = ExtKey.CreateFromSeed(seed);
                            var fullPath = new KeyPath("44'/0'/0'/0/0");
                            var key = masterKey.Derive(fullPath).PrivateKey;
                            privateKey = key.GetWif(Network.Main).ToString();
                        }
                        catch { }
                        
                        var result = $"*** НАЙДЕНО СЕРВЕРОМ! *** Фраза: {seedPhrase}, Ключ: {privateKey}, Адрес: {generatedAddress}, Поток: {threadId}";
                        foundResults.Add(result);
                        OnFoundResult?.Invoke(result);
                        Log($"[SERVER] {result}");
                        
                        // Сохраняем результат в файл
                        await SaveFoundResultAsync(seedPhrase, privateKey, generatedAddress, "SERVER", threadId);
                    }
                    
                    processed++;
                    Interlocked.Increment(ref serverProcessedCount);
                    Interlocked.Increment(ref totalProcessed);
                    
                    // Обновляем прогресс блока
                    block.CurrentIndex = i;
                    block.LastProgressAt = DateTime.Now;
                    
                    if (processed % 10000 == 0)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var rate = processed / Math.Max(elapsed.TotalSeconds, 1);
                        Log($"[SERVER] Поток {threadId}: обработано {processed:N0}, скорость {rate:F0}/сек");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[SERVER] Ошибка обработки индекса {i} в потоке {threadId}: {ex.Message}");
                }
            }
        }
        
        private string GenerateSeedPhraseByIndex(long index, List<string> bip39Words, int wordCount)
        {
            var words = new string[wordCount];
            var tempIndex = index;
            
            for (int pos = 0; pos < wordCount; pos++)
            {
                words[pos] = bip39Words[(int)(tempIndex % bip39Words.Count)];
                tempIndex /= bip39Words.Count;
            }
            
            return string.Join(" ", words);
        }
        
        private async Task SaveFoundResultAsync(string seedPhrase, string? privateKey, string address, string foundBy, int threadId)
        {
            try
            {
                var resultData = new
                {
                    Timestamp = DateTime.Now,
                    SeedPhrase = seedPhrase,
                    PrivateKey = privateKey,
                    Address = address,
                    FoundBy = foundBy,
                    ThreadId = threadId,
                    SearchStartTime = searchStartTime,
                    TotalProcessed = totalProcessed
                };
                
                var json = JsonSerializer.Serialize(resultData, new JsonSerializerOptions { WriteIndented = true });
                var fileName = $"found_result_{DateTime.Now:yyyyMMdd_HHmmss}_{foundBy}_T{threadId}.json";
                
                await File.WriteAllTextAsync(fileName, json);
                Log($"[SERVER] Результат сохранен в файл: {fileName}");
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка сохранения результата: {ex.Message}");
            }
        }
        
        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && isRunning)
            {
                try
                {
                    var tcpClient = await tcpListener!.AcceptTcpClientAsync();
                    var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
                    
                    Log($"[SERVER] Новое подключение от {clientEndpoint}");
                    
                    // Обрабатываем клиента в отдельной задаче
                    _ = Task.Run(() => HandleClientAsync(tcpClient, clientEndpoint, token));
                }
                catch (ObjectDisposedException)
                {
                    break; // Сервер остановлен
                }
                catch (Exception ex)
                {
                    Log($"[SERVER] Ошибка при принятии подключения: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }
        
        private async Task HandleClientAsync(TcpClient client, string clientEndpoint, CancellationToken token)
        {
            string agentId = $"Agent_{DateTime.Now:HHmmss}_{clientEndpoint}";
            AgentConnection? agentConnection = null;
            
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
                {
                    agentConnection = new AgentConnection
                    {
                        AgentId = agentId,
                        Endpoint = clientEndpoint,
                        ConnectedAt = DateTime.Now,
                        LastActivity = DateTime.Now,
                        Writer = writer,
                        IsConnected = true
                    };
                    
                    connectedAgents.TryAdd(agentId, agentConnection);
                    agentStats.TryAdd(agentId, new AgentStats { AgentId = agentId });
                    
                    Log($"[SERVER] Агент {agentId} подключен");
                    
                    // Ждем приветствие от агента (опционально - для обратной совместимости)
                    bool isEnhancedAgent = false;
                    
                    while (!token.IsCancellationRequested && client.Connected && isRunning)
                    {
                        try
                        {
                            // Читаем сообщение от агента с таймаутом
                            var lineTask = reader.ReadLineAsync();
                            var timeoutTask = Task.Delay(120000, token); // 2 минуты таймаут
                            
                            var completedTask = await Task.WhenAny(lineTask, timeoutTask);
                            if (completedTask == timeoutTask)
                            {
                                Log($"[SERVER] Таймаут ожидания сообщения от {agentId}");
                                break;
                            }
                            
                            string? line = await lineTask;
                            if (line == null) break;
                            
                            agentConnection.LastActivity = DateTime.Now;
                            
                            Dictionary<string, object>? request = null;
                            try
                            {
                                request = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                            }
                            catch (JsonException ex)
                            {
                                Log($"[SERVER] Ошибка JSON от {agentId}: {ex.Message}");
                                await SendErrorResponse(writer, "INVALID_JSON", ex.Message);
                                continue;
                            }
                            
                            if (request == null || !request.ContainsKey("command"))
                            {
                                Log($"[SERVER] Некорректное сообщение от {agentId}: нет команды");
                                await SendErrorResponse(writer, "MISSING_COMMAND", "Command field is required");
                                continue;
                            }
                            
                            string command = request["command"].ToString()!;
                            
                            // Валидируем команды
                            if (!IsValidCommand(command))
                            {
                                Log($"[SERVER] Неизвестная команда от {agentId}: {command}");
                                await SendErrorResponse(writer, "UNKNOWN_COMMAND", $"Unknown command: {command}");
                                continue;
                            }
                            
                            try
                            {
                                switch (command)
                                {
                                    case "AGENT_HELLO":
                                        isEnhancedAgent = await HandleAgentHello(request, agentConnection, writer);
                                        break;
                                        
                                    case "AGENT_GOODBYE":
                                        await HandleAgentGoodbye(request, agentId, writer);
                                        Log($"[SERVER] Агент {agentId} попрощался");
                                        return; // Выходим из цикла
                                        
                                    case "HEARTBEAT":
                                        await HandleHeartbeat(request, agentConnection, writer);
                                        break;
                                        
                                    case "GET_TASK":
                                        await HandleGetTaskRequest(agentConnection, writer);
                                        break;
                                        
                                    case "TASK_ACCEPTED":
                                        await HandleTaskAccepted(request, agentId, writer);
                                        break;
                                        
                                    case "TASK_COMPLETED":
                                        await HandleTaskCompleted(request, agentId, writer);
                                        break;
                                        
                                    case "REPORT_PROGRESS":
                                        await HandleProgressReport(request, agentId);
                                        await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "ACK" }));
                                        break;
                                        
                                    case "REPORT_FOUND":
                                        await HandleFoundReport(request, agentId);
                                        await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "ACK" }));
                                        break;
                                        
                                    case "RELEASE_BLOCK":
                                        await HandleBlockRelease(request, agentId);
                                        await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "ACK" }));
                                        break;
                                        
                                    case "PING":
                                        await writer.WriteLineAsync(JsonSerializer.Serialize(new { 
                                            command = "PONG", 
                                            timestamp = DateTime.Now,
                                            serverTime = DateTime.Now
                                        }));
                                        break;
                                        
                                    case "PONG":
                                        // Ответ на наш PING - просто обновляем активность
                                        Log($"[SERVER] Получен PONG от {agentId}");
                                        break;
                                        
                                    case "REQUEST_STATUS":
                                        await HandleStatusRequest(agentConnection, writer);
                                        break;
                                        
                                    default:
                                        Log($"[SERVER] Неподдерживаемая команда от {agentId}: {command}");
                                        await SendErrorResponse(writer, "UNSUPPORTED_COMMAND", $"Command {command} is not supported in this version");
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"[SERVER] Ошибка обработки команды {command} от {agentId}: {ex.Message}");
                                await SendErrorResponse(writer, "PROCESSING_ERROR", ex.Message);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Log($"[SERVER] Общая ошибка при обработке {agentId}: {ex.Message}");
                            await Task.Delay(1000, token); // Пауза перед продолжением
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка соединения с {agentId}: {ex.Message}");
            }
            finally
            {
                // Очистка при отключении агента
                if (agentConnection != null)
                {
                    agentConnection.IsConnected = false;
                    agentConnection.DisconnectedAt = DateTime.Now;
                    
                    // Возвращаем назначенные блоки в очередь
                    var blocksReturned = 0;
                    foreach (var kvp in assignedBlocks.ToList())
                    {
                        if (kvp.Value.AssignedTo == agentId)
                        {
                            kvp.Value.Status = BlockStatus.Pending;
                            kvp.Value.AssignedTo = null;
                            kvp.Value.AssignedAt = null;
                            pendingBlocks.Enqueue(kvp.Value);
                            assignedBlocks.TryRemove(kvp.Key, out _);
                            blocksReturned++;
                        }
                    }
                    
                    if (blocksReturned > 0)
                    {
                        Log($"[SERVER] {blocksReturned} блоков возвращено в очередь после отключения {agentId}");
                    }
                }
                
                connectedAgents.TryRemove(agentId, out _);
                agentStats.TryRemove(agentId, out _);
                Log($"[SERVER] Агент {agentId} отключен");
            }
        }
        
        private bool IsValidCommand(string command)
        {
            var validCommands = new HashSet<string>
            {
                "AGENT_HELLO", "AGENT_GOODBYE", "HEARTBEAT", "GET_TASK", 
                "TASK_ACCEPTED", "TASK_COMPLETED", "REPORT_PROGRESS", 
                "REPORT_FOUND", "RELEASE_BLOCK", "PING", "PONG", "REQUEST_STATUS"
            };
            return validCommands.Contains(command);
        }
        
        private async Task SendErrorResponse(StreamWriter writer, string errorCode, string errorMessage)
        {
            try
            {
                var errorResponse = new
                {
                    command = "ERROR",
                    errorCode = errorCode,
                    errorMessage = errorMessage,
                    timestamp = DateTime.Now
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(errorResponse));
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка отправки ошибки: {ex.Message}");
            }
        }
        
        private async Task<bool> HandleAgentHello(Dictionary<string, object> request, AgentConnection agent, StreamWriter writer)
        {
            try
            {
                string version = request.ContainsKey("version") ? request["version"].ToString()! : "1.0";
                var capabilities = new List<string>();
                
                if (request.ContainsKey("capabilities"))
                {
                    var capsElement = (JsonElement)request["capabilities"];
                    if (capsElement.ValueKind == JsonValueKind.Array)
                    {
                        capabilities = capsElement.EnumerateArray()
                            .Select(e => e.GetString() ?? "")
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    }
                }
                
                // Обновляем информацию об агенте
                if (request.ContainsKey("agentId"))
                {
                    string providedId = request["agentId"].ToString()!;
                    if (!string.IsNullOrWhiteSpace(providedId))
                    {
                        agent.AgentId = providedId;
                    }
                }
                
                // Отправляем ответ
                var response = new
                {
                    command = "HELLO_ACK",
                    serverVersion = "2.0",
                    serverCapabilities = new[] { "DISTRIBUTED_SEARCH", "PROGRESS_TRACKING", "HEARTBEAT", "BLOCK_MANAGEMENT" },
                    maxBlockSize = blockSize,
                    supportedWordCounts = new[] { 12, 15, 18, 21, 24 },
                    timestamp = DateTime.Now
                };
                
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                
                Log($"[SERVER] Приветствие агента {agent.AgentId} (версия {version}, возможности: {string.Join(", ", capabilities)})");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка обработки приветствия: {ex.Message}");
                await SendErrorResponse(writer, "HELLO_ERROR", ex.Message);
                return false;
            }
        }
        
        private async Task HandleAgentGoodbye(Dictionary<string, object> request, string agentId, StreamWriter writer)
        {
            try
            {
                var response = new
                {
                    command = "GOODBYE_ACK",
                    message = "Goodbye, thank you for your service",
                    timestamp = DateTime.Now
                };
                
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка обработки прощания: {ex.Message}");
            }
        }
        
        private async Task HandleHeartbeat(Dictionary<string, object> request, AgentConnection agent, StreamWriter writer)
        {
            try
            {
                string status = request.ContainsKey("status") ? request["status"].ToString()! : "unknown";
                
                var response = new
                {
                    command = "HEARTBEAT_ACK",
                    serverStatus = "running",
                    timestamp = DateTime.Now,
                    uptime = DateTime.Now - searchStartTime
                };
                
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                
                // Обновляем статистику активности
                agent.LastActivity = DateTime.Now;
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка обработки heartbeat: {ex.Message}");
                await SendErrorResponse(writer, "HEARTBEAT_ERROR", ex.Message);
            }
        }
        
        private async Task HandleTaskAccepted(Dictionary<string, object> request, string agentId, StreamWriter writer)
        {
            try
            {
                int blockId = Convert.ToInt32(request["blockId"]);
                
                if (assignedBlocks.TryGetValue(blockId, out var block) && block.AssignedTo == agentId)
                {
                    block.Status = BlockStatus.Assigned; // Подтверждаем назначение
                    Log($"[SERVER] Агент {agentId} подтвердил принятие блока {blockId}");
                    
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "ACK" }));
                }
                else
                {
                    Log($"[SERVER] Агент {agentId} пытается подтвердить несуществующий блок {blockId}");
                    await SendErrorResponse(writer, "INVALID_BLOCK", $"Block {blockId} not found or not assigned to you");
                }
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка подтверждения задания: {ex.Message}");
                await SendErrorResponse(writer, "TASK_ACCEPTED_ERROR", ex.Message);
            }
        }
        
        private async Task HandleTaskCompleted(Dictionary<string, object> request, string agentId, StreamWriter writer)
        {
            try
            {
                int blockId = Convert.ToInt32(request["blockId"]);
                
                if (assignedBlocks.TryRemove(blockId, out var block) && block.AssignedTo == agentId)
                {
                    block.Status = BlockStatus.Completed;
                    block.CompletedAt = DateTime.Now;
                    
                    // Обновляем статистику
                    if (agentStats.TryGetValue(agentId, out var stats))
                    {
                        stats.CompletedBlocks++;
                        stats.LastUpdate = DateTime.Now;
                    }
                    
                    Interlocked.Increment(ref completedBlocks);
                    
                    Log($"[SERVER] Агент {agentId} завершил блок {blockId}");
                    
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "ACK" }));
                }
                else
                {
                    Log($"[SERVER] Агент {agentId} пытается завершить несуществующий блок {blockId}");
                    await SendErrorResponse(writer, "INVALID_BLOCK", $"Block {blockId} not found or not assigned to you");
                }
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка завершения задания: {ex.Message}");
                await SendErrorResponse(writer, "TASK_COMPLETED_ERROR", ex.Message);
            }
        }
        
        private async Task HandleStatusRequest(AgentConnection agent, StreamWriter writer)
        {
            try
            {
                var status = new
                {
                    command = "STATUS_RESPONSE",
                    serverStatus = "running",
                    connectedAgents = connectedAgents.Count,
                    pendingBlocks = pendingBlocks.Count,
                    assignedBlocks = assignedBlocks.Count,
                    completedBlocks = completedBlocks,
                    totalProcessed = totalProcessed,
                    foundResults = foundResults.Count,
                    uptime = DateTime.Now - searchStartTime,
                    timestamp = DateTime.Now
                };
                
                await writer.WriteLineAsync(JsonSerializer.Serialize(status));
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка получения статуса: {ex.Message}");
                await SendErrorResponse(writer, "STATUS_ERROR", ex.Message);
            }
        }
        
        private async Task HandleGetTaskRequest(AgentConnection agent, StreamWriter writer)
        {
            try
            {
                SearchBlock? block = null;
                
                // Пытаемся получить блок с приоритетом
                lock (lockObject)
                {
                    // Ищем блоки с высоким приоритетом сначала
                    var availableBlocks = pendingBlocks.ToArray().OrderByDescending(b => b.Priority).ToArray();
                    
                    if (availableBlocks.Length > 0)
                    {
                        block = availableBlocks[0];
                        
                        // Удаляем найденный блок из очереди
                        var tempQueue = new ConcurrentQueue<SearchBlock>();
                        SearchBlock? tempBlock;
                        while (pendingBlocks.TryDequeue(out tempBlock))
                        {
                            if (tempBlock.BlockId != block.BlockId)
                                tempQueue.Enqueue(tempBlock);
                        }
                        
                        // Возвращаем все блоки обратно кроме выбранного
                        while (tempQueue.TryDequeue(out tempBlock))
                        {
                            pendingBlocks.Enqueue(tempBlock);
                        }
                    }
                }
                
                if (block != null)
                {
                    block.Status = BlockStatus.Assigned;
                    block.AssignedTo = agent.AgentId;
                    block.AssignedAt = DateTime.Now;
                    
                    assignedBlocks.TryAdd(block.BlockId, block);
                    
                    var response = new
                    {
                        command = "TASK",
                        blockId = block.BlockId,
                        startIndex = block.StartIndex,
                        endIndex = block.EndIndex,
                        wordCount = block.WordCount,
                        targetAddress = block.TargetAddress, // Используем targetAddress вместо address
                        priority = block.Priority,
                        estimatedCombinations = block.EndIndex - block.StartIndex + 1,
                        timestamp = DateTime.Now,
                        serverVersion = "2.0"
                    };
                    
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                    Log($"[SERVER] Блок {block.BlockId} (приоритет {block.Priority}) назначен агенту {agent.AgentId} ({block.StartIndex:N0}-{block.EndIndex:N0})");
                }
                else
                {
                    // Генерируем новые блоки если очередь пуста
                    if (pendingBlocks.IsEmpty && currentBlockId * blockSize < totalCombinations)
                    {
                        GenerateMoreBlocks();
                        
                        // Пробуем снова после генерации
                        if (pendingBlocks.TryDequeue(out block))
                        {
                            block.Status = BlockStatus.Assigned;
                            block.AssignedTo = agent.AgentId;
                            block.AssignedAt = DateTime.Now;
                            
                            assignedBlocks.TryAdd(block.BlockId, block);
                            
                            var response = new
                            {
                                command = "TASK",
                                blockId = block.BlockId,
                                startIndex = block.StartIndex,
                                endIndex = block.EndIndex,
                                wordCount = block.WordCount,
                                targetAddress = block.TargetAddress,
                                priority = block.Priority,
                                estimatedCombinations = block.EndIndex - block.StartIndex + 1,
                                timestamp = DateTime.Now,
                                serverVersion = "2.0"
                            };
                            
                            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                            Log($"[SERVER] Новый блок {block.BlockId} назначен агенту {agent.AgentId}");
                            return;
                        }
                    }
                    
                    // Нет доступных заданий
                    var noTaskResponse = new { 
                        command = "NO_TASK",
                        reason = "All blocks are assigned or search is complete",
                        totalBlocks = currentBlockId - 1,
                        pendingBlocks = pendingBlocks.Count,
                        assignedBlocks = assignedBlocks.Count,
                        completedBlocks = completedBlocks,
                        timestamp = DateTime.Now
                    };
                    
                    await writer.WriteLineAsync(JsonSerializer.Serialize(noTaskResponse));
                }
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка обработки запроса задания от {agent.AgentId}: {ex.Message}");
                await SendErrorResponse(writer, "TASK_REQUEST_ERROR", ex.Message);
            }
        }
        
        private void GenerateMoreBlocks()
        {
            Log("[SERVER] Генерация дополнительных блоков...");
            
            long adaptiveBlockSize = CalculateOptimalBlockSize();
            int blocksToGenerate = Math.Min(10000, (int)((totalCombinations - (currentBlockId * blockSize)) / adaptiveBlockSize));
            
            if (blocksToGenerate <= 0) return;
            
            for (int i = 0; i < blocksToGenerate; i++)
            {
                long startIndex = currentBlockId * adaptiveBlockSize;
                long endIndex = Math.Min(startIndex + adaptiveBlockSize - 1, totalCombinations - 1);
                
                if (startIndex >= totalCombinations) break;
                
                var block = new SearchBlock
                {
                    BlockId = currentBlockId++,
                    StartIndex = startIndex,
                    EndIndex = endIndex,
                    WordCount = wordCount,
                    TargetAddress = targetAddress,
                    Status = BlockStatus.Pending,
                    CreatedAt = DateTime.Now,
                    CurrentIndex = startIndex,
                    Priority = CalculateBlockPriority(startIndex, endIndex)
                };
                
                pendingBlocks.Enqueue(block);
            }
            
            Log($"[SERVER] Сгенерировано {blocksToGenerate} дополнительных блоков");
        }
        
        private async Task HandleProgressReport(Dictionary<string, object> request, string agentId)
        {
            int blockId = Convert.ToInt32(request["blockId"]);
            long currentIndex = Convert.ToInt64(request["currentIndex"]);
            long processed = request.ContainsKey("processed") ? Convert.ToInt64(request["processed"]) : 0;
            double rate = request.ContainsKey("rate") ? Convert.ToDouble(request["rate"]) : 0;
            
            if (assignedBlocks.TryGetValue(blockId, out var block) && block.AssignedTo == agentId)
            {
                block.CurrentIndex = currentIndex;
                block.LastProgressAt = DateTime.Now;
                
                // Обновляем статистику агента
                if (agentStats.TryGetValue(agentId, out var stats))
                {
                    stats.ProcessedCount = processed;
                    stats.CurrentRate = rate;
                    stats.LastUpdate = DateTime.Now;
                }
                
                Interlocked.Add(ref totalProcessed, processed - (stats?.ProcessedCount ?? 0));
            }
        }
        
        private async Task HandleFoundReport(Dictionary<string, object> request, string agentId)
        {
            string combination = request["combination"].ToString()!;
            string privateKey = request.ContainsKey("privateKey") ? request["privateKey"].ToString()! : "";
            string address = request.ContainsKey("address") ? request["address"].ToString()! : "";
            long index = request.ContainsKey("index") ? Convert.ToInt64(request["index"]) : 0;
            
            var result = $"*** НАЙДЕНО СОВПАДЕНИЕ ***\n" +
                        $"Агент: {agentId}\n" +
                        $"Seed фраза: {combination}\n" +
                        $"Приватный ключ: {privateKey}\n" +
                        $"Адрес: {address}\n" +
                        $"Индекс: {index}\n" +
                        $"Время: {DateTime.Now}";
            
            foundResults.Add(result);
            Log($"[SERVER] *** НАЙДЕНО СОВПАДЕНИЕ АГЕНТОМ {agentId} ***");
            Log($"[SERVER] Seed фраза: {combination}");
            Log($"[SERVER] Приватный ключ: {privateKey}");
            
            OnFoundResult?.Invoke(result);
            
            // Сохраняем в файл
            try
            {
                var resultFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"found_results_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                await File.WriteAllTextAsync(resultFile, result);
                Log($"[SERVER] Результат сохранен в файл: {resultFile}");
            }
            catch (Exception ex)
            {
                Log($"[SERVER] Ошибка сохранения результата: {ex.Message}");
            }
        }
        
        private async Task HandleBlockRelease(Dictionary<string, object> request, string agentId)
        {
            int blockId = Convert.ToInt32(request["blockId"]);
            long totalProcessed = request.ContainsKey("totalProcessed") ? Convert.ToInt64(request["totalProcessed"]) : 0;
            
            if (assignedBlocks.TryRemove(blockId, out var block) && block.AssignedTo == agentId)
            {
                block.Status = BlockStatus.Completed;
                block.CompletedAt = DateTime.Now;
                
                Interlocked.Increment(ref completedBlocks);
                
                // Обновляем статистику агента
                if (agentStats.TryGetValue(agentId, out var stats))
                {
                    stats.CompletedBlocks++;
                    stats.TotalProcessed += totalProcessed;
                }
                
                Log($"[SERVER] Блок {blockId} завершен агентом {agentId} ({totalProcessed:N0} обработано)");
            }
        }
        
        private async Task MonitorAgentsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && isRunning)
            {
                try
                {
                    var now = DateTime.Now;
                    var timeoutThreshold = TimeSpan.FromMinutes(5); // 5 минут таймаут
                    
                    foreach (var agent in connectedAgents.Values.ToArray())
                    {
                        if (now - agent.LastActivity > timeoutThreshold)
                        {
                            Log($"[SERVER] Агент {agent.AgentId} не отвечает, отключение...");
                            agent.IsConnected = false;
                        }
                    }
                    
                    await Task.Delay(30000, token); // Проверка каждые 30 секунд
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[SERVER] Ошибка мониторинга агентов: {ex.Message}");
                }
            }
        }
        
        private async Task UpdateStatsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && isRunning)
            {
                try
                {
                    var stats = new ServerStats
                    {
                        ConnectedAgents = connectedAgents.Count,
                        PendingBlocks = pendingBlocks.Count,
                        AssignedBlocks = assignedBlocks.Count,
                        CompletedBlocks = completedBlocks,
                        TotalProcessed = totalProcessed,
                        TotalCombinations = totalCombinations,
                        FoundResults = foundResults.Count,
                        Uptime = DateTime.Now - searchStartTime,
                        AgentStats = agentStats.Values.ToList()
                    };
                    
                    OnStatsUpdate?.Invoke(stats);
                    
                    await Task.Delay(5000, token); // Обновление каждые 5 секунд
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[SERVER] Ошибка обновления статистики: {ex.Message}");
                }
            }
        }
        
        private void Log(string message)
        {
            var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(logMessage);
            OnLog?.Invoke(logMessage);
        }
        
        public List<string> GetFoundResults() => new List<string>(foundResults);
        
        public ServerStats GetCurrentStats()
        {
            return new ServerStats
            {
                ConnectedAgents = connectedAgents.Count,
                PendingBlocks = pendingBlocks.Count,
                AssignedBlocks = assignedBlocks.Count,
                CompletedBlocks = completedBlocks,
                TotalProcessed = totalProcessed,
                TotalCombinations = totalCombinations,
                FoundResults = foundResults.Count,
                Uptime = DateTime.Now - searchStartTime,
                AgentStats = agentStats.Values.ToList()
            };
        }
    }

    public class AgentConnection
    {
        public string AgentId { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public DateTime ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public StreamWriter? Writer { get; set; }
        public bool IsConnected { get; set; }
    }

    public class SearchBlock
    {
        public int BlockId { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
        public int WordCount { get; set; }
        public string TargetAddress { get; set; } = "";
        public BlockStatus Status { get; set; }
        public string? AssignedTo { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public long CurrentIndex { get; set; }
        public DateTime? LastProgressAt { get; set; }
        public int Priority { get; set; } // Добавляем приоритет
    }

    public enum BlockStatus
    {
        Pending,
        Assigned,
        Completed,
        Failed
    }

    public class AgentStats
    {
        public string AgentId { get; set; } = "";
        public long ProcessedCount { get; set; }
        public double CurrentRate { get; set; }
        public int CompletedBlocks { get; set; }
        public long TotalProcessed { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class ServerStats
    {
        public int ConnectedAgents { get; set; }
        public int PendingBlocks { get; set; }
        public int AssignedBlocks { get; set; }
        public int CompletedBlocks { get; set; }
        public long TotalProcessed { get; set; }
        public long TotalCombinations { get; set; }
        public int FoundResults { get; set; }
        public TimeSpan Uptime { get; set; }
        public List<AgentStats> AgentStats { get; set; } = new List<AgentStats>();
    }
} 
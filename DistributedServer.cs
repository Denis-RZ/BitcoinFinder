using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
        private int currentBlockId = 1; // Переименовано из nextBlockId
        private readonly object lockObject = new object();
        
        // Параметры поиска
        private string targetAddress = "";
        private int wordCount = 12;
        private long totalCombinations = 0;
        private long blockSize = 100000; // Размер блока для каждого агента
        private DateTime searchStartTime;
        
        // Статистика
        private long totalProcessed = 0;
        private int completedBlocks = 0;
        private readonly ConcurrentDictionary<string, AgentStats> agentStats;
        
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

        public async Task StartAsync(string targetBitcoinAddress, int wordCount, long? totalCombinations = null)
        {
            if (isRunning) return;
            
            this.targetAddress = targetBitcoinAddress;
            this.wordCount = wordCount;
            this.searchStartTime = DateTime.Now;
            
            // Вычисляем общее количество комбинаций если не задано
            if (totalCombinations.HasValue)
            {
                this.totalCombinations = totalCombinations.Value;
            }
            else
            {
                // Для полного перебора: 2048^wordCount
                this.totalCombinations = (long)Math.Pow(2048, wordCount);
                if (this.totalCombinations <= 0) // Overflow protection
                    this.totalCombinations = long.MaxValue;
            }
            
            Log($"[SERVER] Запуск сервера на порту {port}");
            Log($"[SERVER] Целевой адрес: {targetAddress}");
            Log($"[SERVER] Количество слов: {wordCount}");
            Log($"[SERVER] Всего комбинаций: {this.totalCombinations:N0}");
            Log($"[SERVER] Размер блока: {blockSize:N0}");
            
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
            
            await Task.WhenAny(acceptTask, monitorTask, statsTask);
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
            
            // Адаптивный размер блока в зависимости от общего объема
            long adaptiveBlockSize = blockSize;
            if (totalCombinations > 1_000_000_000) // Более 1 млрд
            {
                adaptiveBlockSize = Math.Min(blockSize * 10, 1_000_000); // Увеличиваем размер блока
            }
            else if (totalCombinations < 100_000) // Менее 100к
            {
                adaptiveBlockSize = Math.Max(blockSize / 10, 1000); // Уменьшаем размер блока
            }
            
            Log($"[SERVER] Используется размер блока: {adaptiveBlockSize:N0}");
            
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
                    CurrentIndex = startIndex
                };
                
                pendingBlocks.Enqueue(block);
                blocksGenerated++;
                
                // Ограничиваем количество блоков в памяти для больших задач
                if (blocksGenerated >= 10000)
                {
                    Log($"[SERVER] Сгенерировано {blocksGenerated:N0} блоков (ограничение достигнуто)");
                    Log($"[SERVER] Покрыто {endIndex + 1:N0} из {totalCombinations:N0} комбинаций");
                    break;
                }
            }
            
            Log($"[SERVER] Генерация завершена: {blocksGenerated:N0} блоков, размер блока: {adaptiveBlockSize:N0}");
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
                    
                    while (!token.IsCancellationRequested && client.Connected && isRunning)
                    {
                        string? line = await reader.ReadLineAsync();
                        if (line == null) break;
                        
                        agentConnection.LastActivity = DateTime.Now;
                        
                        try
                        {
                            var request = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                            if (request == null) continue;
                            
                            string command = request["command"].ToString()!;
                            
                            switch (command)
                            {
                                case "GET_TASK":
                                    await HandleGetTaskRequest(agentConnection, writer);
                                    break;
                                    
                                case "REPORT_PROGRESS":
                                    await HandleProgressReport(request, agentId);
                                    await writer.WriteLineAsync("ACK");
                                    break;
                                    
                                case "REPORT_FOUND":
                                    await HandleFoundReport(request, agentId);
                                    await writer.WriteLineAsync("ACK");
                                    break;
                                    
                                case "RELEASE_BLOCK":
                                    await HandleBlockRelease(request, agentId);
                                    await writer.WriteLineAsync("ACK");
                                    break;
                                    
                                default:
                                    Log($"[SERVER] Неизвестная команда от {agentId}: {command}");
                                    break;
                            }
                        }
                        catch (JsonException ex)
                        {
                            Log($"[SERVER] Ошибка JSON от {agentId}: {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[SERVER] Ошибка обработки сообщения от {agentId}: {ex.Message}");
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
                    foreach (var kvp in assignedBlocks)
                    {
                        if (kvp.Value.AssignedTo == agentId)
                        {
                            kvp.Value.Status = BlockStatus.Pending;
                            kvp.Value.AssignedTo = null;
                            kvp.Value.AssignedAt = null;
                            pendingBlocks.Enqueue(kvp.Value);
                            assignedBlocks.TryRemove(kvp.Key, out _);
                            Log($"[SERVER] Блок {kvp.Key} возвращен в очередь после отключения {agentId}");
                        }
                    }
                }
                
                connectedAgents.TryRemove(agentId, out _);
                Log($"[SERVER] Агент {agentId} отключен");
            }
        }
        
        private async Task HandleGetTaskRequest(AgentConnection agent, StreamWriter writer)
        {
            if (pendingBlocks.TryDequeue(out var block))
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
                    address = block.TargetAddress,
                    seed = "" // Для полного перебора
                };
                
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                Log($"[SERVER] Блок {block.BlockId} назначен агенту {agent.AgentId} ({block.StartIndex}-{block.EndIndex})");
            }
            else
            {
                var response = new { command = "NO_TASK" };
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
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
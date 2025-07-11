using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder.Distributed;

using ProtoMessage = BitcoinFinder.Distributed.Message;
using ProtoBlockTask = BitcoinFinder.Distributed.BlockTask;

namespace BitcoinFinder
{
    public enum AgentState
    {
        Disconnected,
        Connecting,
        Connected,
        Registered,
        Working,
        Error
    }

    public class DistributedAgentClient : IDisposable
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 5000;
        public int ProgressReportInterval { get; set; } = 10000;
        public string ProgressFile { get; set; } = "agent_progress.json";
        public Action<string>? OnLog { get; set; }
        public Action<long>? OnProgress { get; set; }
        public Action<string>? OnFound { get; set; }
        public Action<AgentState>? OnStateChanged { get; set; }
        
        public AgentState State { get; private set; } = AgentState.Disconnected;

        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;

        public async Task RunAgentAsync(Func<long, long, CancellationToken, Task> searchBlock, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        await client.ConnectAsync(ServerIp, ServerPort, cancellationToken);
                        Log($"[AGENT] Connected to server {ServerIp}:{ServerPort}");
                        using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            // Запросить задание
                            await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "GET_TASK" }));
                            var line = await reader.ReadLineAsync();
                            if (line == null) break;
                            var msg = JsonSerializer.Deserialize<BlockTask>(line);
                            if (msg == null || msg.command != "TASK")
                            {
                                Log("[AGENT] Нет задания, жду 10 сек...");
                                await Task.Delay(10000, cancellationToken);
                                continue;
                            }
                            Log($"[AGENT] Получен блок {msg.blockId}: {msg.startIndex}-{msg.endIndex}");
                            long lastIndex = msg.startIndex;
                            // Восстановить прогресс, если есть
                            if (File.Exists(ProgressFile))
                            {
                                try
                                {
                                    var json = File.ReadAllText(ProgressFile);
                                    var prog = JsonSerializer.Deserialize<AgentProgress>(json);
                                    if (prog != null && prog.blockId == msg.blockId)
                                    {
                                        lastIndex = prog.currentIndex;
                                        Log($"[AGENT] Восстановлен прогресс: {lastIndex}");
                                    }
                                }
                                catch { }
                            }
                            // Запуск вашей логики поиска в диапазоне
                            await searchBlock(lastIndex, msg.endIndex, cancellationToken);
                            // После завершения блока
                            File.Delete(ProgressFile);
                            Log($"[AGENT] Блок {msg.blockId} завершён");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[AGENT] Ошибка: {ex.Message}. Переподключение через 10 сек...");
                    try
                    {
                        await Task.Delay(10000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        public async Task ReportProgressAsync(StreamWriter writer, int blockId, long currentIndex)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "REPORT_PROGRESS", blockId = blockId, currentIndex = currentIndex }));
        }
        public async Task ReportFoundAsync(StreamWriter writer, int blockId, string phrase)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "REPORT_FOUND", blockId = blockId, combination = phrase }));
        }

        // ===== New protocol helper methods =====
        public async Task<bool> ConnectToServer(string ip, int port, CancellationToken token = default)
        {
            try
            {
                SetState(AgentState.Connecting);
                ServerIp = ip;
                ServerPort = port;

                await DisconnectAsync();

                _client = new TcpClient();
                _client.ReceiveTimeout = 60000;
                _client.SendTimeout = 30000;
                _client.NoDelay = true;

                var connectTask = _client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(15000, token);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask || !_client.Connected)
                {
                    throw new TimeoutException($"Таймаут подключения к {ip}:{port}");
                }

                var stream = _client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                SetState(AgentState.Connected);
                Log($"Подключено к серверу {ip}:{port}");
                return true;
            }
            catch (Exception ex)
            {
                SetState(AgentState.Error);
                Log($"Ошибка подключения: {ex.Message}");
                await DisconnectAsync();
                return false;
            }
        }

        public async Task<bool> SendMessage(ProtoMessage message)
        {
            if (_writer == null)
            {
                Log("[AGENT] SendMessage called before connection");
                return false;
            }
            try
            {
                await _writer.WriteLineAsync(message.ToJson());
                return true;
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Send error: {ex.Message}");
                return false;
            }
        }

        public async Task<ProtoMessage?> ReceiveMessage(CancellationToken token = default)
        {
            if (_reader == null)
            {
                Log("[AGENT] ReceiveMessage called before connection");
                return null;
            }
            try
            {
                var line = await _reader.ReadLineAsync();
                if (line == null) return null;
                return ProtoMessage.FromJson(line);
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Receive error: {ex.Message}");
                return null;
            }
        }

        public Task<bool> SendRegisterMessage(string agentId)
        {
            var msg = new ProtoMessage
            {
                Type = MessageType.AGENT_REGISTER,
                AgentId = agentId,
                Timestamp = DateTime.UtcNow
            };
            return SendMessage(msg);
        }

        public Task<bool> RequestTask(string agentId)
        {
            var msg = new ProtoMessage
            {
                Type = MessageType.AGENT_REQUEST_TASK,
                AgentId = agentId,
                Timestamp = DateTime.UtcNow
            };
            return SendMessage(msg);
        }

        public async Task<ProtoBlockTask?> ReceiveTask(CancellationToken token = default)
        {
            var resp = await ReceiveMessage(token);
            if (resp == null) return null;
            if (resp.Type == MessageType.SERVER_TASK_ASSIGNED && resp.Data != null)
            {
                try
                {
                    return resp.Data.Value.Deserialize<ProtoBlockTask>();
                }
                catch (Exception ex)
                {
                    Log($"[AGENT] Failed to parse task: {ex.Message}");
                    return null;
                }
            }
            if (resp.Type == MessageType.SERVER_NO_TASKS)
            {
                Log("[AGENT] No tasks available");
            }
            return null;
        }

        public async Task ProcessTestBlock(ProtoBlockTask task, CancellationToken token = default)
        {
            Log($"[AGENT] Start processing block {task.BlockId} {task.StartIndex}-{task.EndIndex}");
            long current = task.StartIndex;
            while (current <= task.EndIndex && !token.IsCancellationRequested)
            {
                OnProgress?.Invoke(current);
                await Task.Delay(10, token); // simulate work
                current++;
            }
            Log($"[AGENT] Completed block {task.BlockId}");
        }

        public async Task<bool> RegisterAgent(string agentId, CancellationToken token = default)
        {
            if (State != AgentState.Connected) return false;

            try
            {
                var registerMessage = new ProtoMessage
                {
                    Type = MessageType.AGENT_REGISTER,
                    AgentId = agentId,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        version = "2.0",
                        capabilities = new[] { "SEARCH", "PROGRESS_REPORT", "HEARTBEAT" },
                        machineName = Environment.MachineName,
                        processorCount = Environment.ProcessorCount
                    }),
                    Timestamp = DateTime.UtcNow
                };

                await SendMessage(registerMessage);
                var response = await ReceiveMessage(token);

                if (response?.Type == MessageType.AGENT_REGISTER)
                {
                    SetState(AgentState.Registered);
                    Log($"Агент {agentId} зарегистрирован успешно");
                    return true;
                }
                else
                {
                    Log($"Сервер отклонил регистрацию агента: {response?.Type}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка регистрации агента: {ex.Message}");
                SetState(AgentState.Error);
                return false;
            }
        }

        public async Task<ProtoBlockTask?> RequestTask(string agentId, CancellationToken token = default)
        {
            if (State != AgentState.Registered && State != AgentState.Working) return null;

            try
            {
                var requestMessage = new ProtoMessage
                {
                    Type = MessageType.AGENT_REQUEST_TASK,
                    AgentId = agentId,
                    Timestamp = DateTime.UtcNow
                };

                await SendMessage(requestMessage);
                var response = await ReceiveMessage(token);

                if (response?.Type == MessageType.SERVER_TASK_ASSIGNED && response.Data.HasValue)
                {
                    var task = response.Data.Value.Deserialize<ProtoBlockTask>();
                    if (task != null)
                    {
                        SetState(AgentState.Working);
                        Log($"Получено задание: блок {task.BlockId} ({task.StartIndex}-{task.EndIndex})");
                        return task;
                    }
                }
                else if (response?.Type == MessageType.SERVER_NO_TASKS)
                {
                    Log("Нет доступных заданий");
                }

                return null;
            }
            catch (Exception ex)
            {
                Log($"Ошибка запроса задания: {ex.Message}");
                SetState(AgentState.Error);
                return null;
            }
        }

        public async Task<bool> ProcessTaskWithProgress(ProtoBlockTask task, string targetAddress, CancellationToken token = default)
        {
            if (State != AgentState.Working) return false;

            try
            {
                Log($"Начинаем обработку блока {task.BlockId}");
                
                var finder = new AdvancedSeedPhraseFinder();
                var bip39Words = finder.GetBip39Words();
                
                long processed = 0;
                long reportInterval = Math.Max(ProgressReportInterval, 1000);
                var startTime = DateTime.Now;
                var lastReportTime = DateTime.Now;

                for (long i = task.StartIndex; i <= task.EndIndex && !token.IsCancellationRequested; i++)
                {
                    try
                    {
                        // Генерируем seed-фразу по индексу
                        var seedPhrase = GenerateSeedPhraseByIndex(i, bip39Words, 12);
                        
                        // Проверяем валидность
                        if (finder.IsValidSeedPhrase(seedPhrase))
                        {
                            var generatedAddress = finder.GenerateBitcoinAddress(seedPhrase);
                            if (generatedAddress == targetAddress)
                            {
                                // Найдено совпадение!
                                var result = $"Блок {task.BlockId}, индекс {i}: {seedPhrase}";
                                OnFound?.Invoke(result);
                                
                                await ReportFound(task.BlockId, seedPhrase, generatedAddress, i);
                                Log($"*** НАЙДЕНО СОВПАДЕНИЕ *** {result}");
                            }
                        }

                        processed++;
                        OnProgress?.Invoke(processed);

                        // Отчет о прогрессе
                        if (processed % reportInterval == 0 || (DateTime.Now - lastReportTime).TotalSeconds > 30)
                        {
                            var rate = processed / Math.Max((DateTime.Now - startTime).TotalSeconds, 1);
                            await ReportProgress(task.BlockId, i, processed, rate);
                            lastReportTime = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка обработки индекса {i}: {ex.Message}");
                    }
                }

                // Отчет о завершении
                await ReportTaskCompleted(task.BlockId, processed);
                Log($"Блок {task.BlockId} обработан полностью ({processed:N0} комбинаций)");
                
                SetState(AgentState.Registered); // Возвращаемся в состояние ожидания заданий
                return true;
            }
            catch (OperationCanceledException)
            {
                Log($"Обработка блока {task.BlockId} отменена");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Ошибка обработки блока {task.BlockId}: {ex.Message}");
                SetState(AgentState.Error);
                return false;
            }
        }

        private void SetState(AgentState newState)
        {
            if (State != newState)
            {
                State = newState;
                OnStateChanged?.Invoke(newState);
            }
        }

        private async Task ReportProgress(int blockId, long currentIndex, long processed, double rate)
        {
            try
            {
                var progressMessage = new ProtoMessage
                {
                    Type = MessageType.AGENT_REPORT_PROGRESS,
                    AgentId = Environment.MachineName,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        blockId = blockId,
                        currentIndex = currentIndex,
                        processed = processed,
                        rate = rate,
                        timestamp = DateTime.UtcNow
                    }),
                    Timestamp = DateTime.UtcNow
                };

                await SendMessage(progressMessage);
            }
            catch (Exception ex)
            {
                Log($"Ошибка отправки прогресса: {ex.Message}");
            }
        }

        private async Task ReportFound(int blockId, string seedPhrase, string address, long index)
        {
            try
            {
                var foundMessage = new ProtoMessage
                {
                    Type = MessageType.AGENT_REPORT_RESULT,
                    AgentId = Environment.MachineName,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        blockId = blockId,
                        seedPhrase = seedPhrase,
                        address = address,
                        index = index,
                        foundAt = DateTime.UtcNow
                    }),
                    Timestamp = DateTime.UtcNow
                };

                await SendMessage(foundMessage);
            }
            catch (Exception ex)
            {
                Log($"Ошибка отправки найденного результата: {ex.Message}");
            }
        }

        private async Task ReportTaskCompleted(int blockId, long processed)
        {
            try
            {
                var completedMessage = new ProtoMessage
                {
                    Type = MessageType.AGENT_BLOCK_COMPLETED,
                    AgentId = Environment.MachineName,
                    Data = JsonSerializer.SerializeToElement(new
                    {
                        blockId = blockId,
                        processed = processed,
                        completedAt = DateTime.UtcNow
                    }),
                    Timestamp = DateTime.UtcNow
                };

                await SendMessage(completedMessage);
            }
            catch (Exception ex)
            {
                Log($"Ошибка отправки завершения задания: {ex.Message}");
            }
        }

        private string GenerateSeedPhraseByIndex(long index, List<string> words, int wordCount)
        {
            var result = new string[wordCount];
            var tempIndex = index;

            for (int i = 0; i < wordCount; i++)
            {
                result[i] = words[(int)(tempIndex % words.Count)];
                tempIndex /= words.Count;
            }

            return string.Join(" ", result);
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _writer?.Dispose();
                _reader?.Dispose();
                _client?.Close();
                _client?.Dispose();

                _writer = null;
                _reader = null;
                _client = null;

                SetState(AgentState.Disconnected);
                Log("Отключено от сервера");
            }
            catch (Exception ex)
            {
                Log($"Ошибка отключения: {ex.Message}");
            }
        }
        private void Log(string msg)
        {
            OnLog?.Invoke(msg);
            Console.WriteLine(msg);
        }
        public void Dispose()
        {
            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Dispose();
            _writer = null;
            _reader = null;
            _client = null;
        }
        public class BlockTask
        {
            public string command { get; set; } = "TASK";
            public int blockId { get; set; }
            public string seed { get; set; } = "";
            public string address { get; set; } = "";
            public int wordCount { get; set; }
            public long startIndex { get; set; }
            public long endIndex { get; set; }
        }
        public class AgentProgress
        {
            public int blockId { get; set; }
            public long currentIndex { get; set; }
        }
    }
} 
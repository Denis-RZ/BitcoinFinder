using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder.Distributed;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        public Action<long, double>? OnProgress { get; set; }
        public Action<string>? OnFound { get; set; }
        public Action<AgentState>? OnStateChanged { get; set; }
        
        public AgentState State { get; private set; } = AgentState.Disconnected;
        public string AgentName { get; set; } = Environment.MachineName;
        public int Threads { get; set; } = 1;

        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private string currentAgentId = "";
        private bool _preventSleep = false;

        // Windows API для предотвращения сна
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [Flags]
        private enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        public void PreventSleep()
        {
            if (!_preventSleep)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED | EXECUTION_STATE.ES_DISPLAY_REQUIRED);
                _preventSleep = true;
                Log("[AGENT] Предотвращение сна компьютера включено");
            }
        }

        public void AllowSleep()
        {
            if (_preventSleep)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                _preventSleep = false;
                Log("[AGENT] Предотвращение сна компьютера отключено");
            }
        }

        // Класс для сохранения прогресса агента
        public class AgentProgress
        {
            public string AgentName { get; set; } = "";
            public string ServerIp { get; set; } = "";
            public int ServerPort { get; set; } = 5000;
            public int Threads { get; set; } = 1;
            public int LastBlockId { get; set; } = -1;
            public long LastIndex { get; set; } = 0;
            public DateTime LastUpdate { get; set; } = DateTime.Now;
        }

                public void SaveProgress(int blockId, long currentIndex)
        {
            try
            {
                var progress = new AgentProgress
                {
                    AgentName = AgentName,
                    ServerIp = ServerIp,
                    ServerPort = ServerPort,
                    Threads = Threads,
                    LastBlockId = blockId,
                    LastIndex = currentIndex,
                    LastUpdate = DateTime.Now
                };
                
                var json = JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("agent_config.json", json);
                Log($"[AGENT] Прогресс сохранен: блок {blockId}, индекс {currentIndex:N0}");
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Ошибка сохранения прогресса: {ex.Message}");
            }
        }
        
        public void SaveAgentConfig()
        {
            try
            {
                var config = new AgentProgress
                {
                    AgentName = AgentName,
                    ServerIp = ServerIp,
                    ServerPort = ServerPort,
                    Threads = Threads,
                    LastBlockId = -1,
                    LastIndex = 0,
                    LastUpdate = DateTime.Now
                };
                
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("agent_config.json", json);
                Log($"[AGENT] Конфигурация агента сохранена");
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Ошибка сохранения конфигурации: {ex.Message}");
            }
        }

        public AgentProgress? LoadProgress()
        {
            try
            {
                // Пробуем загрузить из agent_config.json (новый формат)
                if (File.Exists("agent_config.json"))
                {
                    var json = File.ReadAllText("agent_config.json");
                    var progress = JsonSerializer.Deserialize<AgentProgress>(json);
                    if (progress != null)
                    {
                        Log($"[AGENT] Загружен прогресс: блок {progress.LastBlockId}, индекс {progress.LastIndex:N0}");
                        return progress;
                    }
                }
                
                // Если не найден, пробуем загрузить из старого формата
                if (File.Exists("agent_progress.json"))
                {
                    var json = File.ReadAllText("agent_progress.json");
                    var progress = JsonSerializer.Deserialize<AgentProgress>(json);
                    if (progress != null)
                    {
                        Log($"[AGENT] Загружен прогресс из старого файла: блок {progress.LastBlockId}, индекс {progress.LastIndex:N0}");
                        return progress;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Ошибка загрузки прогресса: {ex.Message}");
            }
            return null;
        }

        public async Task RunAgentAsync(Func<long, long, CancellationToken, Task> searchBlock, CancellationToken cancellationToken)
        {
            // УДАЛЯЕМ СТАРЫЙ ПРОТОКОЛ - он несовместим с сервером
            // Оставляем только новый протокол через RequestTask/ReceiveTask
            Log("[AGENT] Старый протокол отключен, используйте новый протокол через AgentController");
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

        public async Task<bool> SendMessage(object message)
        {
            if (_writer == null)
            {
                Log("[AGENT] SendMessage called before connection");
                return false;
            }
            try
            {
                await _writer.WriteLineAsync(JsonSerializer.Serialize(message));
                return true;
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Send error: {ex.Message}");
                return false;
            }
        }

        public async Task<Dictionary<string, object>?> ReceiveMessage(CancellationToken token = default)
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
                return JsonSerializer.Deserialize<Dictionary<string, object>>(line);
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Receive error: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> SendRegisterMessage(string agentId)
        {
            var msg = new
            {
                command = "HELLO",
                agentId = agentId,
                version = "2.0",
                capabilities = new[] { "DISTRIBUTED_SEARCH", "PROGRESS_TRACKING", "HEARTBEAT" },
                agentName = AgentName,
                threads = Threads,
                timestamp = DateTime.Now
            };
            return await SendMessage(msg);
        }

        public async Task<ProtoBlockTask?> ReceiveTask(CancellationToken token = default)
        {
            var response = await ReceiveMessage(token);
            if (response == null) return null;
            
            try
            {
                if (response.ContainsKey("command"))
                {
                    string command = response["command"].ToString()!;
                    if (command == "TASK")
                    {
                        // Преобразуем ответ сервера в BlockTask
                        var task = new ProtoBlockTask
                        {
                            BlockId = Convert.ToInt32(response["blockId"]),
                            StartIndex = Convert.ToInt64(response["startIndex"]),
                            EndIndex = Convert.ToInt64(response["endIndex"]),
                            SearchParams = new SearchParameters
                            {
                                WordCount = Convert.ToInt32(response["wordCount"]),
                                BitcoinAddress = response["targetAddress"].ToString() ?? ""
                            },
                            Status = "Assigned"
                        };
                        
                        SetState(AgentState.Working);
                        Log($"Получено задание: блок {task.BlockId} ({task.StartIndex}-{task.EndIndex})");
                        return task;
                    }
                    else if (command == "NO_TASK")
                    {
                        Log("Нет доступных заданий");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Failed to parse task: {ex.Message}");
            }
            
            return null;
        }

        public async Task ProcessTestBlock(ProtoBlockTask task, CancellationToken token = default)
        {
            Log($"[AGENT] Start processing block {task.BlockId} {task.StartIndex}-{task.EndIndex}");
            long current = task.StartIndex;
            while (current <= task.EndIndex && !token.IsCancellationRequested)
            {
                OnProgress?.Invoke(current, 0); // simulate work
                await Task.Delay(10, token);
                current++;
            }
            Log($"[AGENT] Completed block {task.BlockId}");
        }

        public async Task<bool> RegisterAgent(string agentId, CancellationToken token = default)
        {
            if (State != AgentState.Connected) return false;

            try
            {
                currentAgentId = agentId; // Сохраняем ID агента
                
                // Отправляем приветственное сообщение в формате, который ожидает сервер
                var helloMessage = new
                {
                    command = "AGENT_HELLO",
                    agentId = agentId,
                    version = "2.0",
                    capabilities = new[] { "DISTRIBUTED_SEARCH", "PROGRESS_TRACKING", "HEARTBEAT" },
                    agentName = AgentName,
                    threads = Threads,
                    timestamp = DateTime.Now
                };

                Log($"[AGENT] Отправляем приветствие: {JsonSerializer.Serialize(helloMessage)}");
                await SendMessage(helloMessage);
                var response = await ReceiveMessage(token);

                if (response != null)
                {
                    Log($"[AGENT] Получен ответ от сервера: {JsonSerializer.Serialize(response)}");
                    
                    if (response.ContainsKey("command"))
                    {
                        string command = response["command"].ToString()!;
                        if (command == "HELLO_ACK")
                        {
                            SetState(AgentState.Registered);
                            Log($"Агент {agentId} зарегистрирован успешно");
                            return true;
                        }
                        else if (command == "ERROR")
                        {
                            string errorMsg = response.ContainsKey("message") ? response["message"].ToString()! : "Unknown error";
                            Log($"Сервер отклонил регистрацию агента: {errorMsg}");
                        }
                    }
                }
                else
                {
                    Log("[AGENT] Не получен ответ от сервера");
                }
                
                return false;
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
                // Отправляем запрос задания в формате, который ожидает сервер
                var requestMessage = new
                {
                    command = "GET_TASK",
                    agentId = agentId,
                    timestamp = DateTime.Now
                };

                await SendMessage(requestMessage);
                var response = await ReceiveMessage(token);

                if (response != null && response.ContainsKey("command"))
                {
                    string command = response["command"].ToString()!;
                    if (command == "TASK")
                    {
                        // Парсим данные задания, правильно извлекая значения из JsonElement
                        var task = new ProtoBlockTask
                        {
                            BlockId = GetInt32Value(response["blockId"]),
                            StartIndex = GetInt64Value(response["startIndex"]),
                            EndIndex = GetInt64Value(response["endIndex"]),
                            SearchParams = new SearchParameters
                            {
                                WordCount = GetInt32Value(response["wordCount"]),
                                BitcoinAddress = GetStringValue(response["targetAddress"])
                            },
                            Status = "Assigned"
                        };
                        
                        SetState(AgentState.Working);
                        Log($"Получено задание: блок {task.BlockId} ({task.StartIndex}-{task.EndIndex})");
                        return task;
                    }
                    else if (command == "NO_TASK")
                    {
                        Log("Нет доступных заданий");
                    }
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
                Log($"[AGENT] Начинаю обработку блока {task.BlockId} ({task.StartIndex:N0}-{task.EndIndex:N0})");
                
                // Включаем предотвращение сна
                PreventSleep();
                
                var finder = new AdvancedSeedPhraseFinder();
                var bip39Words = finder.GetBip39Words();
                
                long currentIndex = task.StartIndex;
                long totalProcessed = 0;
                var startTime = DateTime.Now;
                var lastProgressTime = startTime;
                var lastLogTime = startTime;
                
                // Множество для отслеживания уникальности комбинаций
                var processedCombinations = new HashSet<string>();
                
                while (currentIndex <= task.EndIndex && !token.IsCancellationRequested)
                {
                    // Генерируем seed phrase по индексу
                    var seedPhrase = GenerateSeedPhraseByIndex(currentIndex, bip39Words, task.SearchParams.WordCount);
                    
                    // Проверяем уникальность комбинации
                    if (!processedCombinations.Add(seedPhrase))
                    {
                        Log($"[AGENT] ВНИМАНИЕ: Дублированная комбинация обнаружена: {seedPhrase} (индекс: {currentIndex})");
                    }
                    
                    // Логируем текущую комбинацию каждые 1000 итераций
                    var now = DateTime.Now;
                    if (totalProcessed % 1000 == 0)
                    {
                        Log($"[AGENT] Текущая комбинация: {seedPhrase} (индекс: {currentIndex:N0})");
                    }
                    
                    // Проверяем адрес
                    var address = finder.GenerateBitcoinAddress(seedPhrase);
                    if (address == targetAddress)
                    {
                        Log($"[AGENT] *** НАЙДЕНО СОВПАДЕНИЕ ***");
                        Log($"[AGENT] Seed phrase: {seedPhrase}");
                        Log($"[AGENT] Address: {address}");
                        Log($"[AGENT] Index: {currentIndex}");
                        
                        OnFound?.Invoke($"НАЙДЕНО: {seedPhrase} -> {address} (индекс: {currentIndex})");
                        
                        // Отправляем результат на сервер
                        await ReportFound(task.BlockId, seedPhrase, address, currentIndex);
                    }
                    
                    currentIndex++;
                    totalProcessed++;
                    
                    // Отправляем прогресс каждые 1000 итераций или каждые 10 секунд
                    if (totalProcessed % 1000 == 0 || (now - lastProgressTime).TotalSeconds >= 10)
                    {
                        var rate = totalProcessed / (now - startTime).TotalSeconds;
                        OnProgress?.Invoke(currentIndex, rate);
                        await ReportProgress(task.BlockId, currentIndex, totalProcessed, rate);
                        
                        // Сохраняем прогресс в конфиг
                        SaveProgress(task.BlockId, currentIndex);
                        
                        lastProgressTime = now;
                    }
                }
                
                // Отправляем завершение задания
                await ReportTaskCompleted(task.BlockId, totalProcessed);
                
                Log($"[AGENT] Блок {task.BlockId} обработан успешно. Обработано: {totalProcessed:N0} комбинаций");
                Log($"[AGENT] Уникальных комбинаций: {processedCombinations.Count:N0}");
                
                // Отключаем предотвращение сна
                AllowSleep();
                
                return true;
            }
            catch (OperationCanceledException)
            {
                Log($"[AGENT] Обработка блока {task.BlockId} отменена");
                AllowSleep();
                return false;
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Ошибка обработки блока {task.BlockId}: {ex.Message}");
                AllowSleep();
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
                Log($"[AGENT] Отправляю прогресс: блок {blockId}, индекс {currentIndex:N0}, обработано {processed:N0}, скорость {rate:F1}/сек");
                
                var progressMessage = new
                {
                    command = "REPORT_PROGRESS",
                    agentId = currentAgentId,
                    blockId = blockId,
                    currentIndex = currentIndex,
                    processed = processed,
                    rate = rate,
                    currentCombination = GenerateSeedPhraseByIndex(currentIndex, new AdvancedSeedPhraseFinder().GetBip39Words(), 12),
                    timestamp = DateTime.Now
                };
                
                await SendMessage(progressMessage);
                Log($"[AGENT] Прогресс отправлен успешно");
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Ошибка отправки прогресса: {ex.Message}");
            }
        }

        private async Task ReportFound(int blockId, string seedPhrase, string address, long index)
        {
            try
            {
                var foundMessage = new
                {
                    command = "REPORT_FOUND",
                    agentId = currentAgentId,
                    blockId = blockId,
                    seedPhrase = seedPhrase,
                    address = address,
                    index = index,
                    timestamp = DateTime.Now
                };
                
                await SendMessage(foundMessage);
                Log($"[AGENT] Отправлен результат: {seedPhrase} -> {address}");
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Ошибка отправки результата: {ex.Message}");
            }
        }

        private async Task ReportTaskCompleted(int blockId, long processed)
        {
            try
            {
                var completedMessage = new
                {
                    command = "TASK_COMPLETED",
                    agentId = currentAgentId,
                    blockId = blockId,
                    processed = processed,
                    timestamp = DateTime.Now
                };
                
                await SendMessage(completedMessage);
                Log($"[AGENT] Отправлено завершение блока {blockId}");
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Ошибка отправки завершения: {ex.Message}");
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

        // Вспомогательные методы для извлечения значений из JsonElement
        private int GetInt32Value(object value)
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.GetInt32();
            }
            else if (value is int intValue)
            {
                return intValue;
            }
            else if (value is long longValue)
            {
                return (int)longValue;
            }
            else
            {
                return Convert.ToInt32(value);
            }
        }

        private long GetInt64Value(object value)
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.GetInt64();
            }
            else if (value is long longValue)
            {
                return longValue;
            }
            else if (value is int intValue)
            {
                return intValue;
            }
            else
            {
                return Convert.ToInt64(value);
            }
        }

        private string GetStringValue(object value)
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.GetString() ?? "";
            }
            else if (value is string stringValue)
            {
                return stringValue;
            }
            else
            {
                return value?.ToString() ?? "";
            }
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
    }
} 
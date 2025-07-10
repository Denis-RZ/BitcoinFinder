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
    public class DistributedAgentClient
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int ServerPort { get; set; } = 5000;
        public int ProgressReportInterval { get; set; } = 10000;
        public string ProgressFile { get; set; } = "agent_progress.json";
        public Action<string>? OnLog { get; set; }
        public Action<long>? OnProgress { get; set; }
        public Action<string>? OnFound { get; set; }

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
            ServerIp = ip;
            ServerPort = port;
            try
            {
                _client?.Dispose();
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port, token);
                var stream = _client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                Log($"[AGENT] Connected to server {ip}:{port}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[AGENT] Connection error: {ex.Message}");
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
        private void Log(string msg)
        {
            OnLog?.Invoke(msg);
            Console.WriteLine(msg);
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
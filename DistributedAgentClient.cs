using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

        public async Task RunAgentAsync(Func<long, long, Task> searchBlock)
        {
            while (true)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        await client.ConnectAsync(ServerIp, ServerPort);
                        Log($"[AGENT] Connected to server {ServerIp}:{ServerPort}");
                        var stream = client.GetStream();
                        var reader = new StreamReader(stream, Encoding.UTF8);
                        var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                        while (true)
                        {
                            // Запросить задание
                            await writer.WriteLineAsync(JsonSerializer.Serialize(new { command = "GET_TASK" }));
                            var line = await reader.ReadLineAsync();
                            if (line == null) break;
                            var msg = JsonSerializer.Deserialize<BlockTask>(line);
                            if (msg == null || msg.command != "TASK")
                            {
                                Log("[AGENT] Нет задания, жду 10 сек...");
                                await Task.Delay(10000);
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
                            await searchBlock(lastIndex, msg.endIndex);
                            // После завершения блока
                            File.Delete(ProgressFile);
                            Log($"[AGENT] Блок {msg.blockId} завершён");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[AGENT] Ошибка: {ex.Message}. Переподключение через 10 сек...");
                    await Task.Delay(10000);
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
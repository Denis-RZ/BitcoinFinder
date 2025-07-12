using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BitcoinFinderWebServer
{
    public class TestWinFormsCompatibility
    {
        private readonly string _serverIp;
        private readonly int _serverPort;
        private readonly string _agentName;
        private readonly int _threads;

        public TestWinFormsCompatibility(string serverIp = "127.0.0.1", int serverPort = 5000, string agentName = "TestAgent", int threads = 2)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;
            _agentName = agentName;
            _threads = threads;
        }

        public async Task TestConnectionAsync()
        {
            Console.WriteLine($"Тестирование подключения к {_serverIp}:{_serverPort}");
            
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(_serverIp, _serverPort);
                
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                Console.WriteLine("Подключение установлено");

                // Отправляем HELLO
                var helloMessage = new
                {
                    command = "HELLO",
                    agentName = _agentName,
                    threads = _threads
                };

                await writer.WriteLineAsync(JsonSerializer.Serialize(helloMessage));
                Console.WriteLine($"Отправлен HELLO: {JsonSerializer.Serialize(helloMessage)}");

                // Получаем ответ
                var response = await reader.ReadLineAsync();
                Console.WriteLine($"Получен ответ: {response}");

                var responseObj = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
                if (responseObj != null && responseObj.TryGetValue("command", out var command) && command?.ToString() == "HELLO_ACK")
                {
                    var agentId = responseObj.TryGetValue("agentId", out var id) ? id?.ToString() : "";
                    Console.WriteLine($"Агент зарегистрирован с ID: {agentId}");

                    // Запрашиваем задачу
                    await TestGetTask(writer, reader, agentId);
                }
                else
                {
                    Console.WriteLine("Ошибка регистрации агента");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка подключения: {ex.Message}");
            }
        }

        private async Task TestGetTask(StreamWriter writer, StreamReader reader, string agentId)
        {
            Console.WriteLine("Запрашиваем задачу...");

            var getTaskMessage = new { command = "GET_TASK" };
            await writer.WriteLineAsync(JsonSerializer.Serialize(getTaskMessage));

            var response = await reader.ReadLineAsync();
            Console.WriteLine($"Ответ на GET_TASK: {response}");

            var responseObj = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            if (responseObj != null && responseObj.TryGetValue("command", out var command))
            {
                var cmd = command?.ToString();
                if (cmd == "ASSIGN_BLOCK")
                {
                    Console.WriteLine("Получен блок для обработки!");
                    
                    // Имитируем обработку блока
                    await TestBlockProcessing(writer, reader, responseObj);
                }
                else if (cmd == "NO_BLOCKS")
                {
                    Console.WriteLine("Нет доступных блоков");
                }
            }
        }

        private async Task TestBlockProcessing(StreamWriter writer, StreamReader reader, Dictionary<string, object> blockData)
        {
            var blockId = GetInt32Value(blockData, "blockId");
            var startIndex = GetInt64Value(blockData, "startIndex");
            var endIndex = GetInt64Value(blockData, "endIndex");
            var wordCount = GetInt32Value(blockData, "wordCount");
            var targetAddress = GetStringValue(blockData, "targetAddress");

            Console.WriteLine($"Обрабатываем блок {blockId}: {startIndex}-{endIndex}, {wordCount} слов, адрес: {targetAddress}");

            // Принимаем задачу
            var acceptMessage = new
            {
                command = "TASK_ACCEPTED",
                blockId = blockId
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(acceptMessage));

            // Имитируем прогресс
            for (long i = startIndex; i <= endIndex; i += 1000)
            {
                var progressMessage = new
                {
                    command = "REPORT_PROGRESS",
                    blockId = blockId,
                    currentIndex = i
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(progressMessage));
                await Task.Delay(100); // Имитация работы
            }

            // Завершаем блок
            var completedMessage = new
            {
                command = "TASK_COMPLETED",
                blockId = blockId,
                processed = endIndex - startIndex + 1
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(completedMessage));

            Console.WriteLine($"Блок {blockId} завершен");

            // Отправляем GOODBYE
            var goodbyeMessage = new
            {
                command = "GOODBYE",
                agentId = "TestAgent"
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(goodbyeMessage));

            var response = await reader.ReadLineAsync();
            Console.WriteLine($"Ответ на GOODBYE: {response}");
        }

        private string GetStringValue(Dictionary<string, object> data, string key)
        {
            return data.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";
        }

        private int GetInt32Value(Dictionary<string, object> data, string key)
        {
            if (data.TryGetValue(key, out var value))
            {
                if (value is int intValue) return intValue;
                if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return 0;
        }

        private long GetInt64Value(Dictionary<string, object> data, string key)
        {
            if (data.TryGetValue(key, out var value))
            {
                if (value is long longValue) return longValue;
                if (value is int intValue) return intValue;
                if (long.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return 0;
        }

        public static async Task Main(string[] args)
        {
            var tester = new TestWinFormsCompatibility();
            await tester.TestConnectionAsync();
        }
    }
} 
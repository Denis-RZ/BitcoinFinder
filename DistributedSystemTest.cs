using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using BitcoinFinder;

namespace BitcoinFinderTest
{
    public class DistributedSystemTest
    {
        private const int SERVER_PORT = 8080;
        private const string SERVER_IP = "127.0.0.1";
        private const int AGENT_COUNT = 3;
        
        private TestDistributedMasterServer? server;
        private List<TestAgent> agents = new List<TestAgent>();
        private List<Task> agentTasks = new List<Task>();

        public async Task RunAllTests()
        {
            Console.WriteLine("=== ТЕСТИРОВАНИЕ РАСПРЕДЕЛЕННОЙ СИСТЕМЫ ===");
            Console.WriteLine();

            try
            {
                // Тест 1: Запуск сервера
                await TestServerStartup();
                
                // Тест 2: Подключение агентов
                await TestAgentConnections();
                
                // Тест 3: Передача параметров
                await TestParameterTransmission();
                
                // Тест 4: Распределение блоков
                await TestBlockDistribution();
                
                // Тест 5: Выполнение поиска
                await TestSearchExecution();
                
                // Тест 6: Отчеты о результатах
                await TestResultReporting();
                
                // Тест 7: Завершение работы
                await TestCleanup();
                
                Console.WriteLine("=== ВСЕ ТЕСТЫ ЗАВЕРШЕНЫ УСПЕШНО ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ОШИБКА В ТЕСТАХ: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task TestServerStartup()
        {
            Console.WriteLine("Тест 1: Запуск сервера...");
            
            server = new TestDistributedMasterServer();
            server.BitcoinAddress = "1LqBGSKuX2pXN5LYGdVKr4iVwBZTyyLE9Z";
            server.WordCount = 12;
            server.FullSearch = false;
            server.BlockSize = 1000;
            server.TotalCombinations = 10000;
            
            // Инициализируем блоки
            server.InitBlocks(server.TotalCombinations, server.BlockSize);
            
            Console.WriteLine($"✓ Сервер инициализирован");
            Console.WriteLine($"  - Адрес: {server.BitcoinAddress}");
            Console.WriteLine($"  - Слов: {server.WordCount}");
            Console.WriteLine($"  - Блоков: {server.BlockQueue.Count}");
            Console.WriteLine($"  - Комбинаций: {server.TotalCombinations:N0}");
            Console.WriteLine();
        }

        private async Task TestAgentConnections()
        {
            Console.WriteLine("Тест 2: Подключение агентов...");
            
            for (int i = 0; i < AGENT_COUNT; i++)
            {
                var agent = new TestAgent($"Agent-{i + 1}");
                agents.Add(agent);
                
                // Симулируем подключение агента
                var agentInfo = new TestAgentInfo
                {
                    AgentId = agent.AgentId,
                    IP = SERVER_IP,
                    AssignedBlock = "",
                    LastReport = DateTime.Now
                };
                
                server.AgentList.Add(agentInfo);
                Console.WriteLine($"✓ Агент {agent.AgentId} подключен");
            }
            
            Console.WriteLine($"✓ Всего подключено агентов: {server.AgentList.Count}");
            Console.WriteLine();
        }

        private async Task TestParameterTransmission()
        {
            Console.WriteLine("Тест 3: Передача параметров агентам...");
            
            foreach (var agent in agents)
            {
                // Передаем параметры агенту
                var parameters = new AgentParameters
                {
                    BitcoinAddress = server.BitcoinAddress,
                    WordCount = server.WordCount,
                    FullSearch = server.FullSearch,
                    BlockSize = server.BlockSize,
                    TotalCombinations = server.TotalCombinations
                };
                
                agent.ReceiveParameters(parameters);
                
                Console.WriteLine($"✓ Параметры переданы агенту {agent.AgentId}:");
                Console.WriteLine($"  - Адрес: {parameters.BitcoinAddress}");
                Console.WriteLine($"  - Слов: {parameters.WordCount}");
                Console.WriteLine($"  - Полный поиск: {parameters.FullSearch}");
                Console.WriteLine($"  - Размер блока: {parameters.BlockSize}");
                Console.WriteLine($"  - Всего комбинаций: {parameters.TotalCombinations:N0}");
            }
            Console.WriteLine();
        }

        private async Task TestBlockDistribution()
        {
            Console.WriteLine("Тест 4: Распределение блоков...");
            
            foreach (var agent in agents)
            {
                // Получаем блок для агента
                var block = server.GetNextBlock(agent.AgentId);
                
                if (block != null)
                {
                    agent.ReceiveBlock(block);
                    Console.WriteLine($"✓ Блок {block.BlockId} назначен агенту {agent.AgentId}:");
                    Console.WriteLine($"  - Диапазон: {block.StartIndex:N0} - {block.EndIndex:N0}");
                    Console.WriteLine($"  - Комбинаций: {block.EndIndex - block.StartIndex + 1:N0}");
                }
                else
                {
                    Console.WriteLine($"✗ Не удалось получить блок для агента {agent.AgentId}");
                }
            }
            
            Console.WriteLine($"✓ Свободных блоков осталось: {server.BlockQueue.Count(b => b.Status == "free")}");
            Console.WriteLine();
        }

        private async Task TestSearchExecution()
        {
            Console.WriteLine("Тест 5: Выполнение поиска...");
            
            // Запускаем поиск на всех агентах
            foreach (var agent in agents)
            {
                var task = Task.Run(() => agent.ExecuteSearch());
                agentTasks.Add(task);
            }
            
            // Ждем завершения всех агентов (максимум 30 секунд)
            var timeout = Task.Delay(30000);
            var completed = await Task.WhenAny(Task.WhenAll(agentTasks), timeout);
            
            if (completed == timeout)
            {
                Console.WriteLine("⚠ Поиск не завершился за 30 секунд, прерываем...");
            }
            else
            {
                Console.WriteLine("✓ Все агенты завершили поиск");
            }
            
            // Выводим статистику
            foreach (var agent in agents)
            {
                Console.WriteLine($"Агент {agent.AgentId}:");
                Console.WriteLine($"  - Проверено комбинаций: {agent.CombinationsChecked:N0}");
                Console.WriteLine($"  - Валидных фраз: {agent.ValidPhrasesFound:N0}");
                Console.WriteLine($"  - Найдено результатов: {agent.ResultsFound:N0}");
            }
            Console.WriteLine();
        }

        private async Task TestResultReporting()
        {
            Console.WriteLine("Тест 6: Отчеты о результатах...");
            
            // Собираем все результаты от агентов
            foreach (var agent in agents)
            {
                foreach (var result in agent.FoundResults)
                {
                    server.ReportFound(result);
                    Console.WriteLine($"✓ Результат от агента {agent.AgentId}: {result}");
                }
            }
            
            Console.WriteLine($"✓ Всего результатов на сервере: {server.FoundResults.Count}");
            foreach (var result in server.FoundResults)
            {
                Console.WriteLine($"  - {result}");
            }
            Console.WriteLine();
        }

        private async Task TestCleanup()
        {
            Console.WriteLine("Тест 7: Завершение работы...");
            
            // Освобождаем блоки
            foreach (var agent in agents)
            {
                if (agent.CurrentBlock != null)
                {
                    server.ReleaseBlock(agent.CurrentBlock.BlockId, agent.AgentId);
                    Console.WriteLine($"✓ Блок {agent.CurrentBlock.BlockId} освобожден агентом {agent.AgentId}");
                }
            }
            
            // Очищаем ресурсы
            agents.Clear();
            agentTasks.Clear();
            server = null;
            
            Console.WriteLine("✓ Все ресурсы освобождены");
            Console.WriteLine();
        }
    }

    // Вспомогательные классы для тестирования
    public class TestAgent
    {
        public string AgentId { get; private set; }
        public AgentParameters? Parameters { get; private set; }
        public TestBlockInfo? CurrentBlock { get; private set; }
        public List<string> FoundResults { get; private set; } = new List<string>();
        public long CombinationsChecked { get; private set; } = 0;
        public long ValidPhrasesFound { get; private set; } = 0;
        public long ResultsFound { get; private set; } = 0;

        public TestAgent(string agentId)
        {
            AgentId = agentId;
        }

        public void ReceiveParameters(AgentParameters parameters)
        {
            Parameters = parameters;
        }

        public void ReceiveBlock(TestBlockInfo block)
        {
            CurrentBlock = block;
        }

        public async Task ExecuteSearch()
        {
            if (Parameters == null || CurrentBlock == null)
            {
                Console.WriteLine($"✗ Агент {AgentId}: отсутствуют параметры или блок");
                return;
            }

            Console.WriteLine($"Агент {AgentId} начинает поиск...");
            
            var finder = new AdvancedSeedPhraseFinder();
            
            // Выполняем поиск в блоке
            for (long i = CurrentBlock.StartIndex; i <= CurrentBlock.EndIndex; i++)
            {
                CombinationsChecked++;
                
                try
                {
                    // Генерируем seed-фразу по индексу
                    var possibleWords = new List<string>[Parameters.WordCount];
                    for (int w = 0; w < Parameters.WordCount; w++)
                        possibleWords[w] = finder.GetBip39Words();
                    
                    var combination = finder.GenerateCombinationByIndex(new System.Numerics.BigInteger(i), possibleWords);
                    var seedPhrase = string.Join(" ", combination);
                    
                    // Проверяем валидность
                    if (finder.IsValidSeedPhrase(seedPhrase))
                    {
                        ValidPhrasesFound++;
                        
                        // Генерируем адрес
                        var generatedAddress = finder.GenerateBitcoinAddress(seedPhrase);
                        
                        // Сравниваем с целевым адресом
                        if (generatedAddress == Parameters.BitcoinAddress)
                        {
                            ResultsFound++;
                            FoundResults.Add(seedPhrase);
                            Console.WriteLine($"🎯 Агент {AgentId} НАШЕЛ: {seedPhrase}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Игнорируем ошибки генерации
                }
                
                // Отправляем прогресс каждые 100 комбинаций
                if (CombinationsChecked % 100 == 0)
                {
                    Console.WriteLine($"Агент {AgentId}: проверено {CombinationsChecked:N0} комбинаций");
                }
            }
            
            Console.WriteLine($"Агент {AgentId} завершил поиск");
        }
    }

    public class AgentParameters
    {
        public string BitcoinAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public bool FullSearch { get; set; } = false;
        public int BlockSize { get; set; } = 1000;
        public long TotalCombinations { get; set; } = 0;
    }

    public class TestDistributedMasterServer
    {
        public string BitcoinAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public bool FullSearch { get; set; } = false;
        public int BlockSize { get; set; } = 1000;
        public long TotalCombinations { get; set; } = 0;
        public List<TestBlockInfo> BlockQueue { get; set; } = new List<TestBlockInfo>();
        public List<TestAgentInfo> AgentList { get; set; } = new List<TestAgentInfo>();
        public List<string> FoundResults { get; set; } = new List<string>();

        public void InitBlocks(long totalCombinations, int blockSize)
        {
            BlockQueue.Clear();
            long blockCount = (totalCombinations + blockSize - 1) / blockSize;
            
            for (int i = 0; i < blockCount; i++)
            {
                long startIndex = i * blockSize;
                long endIndex = Math.Min(startIndex + blockSize - 1, totalCombinations - 1);
                
                BlockQueue.Add(new TestBlockInfo
                {
                    BlockId = i,
                    StartIndex = startIndex,
                    EndIndex = endIndex,
                    Status = "free",
                    AssignedTo = ""
                });
            }
        }

        public TestBlockInfo? GetNextBlock(string agentId)
        {
            var freeBlock = BlockQueue.FirstOrDefault(b => b.Status == "free");
            if (freeBlock != null)
            {
                freeBlock.Status = "assigned";
                freeBlock.AssignedTo = agentId;
                return freeBlock;
            }
            return null;
        }

        public void ReleaseBlock(int blockId, string agentId)
        {
            var block = BlockQueue.FirstOrDefault(b => b.BlockId == blockId && b.AssignedTo == agentId);
            if (block != null)
            {
                block.Status = "free";
                block.AssignedTo = "";
            }
        }

        public void ReportFound(string result)
        {
            if (!FoundResults.Contains(result))
            {
                FoundResults.Add(result);
            }
        }
    }

    public class TestBlockInfo
    {
        public int BlockId { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
        public string Status { get; set; } = "free";
        public string AssignedTo { get; set; } = "";
    }

    public class TestAgentInfo
    {
        public string AgentId { get; set; } = "";
        public string IP { get; set; } = "";
        public string AssignedBlock { get; set; } = "";
        public DateTime LastReport { get; set; }
    }
} 
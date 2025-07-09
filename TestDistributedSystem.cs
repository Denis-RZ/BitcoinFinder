using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BitcoinFinder;
using NBitcoin;

namespace BitcoinFinderTest
{
    public class TestDistributedSystem
    {
        private DistributedMasterServer server;
        private int agentCount = 3;
        private int wordCount = 12;
        private int blockSize = 1000;
        private long totalCombinations = 10000;
        private bool fullSearch = true;
        private string address = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ";
        private List<Task> agentTasks = new List<Task>();
        private CancellationTokenSource cts = new CancellationTokenSource();
        private Task serverTask;

        public void RunWithParams(int agentCount, int wordCount, int blockSize, long totalCombinations, bool fullSearch, string address)
        {
            this.agentCount = agentCount;
            this.wordCount = wordCount;
            this.blockSize = blockSize;
            this.totalCombinations = totalCombinations;
            this.fullSearch = fullSearch;
            this.address = address;
            RunAllTests();
        }

        public void RunAllTests()
        {
            Console.WriteLine($"[TEST] Запуск теста: agents={agentCount}, wordCount={wordCount}, blockSize={blockSize}, totalComb={totalCombinations}, fullSearch={fullSearch}, address={address}");
            StartServer();
            Task.Delay(1000).Wait();
            StartAgents(agentCount);
            Task.Delay(2000).Wait();
            PrintStatus();
            // Дать агентам поработать
            Task.Delay(10000).Wait();
            PrintStatus();
            // Эмулировать зависание агента
            Console.WriteLine("[TEST] Эмулируем зависание агента 1");
            cts.CancelAfter(5000);
            Task.Delay(7000).Wait();
            PrintStatus();
            // Перезапуск сервера (тест восстановления)
            Console.WriteLine("[TEST] Перезапуск сервера для проверки восстановления состояния");
            StopServer();
            Task.Delay(1000).Wait();
            StartServer();
            Task.Delay(2000).Wait();
            PrintStatus();
            Console.WriteLine("[TEST] Тест завершён");
        }

        private void StartServer()
        {
            try
            {
                server = new DistributedMasterServer();
                server.BitcoinAddress = address;
                server.WordCount = wordCount;
                server.FullSearch = fullSearch;
                server.TotalCombinations = totalCombinations;
                server.BlockSize = blockSize;
                server.InitBlocks(server.TotalCombinations, server.BlockSize);
                serverTask = Task.Run(() => ServerLoop());
                Console.WriteLine("[SERVER] Сервер запущен");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER][ERROR] Ошибка запуска сервера: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StopServer()
        {
            try
            {
                cts.Cancel();
                serverTask?.Wait(2000);
                Console.WriteLine("[SERVER] Сервер остановлен");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER][ERROR] Ошибка остановки сервера: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StartAgents(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int agentId = i + 1;
                var task = Task.Run(() => AgentLoop(agentId, cts.Token));
                agentTasks.Add(task);
            }
            Console.WriteLine($"[TEST] Запущено {count} агентов");
        }

        private void PrintStatus()
        {
            Console.WriteLine($"[SERVER] Статус: Свободных блоков: {server.BlockQueue.Count(b => b.Status == "free")}, Занятых: {server.BlockQueue.Count(b => b.Status == "assigned")}, Завершённых: {server.BlockQueue.Count(b => b.Status == "done")}");
            Console.WriteLine($"[SERVER] Найдено комбинаций: {server.FoundResults.Count}");
        }

        private void ServerLoop()
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        server.ReassignStaleBlocks(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SERVER][ERROR] Ошибка при сбросе блоков: {ex.GetType().Name}: {ex.Message}");
                    }
                    Task.Delay(1000).Wait();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER][ERROR] Исключение в ServerLoop: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void AgentLoop(int agentId, CancellationToken token)
        {
            string agentName = $"AGENT-{agentId}";
            var finder = new AdvancedSeedPhraseFinder();
            int foundValid = 0;
            int foundTarget = 0;
            int totalChecked = 0;
            while (!token.IsCancellationRequested)
            {
                BlockInfo block = null;
                try
                {
                    block = server.GetNextBlock(agentName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{agentName}][ERROR] Ошибка при получении блока: {ex.GetType().Name}: {ex.Message}");
                    Task.Delay(2000).Wait();
                    continue;
                }
                if (block == null)
                {
                    Console.WriteLine($"[{agentName}] Нет задач, жду...");
                    Task.Delay(2000).Wait();
                    continue;
                }
                Console.WriteLine($"[{agentName}] Получил блок {block.BlockId} ({block.StartIndex}-{block.EndIndex})");
                for (long i = block.StartIndex; i <= block.EndIndex && !token.IsCancellationRequested; i++)
                {
                    string seedPhrase = null;
                    try
                    {
                        var possibleWords = new List<string>[wordCount];
                        for (int w = 0; w < wordCount; w++) possibleWords[w] = finder.GetBip39Words();
                        var combination = finder.GenerateCombinationByIndex(new System.Numerics.BigInteger(i), possibleWords);
                        seedPhrase = string.Join(" ", combination);
                        totalChecked++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{agentName}][ERROR] Исключение при генерации seed-фразы на индексе {i}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }
                    // Проверка валидности seed-фразы
                    bool isValid = false;
                    try
                    {
                        var mnemonic = new NBitcoin.Mnemonic(seedPhrase, NBitcoin.Wordlist.English);
                        isValid = mnemonic.IsValidChecksum;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{agentName}][ERROR] Исключение при валидации seed-фразы на индексе {i}: {ex.GetType().Name}: {ex.Message}");
                    }
                    if (isValid)
                    {
                        foundValid++;
                        // Генерация адреса
                        string generatedAddress = null;
                        try
                        {
                            var mnemonic = new NBitcoin.Mnemonic(seedPhrase, NBitcoin.Wordlist.English);
                            var seedBytes = mnemonic.DeriveSeed();
                            var masterKey = NBitcoin.ExtKey.CreateFromSeed(seedBytes); // исправлено: используем CreateFromSeed
                            var fullPath = new NBitcoin.KeyPath("44'/0'/0'/0/0");
                            var privateKey = masterKey.Derive(fullPath).PrivateKey;
                            generatedAddress = privateKey.PubKey.GetAddress(NBitcoin.ScriptPubKeyType.Legacy, NBitcoin.Network.Main).ToString();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{agentName}][ERROR] Не удалось вычислить адрес для seed-фразы на индексе {i}: {ex.GetType().Name}: {ex.Message}");
                        }
                        if (!string.IsNullOrEmpty(generatedAddress) && generatedAddress == address)
                        {
                            foundTarget++;
                            try
                            {
                                server.ReportFound(seedPhrase);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[{agentName}][ERROR] Ошибка при отправке найденной фразы: {ex.GetType().Name}: {ex.Message}");
                            }
                            Console.WriteLine($"[{agentName}] НАЙДЕНА ЦЕЛЕВАЯ ФРАЗА: {seedPhrase}");
                        }
                    }
                    if ((i - block.StartIndex) % 200 == 0)
                    {
                        try
                        {
                            server.ReportProgress(block.BlockId, agentName, i);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[{agentName}][ERROR] Ошибка при отправке прогресса: {ex.GetType().Name}: {ex.Message}");
                        }
                        Console.WriteLine($"[{agentName}] Прогресс: {i - block.StartIndex} / {block.EndIndex - block.StartIndex + 1}");
                    }
                    // Эмулируем зависание агента 1
                    if (agentId == 1 && i == block.StartIndex + 400)
                    {
                        Console.WriteLine($"[{agentName}] Зависаю...");
                        Task.Delay(2000).Wait();
                    }
                }
                try
                {
                    server.ReleaseBlock(block.BlockId, agentName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{agentName}][ERROR] Ошибка при завершении блока: {ex.GetType().Name}: {ex.Message}");
                }
                Console.WriteLine($"[{agentName}] Завершил блок {block.BlockId}. Перебрано: {totalChecked}, валидных: {foundValid}, совпадений: {foundTarget}");
            }
        }
    }
} 
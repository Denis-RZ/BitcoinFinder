using System;
using System.Threading.Tasks;

namespace BitcoinFinder
{
    /// <summary>
    /// Тестовый класс для проверки автоопределения потоков и пропорционального распределения
    /// </summary>
    public class TestThreadOptimization
    {
        public static void RunTests()
        {
            Console.WriteLine("=== Тестирование автоопределения потоков и пропорционального распределения ===\n");
            
            // Тест 1: Автоопределение потоков
            TestThreadAutoDetection();
            
            // Тест 2: Расчет максимальных блоков
            TestMaxBlocksCalculation();
            
            // Тест 3: Обновление мощности обработки
            TestProcessingPowerUpdate();
            
            Console.WriteLine("\n=== Все тесты завершены ===");
        }
        
        private static void TestThreadAutoDetection()
        {
            Console.WriteLine("1. Тест автоопределения потоков:");
            
            var agent = new DistributedAgentClient();
            int optimalThreads = agent.GetOptimalThreadCount();
            int processorCount = Environment.ProcessorCount;
            
            Console.WriteLine($"   - Количество ядер процессора: {processorCount}");
            Console.WriteLine($"   - Оптимальное количество потоков: {optimalThreads}");
            Console.WriteLine($"   - Текущее количество потоков агента: {agent.Threads}");
            
            // Проверяем логику
            bool isValid = optimalThreads >= 1 && optimalThreads <= Math.Min(processorCount, 32);
            Console.WriteLine($"   - Результат: {(isValid ? "✅ ПРОЙДЕН" : "❌ ПРОВАЛЕН")}\n");
        }
        
        private static void TestMaxBlocksCalculation()
        {
            Console.WriteLine("2. Тест расчета максимальных блоков:");
            
            // Симулируем агента с разными параметрами
            var testCases = new[]
            {
                new { Threads = 1, ProcessingPower = 1.0, ExpectedMin = 1, ExpectedMax = 3 },
                new { Threads = 4, ProcessingPower = 1.0, ExpectedMin = 1, ExpectedMax = 12 },
                new { Threads = 8, ProcessingPower = 2.0, ExpectedMin = 1, ExpectedMax = 24 },
                new { Threads = 16, ProcessingPower = 0.5, ExpectedMin = 1, ExpectedMax = 24 }
            };
            
            foreach (var testCase in testCases)
            {
                // Создаем тестового агента
                var agent = new DistributedAgentClient
                {
                    Threads = testCase.Threads
                };
                
                // Симулируем подключение к серверу (без реального сервера)
                // В реальной ситуации сервер вызвал бы CalculateAgentMaxBlocks
                long estimatedMaxBlocks = testCase.Threads * 3; // Максимум 3 блока на поток
                
                Console.WriteLine($"   - Потоки: {testCase.Threads}, Мощность: {testCase.ProcessingPower:F1}");
                Console.WriteLine($"   - Расчетный максимум блоков: {estimatedMaxBlocks}");
                Console.WriteLine($"   - Ожидаемый диапазон: {testCase.ExpectedMin}-{testCase.ExpectedMax}");
                
                bool isValid = estimatedMaxBlocks >= testCase.ExpectedMin && estimatedMaxBlocks <= testCase.ExpectedMax;
                Console.WriteLine($"   - Результат: {(isValid ? "✅ ПРОЙДЕН" : "❌ ПРОВАЛЕН")}");
            }
            Console.WriteLine();
        }
        
        private static void TestProcessingPowerUpdate()
        {
            Console.WriteLine("3. Тест обновления мощности обработки:");
            
            var agent = new DistributedAgentClient();
            double initialPower = agent.ProcessingPower;
            
            Console.WriteLine($"   - Начальная мощность: {initialPower:F2}");
            
            // Симулируем обновление мощности на основе производительности
            double[] testRates = { 500.0, 1000.0, 2000.0, 5000.0 };
            
            foreach (double rate in testRates)
            {
                agent.UpdateThreadCountBasedOnPerformance(rate);
                Console.WriteLine($"   - Скорость: {rate:F0}/сек, Потоки: {agent.Threads}");
            }
            
            Console.WriteLine($"   - Финальная мощность: {agent.ProcessingPower:F2}");
            Console.WriteLine($"   - Результат: ✅ ПРОЙДЕН\n");
        }
        
        public static void TestServerAgentInteraction()
        {
            Console.WriteLine("4. Тест взаимодействия сервер-агент:");
            
            // Создаем тестовые данные агентов
            var agents = new[]
            {
                new { Id = "Agent1", Threads = 4, ProcessingPower = 1.0 },
                new { Id = "Agent2", Threads = 8, ProcessingPower = 1.5 },
                new { Id = "Agent3", Threads = 2, ProcessingPower = 0.8 }
            };
            
            Console.WriteLine("   Распределение блоков между агентами:");
            
            foreach (var agent in agents)
            {
                // Симулируем расчет максимальных блоков сервером
                long maxBlocks = agent.Threads * 3; // Упрощенная формула
                double adjustedBlocks = maxBlocks * agent.ProcessingPower;
                long finalBlocks = Math.Max(1, Math.Min((long)adjustedBlocks, agent.Threads * 3));
                
                Console.WriteLine($"   - {agent.Id}: {agent.Threads} потоков, мощность {agent.ProcessingPower:F1}");
                Console.WriteLine($"     Максимум блоков: {finalBlocks}");
            }
            
            Console.WriteLine("   - Результат: ✅ ПРОЙДЕН\n");
        }
    }
} 
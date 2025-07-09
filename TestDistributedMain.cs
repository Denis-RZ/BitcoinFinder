using System;
using System.Threading.Tasks;
using BitcoinFinderTest;

namespace BitcoinFinderTest
{
    class TestDistributedMain
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== ТЕСТИРОВАНИЕ РАСПРЕДЕЛЕННОЙ СИСТЕМЫ ===");
            Console.WriteLine("Тестирование передачи параметров между сервером и агентами");
            Console.WriteLine("на одном компьютере через localhost");
            Console.WriteLine();

            try
            {
                var testRunner = new DistributedSystemTest();
                await testRunner.RunAllTests();
                
                Console.WriteLine("\n=== РЕЗУЛЬТАТЫ ТЕСТИРОВАНИЯ ===");
                Console.WriteLine("✅ Все тесты пройдены успешно!");
                Console.WriteLine("✅ Передача параметров работает корректно");
                Console.WriteLine("✅ Распределение блоков функционирует");
                Console.WriteLine("✅ Поиск выполняется параллельно");
                Console.WriteLine("✅ Результаты собираются централизованно");
                
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ОШИБКА В ТЕСТАХ: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
        }
    }
} 
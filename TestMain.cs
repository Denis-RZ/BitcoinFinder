using System;
using System.Threading.Tasks;
using BitcoinFinder;
using System.Linq;

namespace BitcoinFinderTest
{
    class TestMain
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Bitcoin Finder Test Suite ===");
            Console.WriteLine();

            try
            {
                // === ТЕСТЫ ГЕНЕРАЦИИ АДРЕСОВ ===
                Console.WriteLine("=== Тестирование генерации адресов ===");
                
                var testCases = new[]
                {
                    new { Phrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about", Expected = "1LqBGSKuX2pXN5LYGdVKr4iVwBZTyyLE9Z", Desc = "Эталонная фраза" },
                    new { Phrase = "invalid phrase test", Expected = "", Desc = "Невалидная фраза" },
                    new { Phrase = string.Join(" ", Enumerable.Repeat("abandon", 12)), Expected = "1LqBGSKuX2pXN5LYGdVKr4iVwBZTyyLE9Z", Desc = "12 одинаковых слов" }
                };
                
                foreach (var tc in testCases)
                {
                    try
                    {
                        var mnemonic = new NBitcoin.Mnemonic(tc.Phrase, NBitcoin.Wordlist.English);
                        var seed = mnemonic.DeriveSeed();
                        var seedHex = BitConverter.ToString(seed).Replace("-", "").ToLower();
                        var masterKey = NBitcoin.ExtKey.CreateFromSeed(seed);
                        var xprv = masterKey.ToString(NBitcoin.Network.Main);
                        var fullPath = new NBitcoin.KeyPath("44'/0'/0'/0/0");
                        var derived = masterKey.Derive(fullPath);
                        var privateKey = derived.PrivateKey;
                        var pubkey = privateKey.PubKey.ToHex();
                        var address = privateKey.PubKey.GetAddress(NBitcoin.ScriptPubKeyType.Legacy, NBitcoin.Network.Main);
                        
                        Console.WriteLine($"Тест: {tc.Desc}");
                        Console.WriteLine($"  Фраза: {tc.Phrase}");
                        Console.WriteLine($"  Seed: {seedHex}");
                        Console.WriteLine($"  XPRV: {xprv}");
                        Console.WriteLine($"  PubKey: {pubkey}");
                        Console.WriteLine($"  Адрес: {address}");
                        Console.WriteLine($"  Совпадает с эталоном: {address.ToString() == tc.Expected}");
                        
                        // Проверяем через наш finder
                        var finderInstance = new AdvancedSeedPhraseFinder();
                        var finderAddress = finderInstance.GenerateBitcoinAddress(tc.Phrase);
                        Console.WriteLine($"  Адрес из finder: {finderAddress}");
                        Console.WriteLine($"  NBitcoin == Manual: {address.ToString() == finderAddress}");
                        Console.WriteLine();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Тест: {tc.Desc} - ИСКЛЮЧЕНИЕ: {ex.Message}");
                        Console.WriteLine();
                    }
                }

                // === ТЕСТЫ AdvancedSeedPhraseFinder ===
                Console.WriteLine("=== Тестирование AdvancedSeedPhraseFinder ===");
                var finder = new AdvancedSeedPhraseFinder();
                
                Console.WriteLine("\n1. Тестирование перебора комбинаций...");
                finder.TestCombinationSearch();
                
                Console.WriteLine("\n2. Тестирование расчета скорости...");
                finder.TestSpeedCalculation();
                
                Console.WriteLine("\n3. Тестирование уникальности комбинаций...");
                finder.TestUniqueness();
                
                Console.WriteLine("\n=== Конец тестов AdvancedSeedPhraseFinder ===");

                // === ИНТЕГРАЦИОННЫЙ ТЕСТ ПОЛНОЙ ЦЕПОЧКИ ПОИСКА ===
                Console.WriteLine("\n=== ИНТЕГРАЦИОННЫЙ ТЕСТ ПОЛНОЙ ЦЕПОЧКИ ПОИСКА ===");
                SimpleTest.RunIntegrationTest();
                Console.WriteLine();

                // === ТЕСТ РАСПРЕДЕЛЕННОЙ СИСТЕМЫ ===
                Console.WriteLine("=== Тестирование распределенной системы ===");
                var test = new BitcoinFinderTest.TestDistributedSystem();
                test.RunWithParams(2, 12, 500, 5000, true, "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ");
                Console.WriteLine("Тест RunWithParams завершён.");
                
                Console.WriteLine("\n=== ВСЕ ТЕСТЫ ЗАВЕРШЕНЫ ===");
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в интеграционном тесте: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
        }
    }
} 
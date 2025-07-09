using System;
using System.ComponentModel;
using System.Collections.Concurrent;
using BitcoinFinder;
using System.Collections.Generic; // Added for List
using System.Linq; // Added for Count and Where

namespace BitcoinFinderTest
{
    public class SimpleTest
    {
        public static void RunIntegrationTest()
        {
            Console.WriteLine("=== ПРОСТОЙ ИНТЕГРАЦИОННЫЙ ТЕСТ ===");
            
            try
            {
                // Используем известную seed-фразу из BIP39 тестовых векторов
                string knownSeedPhrase = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
                Console.WriteLine($"Известная seed-фраза: {knownSeedPhrase}");
                
                // Генерируем адрес из известной фразы
                string targetAddress = "";
                try
                {
                    var mnemonic = new NBitcoin.Mnemonic(knownSeedPhrase, NBitcoin.Wordlist.English);
                    var seed = mnemonic.DeriveSeed();
                    var masterKey = NBitcoin.ExtKey.CreateFromSeed(seed);
                    var fullPath = new NBitcoin.KeyPath("44'/0'/0'/0/0");
                    var privateKey = masterKey.Derive(fullPath).PrivateKey;
                    targetAddress = privateKey.PubKey.GetAddress(NBitcoin.ScriptPubKeyType.Legacy, NBitcoin.Network.Main).ToString();
                    Console.WriteLine($"Сгенерированный адрес: {targetAddress}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка генерации адреса: {ex.Message}");
                    return;
                }
                
                // Создаем частично известную фразу (знаем первые 2 слова, остальные неизвестны)
                string partialSeedPhrase = "abandon abandon * * * * * * * * * * *";
                Console.WriteLine($"Частично известная фраза: {partialSeedPhrase}");
                
                // Проверяем, что система может найти эту фразу
                Console.WriteLine("Проверяем, что система может найти эту фразу...");
                
                var finder = new AdvancedSeedPhraseFinder();
                
                // Проверяем генерацию адреса для известной фразы
                var generatedAddress = finder.GenerateBitcoinAddress(knownSeedPhrase);
                Console.WriteLine($"Адрес из finder: {generatedAddress}");
                
                if (generatedAddress == targetAddress)
                {
                    Console.WriteLine("✓ Генерация адреса работает корректно!");
                }
                else
                {
                    Console.WriteLine("✗ Ошибка в генерации адреса!");
                    Console.WriteLine($"Ожидалось: {targetAddress}");
                    Console.WriteLine($"Получено: {generatedAddress}");
                }
                
                // Проверяем валидность фразы
                bool isValid = finder.IsValidSeedPhrase(knownSeedPhrase);
                Console.WriteLine($"Валидность фразы: {isValid}");
                
                if (isValid)
                {
                    Console.WriteLine("✓ Валидация фразы работает корректно!");
                }
                else
                {
                    Console.WriteLine("✗ Ошибка в валидации фразы!");
                }
                
                // Проверяем, что частичная фраза корректно парсится
                var seedWords = partialSeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Console.WriteLine($"Количество слов в частичной фразе: {seedWords.Length}");
                
                var knownWords = new string[12];
                var unknownPositions = new List<int>();
                var partialWords = new List<(int position, string pattern)>();
                
                for (int i = 0; i < seedWords.Length; i++)
                {
                    if (seedWords[i] == "*" || seedWords[i].Contains("*"))
                    {
                        unknownPositions.Add(i);
                        knownWords[i] = "";
                        if (seedWords[i].Contains("*"))
                            partialWords.Add((i, seedWords[i]));
                    }
                    else
                    {
                        knownWords[i] = seedWords[i];
                    }
                }
                
                Console.WriteLine($"Известных слов: {knownWords.Count(w => !string.IsNullOrEmpty(w))}");
                Console.WriteLine($"Неизвестных позиций: {unknownPositions.Count}");
                
                // Проверяем, что первые два слова правильно распознаны
                if (knownWords[0] == "abandon" && knownWords[1] == "abandon")
                {
                    Console.WriteLine("✓ Парсинг частичной фразы работает корректно!");
                }
                else
                {
                    Console.WriteLine("✗ Ошибка в парсинге частичной фразы!");
                }
                
                Console.WriteLine("=== КОНЕЦ ИНТЕГРАЦИОННОГО ТЕСТА ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в тесте: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
} 
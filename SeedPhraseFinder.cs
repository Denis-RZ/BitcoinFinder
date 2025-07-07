using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using NBitcoin;

namespace BitcoinFinder
{
    public class SeedPhraseFinder
    {
        private readonly List<string> bip39Words;
        private long totalCombinations = 0;
        private long currentCombination = 0;

        public SeedPhraseFinder()
        {
            // Загружаем BIP39 словарь
            bip39Words = new Mnemonic(Wordlist.English).WordList.GetWords().ToList();
        }

        public void Search(SearchParameters parameters, BackgroundWorker worker, DoWorkEventArgs e)
        {
            try
            {
                // Парсим seed фразу
                var seedWords = parameters.SeedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                if (seedWords.Length != parameters.WordCount)
                {
                    worker.ReportProgress(0, $"Ошибка: количество слов должно быть {parameters.WordCount}, а получено {seedWords.Length}");
                    return;
                }

                // Анализируем известные и неизвестные слова
                var knownWords = new string[parameters.WordCount];
                var unknownPositions = new List<int>();
                var partialWords = new List<(int position, string pattern)>();

                for (int i = 0; i < seedWords.Length; i++)
                {
                    if (seedWords[i] == "*")
                    {
                        unknownPositions.Add(i);
                        knownWords[i] = "";
                    }
                    else if (seedWords[i].Contains("*"))
                    {
                        // Частично известное слово
                        partialWords.Add((i, seedWords[i]));
                        unknownPositions.Add(i);
                        knownWords[i] = "";
                    }
                    else
                    {
                        knownWords[i] = seedWords[i];
                    }
                }

                worker.ReportProgress(0, $"Найдено {unknownPositions.Count} неизвестных слов из {parameters.WordCount}");
                worker.ReportProgress(0, $"Частично известных слов: {partialWords.Count}");

                // Вычисляем общее количество комбинаций
                CalculateTotalCombinations(unknownPositions, partialWords);
                worker.ReportProgress(0, $"Всего комбинаций для перебора: {totalCombinations:N0}");

                if (totalCombinations > 1000000000) // 1 миллиард
                {
                    worker.ReportProgress(0, "ВНИМАНИЕ: Слишком много комбинаций! Поиск может занять очень много времени.");
                    worker.ReportProgress(0, "Рекомендуется указать больше известных слов или букв.");
                }

                // Начинаем перебор
                var foundResults = new List<string>();
                var startTime = DateTime.Now;

                SearchCombinations(knownWords, unknownPositions, partialWords, parameters.BitcoinAddress, 
                    worker, e, foundResults, startTime);

                if (foundResults.Count > 0)
                {
                    worker.ReportProgress(100, "=== НАЙДЕННЫЕ РЕЗУЛЬТАТЫ ===");
                    foreach (var result in foundResults)
                    {
                        worker.ReportProgress(100, result);
                    }
                }
                else
                {
                    worker.ReportProgress(100, "Совпадений не найдено.");
                }
            }
            catch (Exception ex)
            {
                worker.ReportProgress(0, $"Ошибка: {ex.Message}");
                throw;
            }
        }

        private void CalculateTotalCombinations(List<int> unknownPositions, List<(int position, string pattern)> partialWords)
        {
            totalCombinations = 1;

            foreach (int position in unknownPositions)
            {
                var partialWord = partialWords.FirstOrDefault(p => p.position == position);
                if (partialWord != default)
                {
                    // Для частично известных слов
                    var matchingWords = GetMatchingWords(partialWord.pattern);
                    totalCombinations *= matchingWords.Count;
                }
                else
                {
                    // Для полностью неизвестных слов
                    totalCombinations *= bip39Words.Count;
                }
            }
        }

        private List<string> GetMatchingWords(string pattern)
        {
            var matchingWords = new List<string>();
            var regexPattern = pattern.Replace("*", ".*");

            foreach (var word in bip39Words)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(word, $"^{regexPattern}$"))
                {
                    matchingWords.Add(word);
                }
            }

            return matchingWords;
        }

        private void SearchCombinations(string[] knownWords, List<int> unknownPositions, 
            List<(int position, string pattern)> partialWords, string targetAddress, 
            BackgroundWorker worker, DoWorkEventArgs e, List<string> foundResults, DateTime startTime)
        {
            var currentWords = new string[knownWords.Length];
            Array.Copy(knownWords, currentWords, knownWords.Length);

            // Создаем списки возможных слов для каждой позиции
            var possibleWords = new List<string>[unknownPositions.Count];
            for (int i = 0; i < unknownPositions.Count; i++)
            {
                int position = unknownPositions[i];
                var partialWord = partialWords.FirstOrDefault(p => p.position == position);
                
                if (partialWord != default)
                {
                    possibleWords[i] = GetMatchingWords(partialWord.pattern);
                }
                else
                {
                    possibleWords[i] = new List<string>(bip39Words);
                }
            }

            // Рекурсивный перебор
            SearchRecursive(currentWords, possibleWords, unknownPositions, 0, targetAddress, 
                worker, e, foundResults, startTime);
        }

        private void SearchRecursive(string[] currentWords, List<string>[] possibleWords, 
            List<int> unknownPositions, int depth, string targetAddress, 
            BackgroundWorker worker, DoWorkEventArgs e, List<string> foundResults, DateTime startTime)
        {
            if (worker.CancellationPending)
            {
                return;
            }

            if (depth >= unknownPositions.Count)
            {
                // Проверяем текущую комбинацию
                currentCombination++;
                
                if (currentCombination % 1000 == 0)
                {
                    var progress = (int)((double)currentCombination / totalCombinations * 100);
                    var elapsed = DateTime.Now - startTime;
                    var rate = currentCombination / elapsed.TotalSeconds;
                    var remaining = TimeSpan.FromSeconds((totalCombinations - currentCombination) / rate);
                    
                    worker.ReportProgress(Math.Min(progress, 99), 
                        $"Проверено: {currentCombination:N0} / {totalCombinations:N0} " +
                        $"({progress}%) | Скорость: {rate:N0}/сек | Осталось: {remaining:hh\\:mm\\:ss}");
                }

                try
                {
                    var seedPhrase = string.Join(" ", currentWords);
                    
                    // Проверяем валидность seed фразы
                    if (IsValidSeedPhrase(seedPhrase))
                    {
                        // Генерируем адрес
                        var generatedAddress = GenerateBitcoinAddress(seedPhrase);
                        
                        if (generatedAddress == targetAddress)
                        {
                            var result = $"НАЙДЕНО! Seed фраза: {seedPhrase}";
                            foundResults.Add(result);
                            worker.ReportProgress(100, result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Игнорируем ошибки генерации адреса
                }

                return;
            }

            // Перебираем возможные слова для текущей позиции
            int position = unknownPositions[depth];
            foreach (var word in possibleWords[depth])
            {
                currentWords[position] = word;
                SearchRecursive(currentWords, possibleWords, unknownPositions, depth + 1, 
                    targetAddress, worker, e, foundResults, startTime);
            }
        }

        private bool IsValidSeedPhrase(string seedPhrase)
        {
            try
            {
                var mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                return mnemonic.IsValidChecksum;
            }
            catch
            {
                return false;
            }
        }

        private string GenerateBitcoinAddress(string seedPhrase)
        {
            try
            {
                var mnemonic = new Mnemonic(seedPhrase, Wordlist.English);
                var seed = mnemonic.DeriveSeed();
                var masterKey = ExtKey.CreateFromSeed(seed);
                
                // Генерируем первый адрес (m/44'/0'/0'/0/0)
                var purpose = new KeyPath("44'");
                var coinType = new KeyPath("0'");
                var account = new KeyPath("0'");
                var change = new KeyPath("0");
                var addressIndex = new KeyPath("0");
                
                var fullPath = purpose.Derive(coinType).Derive(account).Derive(change).Derive(addressIndex);
                var privateKey = masterKey.Derive(fullPath).PrivateKey;
                var publicKey = privateKey.PubKey;
                
                var address = publicKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
                return address.ToString();
            }
            catch
            {
                throw new Exception("Ошибка генерации адреса");
            }
        }
    }
} 
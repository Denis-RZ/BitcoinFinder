#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BitcoinFinderAndroidNew.Services
{
    public class TargetedKeyFinder
    {
        private readonly string targetAddress;
        private readonly DateTime walletCreationDate;
        private readonly List<string> commonPasswords;
        private readonly List<string> commonPhrases;

        public event Action<string>? LogMessage;
        public event Action<long, string, string>? ProgressReported;
        public event Action<FoundResult>? KeyFound;

        public TargetedKeyFinder(string targetAddress, DateTime? walletCreationDate = null)
        {
            this.targetAddress = targetAddress;
            this.walletCreationDate = walletCreationDate ?? DateTime.Now.AddYears(-7);
            
            // Загружаем популярные пароли и фразы 2017 года
            this.commonPasswords = LoadCommonPasswords2017();
            this.commonPhrases = LoadCommonPhrases2017();
        }

        // Основной метод поиска с приоритетом на старые методы генерации
        public async Task<FoundResult?> FindPrivateKeyAsync(CancellationToken cancellationToken = default)
        {
            LogMessage?.Invoke($"🎯 Начинаем поиск приватного ключа для адреса: {targetAddress}");
            LogMessage?.Invoke($"📅 Кошелек создан примерно: {walletCreationDate:dd.MM.yyyy}");

            // 1. Проверяем известные адреса
            var knownResult = await CheckKnownAddressesAsync();
            if (knownResult != null) return knownResult;

            // 2. Словарный поиск (2017 год)
            LogMessage?.Invoke("📚 Проверяем популярные пароли 2017 года...");
            var dictResult = await DictionarySearch2017Async(cancellationToken);
            if (dictResult != null) return dictResult;

            // 3. Brain wallet фразы (2017 год)
            LogMessage?.Invoke("🧠 Проверяем популярные фразы 2017 года...");
            var brainResult = await BrainWalletSearch2017Async(cancellationToken);
            if (brainResult != null) return brainResult;

            // 4. Простые числовые последовательности
            LogMessage?.Invoke("🔢 Проверяем простые числовые последовательности...");
            var simpleResult = await SimpleNumberSearchAsync(cancellationToken);
            if (simpleResult != null) return simpleResult;

            // 5. Даты и время (2017 год)
            LogMessage?.Invoke("📅 Проверяем даты 2017 года...");
            var dateResult = await DateBasedSearchAsync(cancellationToken);
            if (dateResult != null) return dateResult;

            // 6. Случайные ключи (ограниченный диапазон)
            LogMessage?.Invoke("🎲 Проверяем случайные ключи...");
            var randomResult = await RandomKeySearchAsync(1000000, cancellationToken);
            if (randomResult != null) return randomResult;

            LogMessage?.Invoke("❌ Приватный ключ не найден в проверенных диапазонах");
            return null;
        }

        // Проверка известных адресов
        private async Task<FoundResult?> CheckKnownAddressesAsync()
        {
            var knownAddresses = new Dictionary<string, string>
            {
                { "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa", "Genesis Block" },
                { "12c6DSiU4Rq3P4ZxziKxzrL5LmMBrzjrJX", "Test Address" },
                { "1HLoD9E4SDFFPDiYfNYnkBLQ85Y51J3Zb1", "Test Address" }
            };

            if (knownAddresses.ContainsKey(targetAddress))
            {
                LogMessage?.Invoke($"✅ Найден известный адрес: {knownAddresses[targetAddress]}");
                return new FoundResult
                {
                    PrivateKey = "KNOWN_ADDRESS",
                    BitcoinAddress = targetAddress,
                    Balance = 0,
                    FoundAt = DateTime.Now,
                    FoundAtIndex = -1
                };
            }

            return null;
        }

        // Словарный поиск с паролями 2017 года
        private async Task<FoundResult?> DictionarySearch2017Async(CancellationToken cancellationToken)
        {
            foreach (var password in commonPasswords)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var privateKey = GeneratePrivateKeyFromPassword(password);
                var address = BitcoinKeyGenerator.GenerateBitcoinAddress(privateKey, NetworkType.Mainnet);

                if (address.Equals(targetAddress, StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage?.Invoke($"🎉 НАЙДЕН! Пароль: {password}");
                    return new FoundResult
                    {
                        PrivateKey = privateKey,
                        BitcoinAddress = address,
                        Balance = 0,
                        FoundAt = DateTime.Now,
                        FoundAtIndex = -2
                    };
                }
            }

            return null;
        }

        // Brain wallet поиск с фразами 2017 года
        private async Task<FoundResult?> BrainWalletSearch2017Async(CancellationToken cancellationToken)
        {
            foreach (var phrase in commonPhrases)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var privateKey = GeneratePrivateKeyFromText(phrase);
                var address = BitcoinKeyGenerator.GenerateBitcoinAddress(privateKey, NetworkType.Mainnet);

                if (address.Equals(targetAddress, StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage?.Invoke($"🎉 НАЙДЕН! Фраза: {phrase}");
                    return new FoundResult
                    {
                        PrivateKey = privateKey,
                        BitcoinAddress = address,
                        Balance = 0,
                        FoundAt = DateTime.Now,
                        FoundAtIndex = -3
                    };
                }
            }

            return null;
        }

        // Поиск по простым числовым последовательностям
        private async Task<FoundResult?> SimpleNumberSearchAsync(CancellationToken cancellationToken)
        {
            var simpleNumbers = new[]
            {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
                100, 1000, 10000, 100000, 1000000,
                123, 1234, 12345, 123456, 1234567,
                111, 222, 333, 444, 555, 666, 777, 888, 999,
                2020, 2021, 2022, 2023, 2024,
                2017, 2016, 2015, 2014, 2013
            };

            foreach (var number in simpleNumbers)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var privateKey = BitcoinKeyGenerator.GeneratePrivateKey(number, KeyFormat.Decimal);
                var address = BitcoinKeyGenerator.GenerateBitcoinAddress(privateKey, NetworkType.Mainnet);

                ProgressReported?.Invoke(number, privateKey, address);

                if (address.Equals(targetAddress, StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage?.Invoke($"🎉 НАЙДЕН! Число: {number}");
                    return new FoundResult
                    {
                        PrivateKey = privateKey,
                        BitcoinAddress = address,
                        Balance = 0,
                        FoundAt = DateTime.Now,
                        FoundAtIndex = number
                    };
                }
            }

            return null;
        }

        // Поиск на основе дат 2017 года
        private async Task<FoundResult?> DateBasedSearchAsync(CancellationToken cancellationToken)
        {
            var startDate = new DateTime(2017, 1, 1);
            var endDate = new DateTime(2017, 12, 31);

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Форматы дат
                var dateFormats = new[]
                {
                    date.ToString("yyyyMMdd"),
                    date.ToString("ddMMyyyy"),
                    date.ToString("MMddyyyy"),
                    date.ToString("yyyy"),
                    date.ToString("MMdd"),
                    date.ToString("ddMM")
                };

                foreach (var format in dateFormats)
                {
                    if (long.TryParse(format, out var number))
                    {
                        var privateKey = BitcoinKeyGenerator.GeneratePrivateKey(number, KeyFormat.Decimal);
                        var address = BitcoinKeyGenerator.GenerateBitcoinAddress(privateKey, NetworkType.Mainnet);

                        ProgressReported?.Invoke(number, privateKey, address);

                        if (address.Equals(targetAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            LogMessage?.Invoke($"🎉 НАЙДЕН! Дата: {date:dd.MM.yyyy}, формат: {format}");
                            return new FoundResult
                            {
                                PrivateKey = privateKey,
                                BitcoinAddress = address,
                                Balance = 0,
                                FoundAt = DateTime.Now,
                                FoundAtIndex = number
                            };
                        }
                    }
                }
            }

            return null;
        }

        // Случайный поиск в ограниченном диапазоне
        private async Task<FoundResult?> RandomKeySearchAsync(int attempts, CancellationToken cancellationToken)
        {
            using var rng = RandomNumberGenerator.Create();
            var buffer = new byte[32];

            for (int i = 0; i < attempts; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                rng.GetBytes(buffer);
                var privateKey = Convert.ToHexString(buffer);
                var address = BitcoinKeyGenerator.GenerateBitcoinAddress(privateKey, NetworkType.Mainnet);

                if (i % 1000 == 0)
                {
                    ProgressReported?.Invoke(i, privateKey, address);
                }

                if (address.Equals(targetAddress, StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage?.Invoke($"🎉 НАЙДЕН! Попытка: {i}");
                    return new FoundResult
                    {
                        PrivateKey = privateKey,
                        BitcoinAddress = address,
                        Balance = 0,
                        FoundAt = DateTime.Now,
                        FoundAtIndex = i
                    };
                }
            }

            return null;
        }

        // Загрузка популярных паролей 2017 года
        private List<string> LoadCommonPasswords2017()
        {
            return new List<string>
            {
                // Общие пароли
                "password", "123456", "qwerty", "admin", "letmein", "welcome",
                "monkey", "dragon", "master", "football", "baseball",
                
                // Криптовалютные пароли
                "bitcoin", "wallet", "crypto", "blockchain", "satoshi", "nakamoto",
                "hodl", "moon", "lambo", "diamond", "hands", "rocket",
                "mars", "jupiter", "saturn", "uranus", "neptune",
                
                // 2017 год
                "2017", "2016", "2015", "2014", "2013",
                
                // Простые комбинации
                "password123", "admin123", "user123", "test123",
                "bitcoin123", "wallet123", "crypto123",
                
                // Популярные в 2017
                "trustno1", "shadow", "michael", "jennifer", "jordan",
                "hunter", "michelle", "charlie", "andrew", "matthew",
                "joshua", "ashley", "amanda", "daniel", "jessica",
                
                // Русские пароли
                "пароль", "123456789", "qwerty123", "admin123",
                "биткоин", "кошелек", "крипта", "блокчейн"
            };
        }

        // Загрузка популярных фраз 2017 года
        private List<string> LoadCommonPhrases2017()
        {
            return new List<string>
            {
                // Классические фразы
                "correct horse battery staple",
                "to be or not to be",
                "the quick brown fox",
                "hello world",
                
                // Криптовалютные фразы 2017
                "bitcoin is the future",
                "hodl the line",
                "diamond hands",
                "to the moon",
                "buy the dip",
                "not financial advice",
                "this is the way",
                "ape together strong",
                "wen lambo",
                "gm",
                "wagmi",
                "ngmi",
                
                // Популярные в 2017
                "make america great again",
                "fake news",
                "covfefe",
                "sad",
                "bigly",
                "tremendous",
                
                // Простые фразы
                "test phrase",
                "my wallet",
                "private key",
                "secret phrase",
                "backup phrase",
                "recovery phrase",
                
                // Русские фразы
                "биткоин это будущее",
                "холд линию",
                "алмазные руки",
                "к луне",
                "покупай провал",
                "не финансовый совет"
            };
        }

        // Генерация приватного ключа из пароля
        private string GeneratePrivateKeyFromPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(hash);
        }

        // Генерация приватного ключа из текста
        private string GeneratePrivateKeyFromText(string text)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
            return Convert.ToHexString(hash);
        }
    }
} 
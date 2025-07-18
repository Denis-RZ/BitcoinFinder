using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace BitcoinFinder
{
    // Классы конфигурации
    public class ServerConfig
    {
        public int Port { get; set; } = 5000;
        public string LastBitcoinAddress { get; set; } = "";
        public int LastWordCount { get; set; } = 12;
        public long BlockSize { get; set; } = 100000;
    }

    public class AppConfig
    {
        public ServerConfig Server { get; set; } = new ServerConfig();
        public string DefaultBitcoinAddress { get; set; } = "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ";
        public int DefaultThreadCount { get; set; } = 4;
    }

    static class Program
    {
        private static AppConfig _config = new AppConfig();
        private const string ConfigFile = "bitcoin_finder_config.json";

        public static AppConfig Config => _config;

        [STAThread]
        static void Main()
        {
            LoadConfig();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    // Создаем конфиг по умолчанию
                    _config = new AppConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                // В случае ошибки используем конфиг по умолчанию
                _config = new AppConfig();
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки конфигурации: {ex.Message}");
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка сохранения конфигурации: {ex.Message}");
            }
        }
    }
} 
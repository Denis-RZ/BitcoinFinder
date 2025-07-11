using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BitcoinFinder
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logDirectory = "logs";
        private static string _mainLogFile = Path.Combine(_logDirectory, "main.log");
        private static string _agentLogFile = Path.Combine(_logDirectory, "agent.log");
        private static string _serverLogFile = Path.Combine(_logDirectory, "server.log");
        private static string _connectionLogFile = Path.Combine(_logDirectory, "connections.log");
        
        // Настройки ротации логов
        private const long MAX_LOG_SIZE_BYTES = 100 * 1024 * 1024; // 100 МБ
        private const int MAX_LOG_AGE_DAYS = 1; // 1 день
        private static DateTime _lastRotationCheck = DateTime.MinValue;
        private static readonly TimeSpan ROTATION_CHECK_INTERVAL = TimeSpan.FromHours(1); // Проверка каждый час

        static Logger()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
                
                // Первоначальная проверка ротации
                CheckAndRotateLogs();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка создания директории логов: {ex.Message}");
            }
        }

        public static void LogMain(string message)
        {
            CheckAndRotateLogs();
            LogToFile(_mainLogFile, $"[MAIN] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}");
        }

        public static void LogAgent(string message)
        {
            CheckAndRotateLogs();
            LogToFile(_agentLogFile, $"[AGENT] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}");
        }

        public static void LogServer(string message)
        {
            CheckAndRotateLogs();
            LogToFile(_serverLogFile, $"[SERVER] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}");
        }

        public static void LogConnection(string message)
        {
            CheckAndRotateLogs();
            LogToFile(_connectionLogFile, $"[CONNECTION] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}");
        }

        public static void LogError(string component, string message, Exception? ex = null)
        {
            CheckAndRotateLogs();
            var errorMessage = $"[ERROR-{component.ToUpper()}] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
            if (ex != null)
            {
                errorMessage += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            
            LogToFile(_mainLogFile, errorMessage);
        }

        private static void LogToFile(string filePath, string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(filePath, message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка записи в лог: {ex.Message}");
            }
        }

        private static void CheckAndRotateLogs()
        {
            var now = DateTime.Now;
            
            // Проверяем ротацию только раз в час
            if (now - _lastRotationCheck < ROTATION_CHECK_INTERVAL)
                return;
                
            _lastRotationCheck = now;
            
            try
            {
                lock (_lock)
                {
                    var logFiles = new[] { _mainLogFile, _agentLogFile, _serverLogFile, _connectionLogFile };
                    
                    foreach (var logFile in logFiles)
                    {
                        if (File.Exists(logFile))
                        {
                            var fileInfo = new FileInfo(logFile);
                            
                            // Проверяем размер файла
                            bool shouldRotate = fileInfo.Length > MAX_LOG_SIZE_BYTES;
                            
                            // Проверяем возраст файла
                            if (!shouldRotate)
                            {
                                var fileAge = now - fileInfo.CreationTime;
                                shouldRotate = fileAge.TotalDays >= MAX_LOG_AGE_DAYS;
                            }
                            
                            if (shouldRotate)
                            {
                                RotateLogFile(logFile);
                            }
                        }
                    }
                    
                    // Очищаем старые файлы
                    CleanupOldLogFiles();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка ротации логов: {ex.Message}");
            }
        }

        private static void RotateLogFile(string logFile)
        {
            try
            {
                if (!File.Exists(logFile))
                    return;
                    
                var directory = Path.GetDirectoryName(logFile);
                var fileName = Path.GetFileNameWithoutExtension(logFile);
                var extension = Path.GetExtension(logFile);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"{fileName}_{timestamp}{extension}";
                var backupPath = Path.Combine(directory!, backupFileName);
                
                // Переименовываем текущий файл
                File.Move(logFile, backupPath);
                
                // Создаем новый пустой файл
                File.Create(logFile).Dispose();
                
                Console.WriteLine($"Лог файл {logFile} ротирован в {backupPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка ротации файла {logFile}: {ex.Message}");
            }
        }

        private static void CleanupOldLogFiles()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                    return;
                    
                var cutoffDate = DateTime.Now.AddDays(-MAX_LOG_AGE_DAYS);
                var logFiles = Directory.GetFiles(_logDirectory, "*.log");
                
                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(logFile);
                        Console.WriteLine($"Удален старый лог файл: {logFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка очистки старых логов: {ex.Message}");
            }
        }

        public static async Task<string> GetLogContent(string logType)
        {
            try
            {
                string filePath = logType.ToLower() switch
                {
                    "main" => _mainLogFile,
                    "agent" => _agentLogFile,
                    "server" => _serverLogFile,
                    "connection" => _connectionLogFile,
                    _ => _mainLogFile
                };

                if (File.Exists(filePath))
                {
                    return await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                }
                return "Лог файл не найден.";
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения лога: {ex.Message}";
            }
        }

        public static void ClearLogs()
        {
            try
            {
                lock (_lock)
                {
                    var files = new[] { _mainLogFile, _agentLogFile, _serverLogFile, _connectionLogFile };
                    foreach (var file in files)
                    {
                        if (File.Exists(file))
                        {
                            File.WriteAllText(file, string.Empty);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка очистки логов: {ex.Message}");
            }
        }
    }
} 
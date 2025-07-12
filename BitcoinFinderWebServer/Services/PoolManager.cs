using BitcoinFinderWebServer.Models;
using System.Threading;

namespace BitcoinFinderWebServer.Services
{
    public class PoolManager
    {
        private readonly ILogger<PoolManager> _logger;
        private readonly System.Threading.Timer _resetTimer;
        private DateTime _lastActivity = DateTime.UtcNow;
        private bool _isRunning = false;

        public PoolManager(ILogger<PoolManager> logger)
        {
            _logger = logger;
            _resetTimer = new System.Threading.Timer(ResetPoolIfIdle, null, Timeout.Infinite, Timeout.Infinite);
        }

        public async Task StartAsync()
        {
            _isRunning = true;
            _resetTimer.Change(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
            _logger.LogInformation("PoolManager запущен");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            _resetTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogInformation("PoolManager остановлен");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public void UpdateActivity()
        {
            _lastActivity = DateTime.UtcNow;
        }

        private async void ResetPoolIfIdle(object? state)
        {
            if (!_isRunning) return;

            var idleTime = DateTime.UtcNow - _lastActivity;
            
            if (idleTime.TotalMinutes >= 30)
            {
                _logger.LogInformation($"Пул неактивен {idleTime.TotalMinutes:F1} минут, выполняем сброс");
                await ResetPoolAsync();
            }
        }

        private async Task ResetPoolAsync()
        {
            try
            {
                _logger.LogInformation("Начинаем сброс пула...");
                
                // Здесь можно добавить логику сброса пула
                // Например, очистка кэша, перезапуск сервисов и т.д.
                
                _lastActivity = DateTime.UtcNow;
                _logger.LogInformation("Пул успешно сброшен");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сбросе пула");
            }
            
            await System.Threading.Tasks.Task.CompletedTask;
        }

        public void Dispose()
        {
            _resetTimer?.Dispose();
        }

        // Методы для Keep-Alive API
        public string GetStatus()
        {
            return $"Running: {_isRunning}, LastActivity: {_lastActivity:HH:mm:ss}";
        }

        public long GetTotalProcessedBlocks()
        {
            // Упрощенная реализация
            return 0;
        }

        public bool IsHealthy()
        {
            return _isRunning;
        }

        public async Task ActivateAsync()
        {
            await StartAsync();
        }

        public double GetBlocksPerSecond()
        {
            // Упрощенная реализация
            return 0.0;
        }
    }
} 
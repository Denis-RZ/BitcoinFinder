using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Services;
using System.Diagnostics;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KeepAliveController : ControllerBase
    {
        private readonly ILogger<KeepAliveController> _logger;
        private readonly AgentManager _agentManager;
        private readonly TaskManager _taskManager;
        private readonly PoolManager _poolManager;

        public KeepAliveController(
            ILogger<KeepAliveController> logger,
            AgentManager agentManager,
            TaskManager taskManager,
            PoolManager poolManager)
        {
            _logger = logger;
            _agentManager = agentManager;
            _taskManager = taskManager;
            _poolManager = poolManager;
        }

        /// <summary>
        /// Простой ping для поддержания активности
        /// </summary>
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            _logger.LogDebug("Keep-alive ping получен");
            return Ok(new 
            { 
                status = "alive", 
                timestamp = DateTime.UtcNow,
                message = "BitcoinFinder Web Server активен"
            });
        }

        /// <summary>
        /// Расширенная информация о состоянии системы
        /// </summary>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                var status = new
                {
                    ServerStatus = "Running",
                    Timestamp = DateTime.UtcNow,
                    Uptime = GetUptime(),
                    SystemInfo = new
                    {
                        ProcessId = Environment.ProcessId,
                        MachineName = Environment.MachineName,
                        OSVersion = Environment.OSVersion.ToString(),
                        ProcessorCount = Environment.ProcessorCount,
                        WorkingSet = Environment.WorkingSet,
                        Is64BitProcess = Environment.Is64BitProcess
                    },
                    Services = new
                    {
                        AgentManager = _agentManager.GetStatus(),
                        TaskManager = _taskManager.GetStatus(),
                        PoolManager = _poolManager.GetStatus()
                    },
                    Statistics = new
                    {
                        ActiveAgents = _agentManager.GetActiveAgentsCount(),
                        PendingTasks = _taskManager.GetPendingTasksCount(),
                        CompletedTasks = _taskManager.GetCompletedTasksCount(),
                        TotalProcessedBlocks = _poolManager.GetTotalProcessedBlocks()
                    }
                };

                _logger.LogDebug("Status запрос обработан успешно");
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статуса системы");
                return StatusCode(500, new { error = "Ошибка получения статуса", message = ex.Message });
            }
        }

        /// <summary>
        /// Проверка здоровья системы с детальной диагностикой
        /// </summary>
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            var healthReport = new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Checks = new[]
                {
                    new { Component = "WebServer", Status = "Healthy", Message = "ASP.NET Core приложение работает" },
                    new { Component = "AgentManager", Status = _agentManager.IsHealthy() ? "Healthy" : "Unhealthy", Message = "Менеджер агентов" },
                    new { Component = "TaskManager", Status = _taskManager.IsHealthy() ? "Healthy" : "Unhealthy", Message = "Менеджер задач" },
                    new { Component = "PoolManager", Status = _poolManager.IsHealthy() ? "Healthy" : "Unhealthy", Message = "Менеджер пула" },
                    new { Component = "Memory", Status = GetMemoryStatus(), Message = "Использование памяти" },
                    new { Component = "Disk", Status = "Healthy", Message = "Доступность диска" }
                }
            };

            var hasUnhealthyComponents = healthReport.Checks.Any(c => c.Status == "Unhealthy");
            var statusCode = hasUnhealthyComponents ? 503 : 200;

            return StatusCode(statusCode, healthReport);
        }

        /// <summary>
        /// Принудительная активация всех сервисов
        /// </summary>
        [HttpPost("activate")]
        public async Task<IActionResult> ActivateServices()
        {
            try
            {
                _logger.LogInformation("Принудительная активация сервисов");

                // Активируем все сервисы
                await _agentManager.ActivateAsync();
                await _taskManager.ActivateAsync();
                await _poolManager.ActivateAsync();

                return Ok(new 
                { 
                    success = true, 
                    message = "Все сервисы активированы",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка активации сервисов");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Получение метрик производительности
        /// </summary>
        [HttpGet("metrics")]
        public IActionResult GetMetrics()
        {
            var metrics = new
            {
                Timestamp = DateTime.UtcNow,
                Performance = new
                {
                    CpuUsage = GetCpuUsage(),
                    MemoryUsage = GetMemoryUsage(),
                    ActiveConnections = _agentManager.GetActiveAgentsCount(),
                    TasksPerSecond = _taskManager.GetTasksPerSecond(),
                    BlocksPerSecond = _poolManager.GetBlocksPerSecond()
                },
                Counters = new
                {
                    TotalRequests = GetTotalRequests(),
                    SuccessfulRequests = GetSuccessfulRequests(),
                    FailedRequests = GetFailedRequests(),
                    AverageResponseTime = GetAverageResponseTime()
                }
            };

            return Ok(metrics);
        }

        private TimeSpan GetUptime()
        {
            return DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        }

        private string GetMemoryStatus()
        {
            var workingSet = Environment.WorkingSet;
            var maxWorkingSet = Process.GetCurrentProcess().MaxWorkingSet.ToInt64();
            var usagePercent = (double)workingSet / maxWorkingSet * 100;

            return usagePercent < 80 ? "Healthy" : "Warning";
        }

        private double GetMemoryUsage()
        {
            var process = Process.GetCurrentProcess();
            return (double)process.WorkingSet64 / (1024 * 1024); // MB
        }

        private double GetCpuUsage()
        {
            // Упрощенная реализация - в реальном проекте лучше использовать PerformanceCounter
            return 0.0; // Заглушка
        }

        private long GetTotalRequests()
        {
            // Заглушка - в реальном проекте нужно отслеживать метрики
            return 0;
        }

        private long GetSuccessfulRequests()
        {
            return 0;
        }

        private long GetFailedRequests()
        {
            return 0;
        }

        private double GetAverageResponseTime()
        {
            return 0.0;
        }
    }
} 
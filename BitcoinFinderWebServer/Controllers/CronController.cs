using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/cron")]
    public class CronController : ControllerBase
    {
        private readonly ILogger<CronController> _logger;
        private readonly PoolManager _poolManager;
        private readonly TaskManager _taskManager;
        private readonly AgentManager _agentManager;
        private readonly BackgroundTaskService _backgroundTaskService;

        public CronController(
            ILogger<CronController> logger,
            PoolManager poolManager,
            TaskManager taskManager,
            AgentManager agentManager,
            BackgroundTaskService backgroundTaskService)
        {
            _logger = logger;
            _poolManager = poolManager;
            _taskManager = taskManager;
            _agentManager = agentManager;
            _backgroundTaskService = backgroundTaskService;
        }

        /// <summary>
        /// Основной endpoint для cron - поддерживает активность и запускает задачи
        /// </summary>
        [HttpGet("ping")]
        public async Task<IActionResult> CronPing()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                _logger.LogInformation("Cron ping received at {Time}", startTime);

                // Обновляем активность пула
                _poolManager.UpdateActivity();

                // Проверяем и восстанавливаем задачи
                var restoredTasks = await RestoreTasksIfNeeded();

                // Очищаем оффлайн агентов
                var cleanedAgents = await CleanupOfflineAgents();

                var result = new
                {
                    timestamp = startTime,
                    status = "success",
                    restoredTasks = restoredTasks,
                    cleanedAgents = cleanedAgents,
                    memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024), // MB
                    uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                    poolStatus = _poolManager.GetStatus()
                };

                _logger.LogInformation("Cron ping completed successfully");
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cron ping");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Запуск конкретной задачи по ID
        /// </summary>
        [HttpPost("start-task/{taskId}")]
        public async Task<IActionResult> StartTask(string taskId)
        {
            try
            {
                _logger.LogInformation("Cron: Starting task {TaskId}", taskId);

                var task = await _taskManager.GetTaskAsync(taskId);
                if (task == null)
                {
                    return NotFound(new { error = "Task not found", taskId });
                }

                if (task.Status == "Running")
                {
                    return BadRequest(new { error = "Task is already running", taskId });
                }

                await _backgroundTaskService.StartTaskAsync(taskId);

                return Ok(new
                {
                    success = true,
                    taskId = taskId,
                    status = "started",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting task {TaskId} via cron", taskId);
                return StatusCode(500, new { error = "Failed to start task", message = ex.Message });
            }
        }

        /// <summary>
        /// Остановка задачи по ID
        /// </summary>
        [HttpPost("stop-task/{taskId}")]
        public async Task<IActionResult> StopTask(string taskId)
        {
            try
            {
                _logger.LogInformation("Cron: Stopping task {TaskId}", taskId);

                await _backgroundTaskService.StopTaskAsync(taskId);

                return Ok(new
                {
                    success = true,
                    taskId = taskId,
                    status = "stopped",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping task {TaskId} via cron", taskId);
                return StatusCode(500, new { error = "Failed to stop task", message = ex.Message });
            }
        }

        /// <summary>
        /// Создание новой задачи через cron
        /// </summary>
        [HttpPost("create-task")]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
        {
            try
            {
                _logger.LogInformation("Cron: Creating task {TaskName}", request.Name);

                var parameters = new SearchParameters
                {
                    TargetAddress = request.TargetAddress,
                    WordCount = request.WordCount,
                    StartIndex = request.StartIndex,
                    EndIndex = request.EndIndex,
                    BlockSize = request.BlockSize,
                    Threads = request.Threads
                };

                var task = await _taskManager.CreateTaskAsync(request.Name, parameters);

                // Автоматически запускаем задачу если указано
                if (request.AutoStart)
                {
                    await _backgroundTaskService.StartTaskAsync(task.Id);
                }

                return Ok(new
                {
                    success = true,
                    taskId = task.Id,
                    taskName = task.Name,
                    status = request.AutoStart ? "started" : "created",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating task via cron");
                return StatusCode(500, new { error = "Failed to create task", message = ex.Message });
            }
        }

        /// <summary>
        /// Получение статуса всех задач
        /// </summary>
        [HttpGet("tasks-status")]
        public async Task<IActionResult> GetTasksStatus()
        {
            try
            {
                var tasks = await _taskManager.GetAllTasksAsync();
                var runningTasks = _backgroundTaskService.GetRunningTaskIds();

                var status = tasks.Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    status = t.Status,
                    isRunning = runningTasks.Contains(t.Id),
                    processedCombinations = t.ProcessedCombinations,
                    totalCombinations = t.TotalCombinations,
                    createdAt = t.CreatedAt,
                    startedAt = t.StartedAt,
                    completedAt = t.CompletedAt,
                    foundSeedPhrase = t.FoundSeedPhrase,
                    foundAddress = t.FoundAddress
                }).ToList();

                return Ok(new
                {
                    success = true,
                    tasks = status,
                    totalTasks = tasks.Count,
                    runningTasks = runningTasks.Count,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tasks status via cron");
                return StatusCode(500, new { error = "Failed to get tasks status", message = ex.Message });
            }
        }

        /// <summary>
        /// Принудительный сброс пула
        /// </summary>
        [HttpPost("reset-pool")]
        public async Task<IActionResult> ResetPool()
        {
            try
            {
                _logger.LogInformation("Cron: Forcing pool reset");

                // Останавливаем все задачи
                var runningTasks = _backgroundTaskService.GetRunningTaskIds();
                foreach (var taskId in runningTasks)
                {
                    await _backgroundTaskService.StopTaskAsync(taskId);
                }

                // Обновляем активность пула
                _poolManager.UpdateActivity();

                return Ok(new
                {
                    success = true,
                    stoppedTasks = runningTasks.Count,
                    message = "Pool reset completed",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting pool via cron");
                return StatusCode(500, new { error = "Failed to reset pool", message = ex.Message });
            }
        }

        private async Task<int> RestoreTasksIfNeeded()
        {
            try
            {
                var tasks = await _taskManager.GetAllTasksAsync();
                var runningTasks = _backgroundTaskService.GetRunningTaskIds();
                var restoredCount = 0;

                foreach (var task in tasks.Where(t => t.Status == "Running" && !runningTasks.Contains(t.Id)))
                {
                    _logger.LogInformation("Restoring task {TaskId} via cron", task.Id);
                    await _backgroundTaskService.StartTaskAsync(task.Id);
                    restoredCount++;
                }

                return restoredCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring tasks");
                return 0;
            }
        }

        private async Task<int> CleanupOfflineAgents()
        {
            try
            {
                // Логика очистки оффлайн агентов
                return 0; // Заглушка
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up offline agents");
                return 0;
            }
        }
    }

    public class CreateTaskRequest
    {
        public string Name { get; set; } = "";
        public string TargetAddress { get; set; } = "";
        public int WordCount { get; set; } = 12;
        public long StartIndex { get; set; } = 0;
        public long EndIndex { get; set; } = 0;
        public long BlockSize { get; set; } = 10000; // Меньший размер для shared hosting
        public int Threads { get; set; } = 1; // Один поток для shared hosting
        public bool AutoStart { get; set; } = false;
    }
} 
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BitcoinFinderWebServer.Models;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class TaskController : ControllerBase
    {
        private readonly TaskManager _taskManager;
        private readonly SeedPhraseFinder _seedPhraseFinder;
        private readonly IBackgroundTaskService _backgroundTaskService;
        private readonly ILogger<TaskController> _logger;

        public TaskController(TaskManager taskManager, SeedPhraseFinder seedPhraseFinder, IBackgroundTaskService backgroundTaskService, ILogger<TaskController> logger)
        {
            _taskManager = taskManager;
            _seedPhraseFinder = seedPhraseFinder;
            _backgroundTaskService = backgroundTaskService;
            _logger = logger;
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
        {
            try
            {
                _logger.LogInformation($"Creating task: {request.Name}");
                _logger.LogInformation($"Request parameters: TargetAddress='{request.TargetAddress}', WordCount={request.WordCount}, BatchSize={request.BatchSize}, BlockSize={request.BlockSize}, Threads={request.Threads}");

                var parameters = new SearchParameters
                {
                    TargetAddress = request.TargetAddress,
                    KnownWords = request.KnownWords ?? "",
                    WordCount = request.WordCount,
                    Language = request.Language ?? "english",
                    StartIndex = request.StartIndex,
                    EndIndex = request.EndIndex,
                    BatchSize = request.BatchSize,
                    BlockSize = request.BlockSize,
                    Threads = request.Threads
                };

                _logger.LogInformation($"SearchParameters created: TargetAddress='{parameters.TargetAddress}', WordCount={parameters.WordCount}, BatchSize={parameters.BatchSize}, BlockSize={parameters.BlockSize}, Threads={parameters.Threads}");

                var task = await _taskManager.CreateTaskAsync(request.Name, parameters);

                return Ok(new
                {
                    Success = true,
                    TaskId = task.Id,
                    TotalCombinations = task.TotalCombinations,
                    BlockCount = task.Blocks.Count,
                    Message = "Task created successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Task creation failed: {ex.Message}");
                _logger.LogError($"Exception type: {ex.GetType().Name}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");
                return BadRequest(new { Success = false, Message = $"Task creation failed: {ex.Message} (Type: {ex.GetType().Name})" });
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetTasks()
        {
            try
            {
                var tasks = await _taskManager.GetAllTasksAsync();
                // Маппинг для фронта: camelCase поля
                var mapped = tasks.Select(task => new {
                    id = task.Id,
                    name = task.Name,
                    status = task.Status,
                    createdAt = task.CreatedAt,
                    startedAt = task.StartedAt,
                    stoppedAt = task.StoppedAt,
                    completedAt = task.CompletedAt,
                    assignedTo = task.AssignedTo,
                    targetAddress = task.Blocks.FirstOrDefault()?.TargetAddress ?? task.SearchParameters.TargetAddress,
                    wordCount = task.SearchParameters.WordCount,
                    language = task.SearchParameters.Language,
                    startIndex = task.SearchParameters.StartIndex,
                    endIndex = task.SearchParameters.EndIndex,
                    batchSize = task.SearchParameters.BatchSize,
                    blockSize = task.SearchParameters.BlockSize,
                    threads = task.SearchParameters.Threads,
                    totalCombinations = task.TotalCombinations,
                    processedCombinations = task.ProcessedCombinations,
                    foundSeedPhrase = task.FoundSeedPhrase,
                    foundAddress = task.FoundAddress,
                    errorMessage = task.ErrorMessage,
                    blocks = task.Blocks
                }).ToList();
                return Ok(mapped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка задач");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("{taskId}")]
        public async Task<IActionResult> GetTask(string taskId)
        {
            try
            {
                var task = await _taskManager.GetTaskAsync(taskId);
                if (task == null)
                {
                    return NotFound(new { Success = false, Message = "Задача не найдена" });
                }
                // Обновляем ProcessedCombinations перед возвратом
                task.ProcessedCombinations = _taskManager.GetProcessedCombinations(task);
                // Маппинг для фронта: camelCase поля
                var mapped = new {
                    id = task.Id,
                    name = task.Name,
                    status = task.Status,
                    createdAt = task.CreatedAt,
                    startedAt = task.StartedAt,
                    stoppedAt = task.StoppedAt,
                    completedAt = task.CompletedAt,
                    assignedTo = task.AssignedTo,
                    targetAddress = task.Blocks.FirstOrDefault()?.TargetAddress ?? task.SearchParameters.TargetAddress,
                    wordCount = task.SearchParameters.WordCount,
                    language = task.SearchParameters.Language,
                    startIndex = task.SearchParameters.StartIndex,
                    endIndex = task.SearchParameters.EndIndex,
                    batchSize = task.SearchParameters.BatchSize,
                    blockSize = task.SearchParameters.BlockSize,
                    threads = task.SearchParameters.Threads,
                    totalCombinations = task.TotalCombinations,
                    processedCombinations = task.ProcessedCombinations,
                    foundSeedPhrase = task.FoundSeedPhrase,
                    foundAddress = task.FoundAddress,
                    errorMessage = task.ErrorMessage,
                    blocks = task.Blocks
                };
                return Ok(mapped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении задачи");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("task/{taskId}")]
        public async Task<IActionResult> GetTaskById(string taskId)
        {
            try
            {
                var task = await _taskManager.GetTaskAsync(taskId);
                if (task == null)
                {
                    return NotFound(new { Success = false, Message = "Задача не найдена" });
                }
                // Обновляем ProcessedCombinations перед возвратом
                task.ProcessedCombinations = _taskManager.GetProcessedCombinations(task);
                return Ok(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении задачи");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartServer()
        {
            try
            {
                _logger.LogInformation("Запуск сервера поиска");

                await _taskManager.StartServerSearchAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Сервер поиска запущен"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске сервера");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("stop")]
        public async Task<IActionResult> StopServer()
        {
            try
            {
                _logger.LogInformation("Остановка сервера поиска");

                await _taskManager.StopServerSearchAsync();

                return Ok(new
                {
                    Success = true,
                    Message = "Сервер поиска остановлен"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке сервера");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("set-threads")]
        public async Task<IActionResult> SetThreads([FromQuery] int threads)
        {
            try
            {
                await _taskManager.SetServerThreadsAsync(threads);
                return Ok(new { Success = true, Threads = threads, Message = "Количество потоков сервера обновлено" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при изменении количества потоков");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("task/{taskId}/start")]
        public async Task<IActionResult> StartTask(string taskId)
        {
            try
            {
                var task = await _taskManager.GetTaskAsync(taskId);
                if (task == null)
                {
                    return NotFound(new { Success = false, Message = "Задача не найдена" });
                }

                await _backgroundTaskService.StartTaskAsync(taskId);

                return Ok(new { Success = true, Message = "Задача запущена в фоновом режиме" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при запуске задачи");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("task/{taskId}/pause")]
        public async Task<IActionResult> PauseTask(string taskId)
        {
            try
            {
                var task = await _taskManager.GetTaskAsync(taskId);
                if (task == null)
                {
                    return NotFound(new { Success = false, Message = "Задача не найдена" });
                }

                await _backgroundTaskService.PauseTaskAsync(taskId);

                return Ok(new { Success = true, Message = "Задача приостановлена" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при приостановке задачи");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("task/{taskId}/stop")]
        public async Task<IActionResult> StopTask(string taskId)
        {
            try
            {
                var task = await _taskManager.GetTaskAsync(taskId);
                if (task == null)
                {
                    return NotFound(new { Success = false, Message = "Задача не найдена" });
                }

                await _backgroundTaskService.StopTaskAsync(taskId);

                return Ok(new { Success = true, Message = "Задача остановлена" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при остановке задачи");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpDelete("task/{taskId}")]
        public async Task<IActionResult> DeleteTask(string taskId)
        {
            try
            {
                // Сначала останавливаем задачу, если она запущена
                if (_backgroundTaskService.IsTaskRunning(taskId))
                {
                    await _backgroundTaskService.StopTaskAsync(taskId);
                }

                await _taskManager.DeleteTaskAsync(taskId);

                return Ok(new { Success = true, Message = "Задача удалена" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при удалении задачи");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("validate")]
        public async Task<IActionResult> ValidateSeedPhrase([FromQuery] string seedPhrase)
        {
            try
            {
                var isValid = await _seedPhraseFinder.ValidateSeedPhraseAsync(seedPhrase);
                var address = isValid ? await _seedPhraseFinder.GenerateBitcoinAddressAsync(seedPhrase) : null;

                return Ok(new { Success = true, IsValid = isValid, Address = address });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при валидации seed phrase");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("calculate-combinations")]
        public async Task<IActionResult> CalculateCombinations([FromQuery] int wordCount, [FromQuery] string language = "english")
        {
            try
            {
                var parameters = new SearchParameters
                {
                    WordCount = wordCount,
                    Language = language
                };

                var totalCombinations = await _seedPhraseFinder.CalculateTotalCombinationsAsync(parameters);
                return Ok(new { Success = true, TotalCombinations = totalCombinations });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при расчете комбинаций");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("progress/current")]
        public IActionResult GetCurrentProgress()
        {
            try
            {
                var stats = _taskManager.GetServerStats();
                return Ok(new
                {
                    Success = true,
                    TotalProcessed = stats.TotalProcessed,
                    TasksPerSecond = stats.TasksPerSecond,
                    RunningTasks = _backgroundTaskService.GetRunningTaskIds().Count,
                    IsRunning = stats.IsRunning
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении прогресса");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("live-log")]
        public IActionResult GetLiveLog()
        {
            try
            {
                var phrases = _seedPhraseFinder.GetLastSeedPhrases();
                return Ok(new
                {
                    Success = true,
                    Phrases = phrases.Select(p => new
                    {
                        taskId = p.TaskId,
                        timestamp = p.Timestamp,
                        index = p.Index,
                        phrase = p.Phrase,
                        address = p.Address,
                        status = p.Status
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении живого лога");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetSystemStatus()
        {
            try
            {
                var tasks = await _taskManager.GetAllTasksAsync();
                var runningTasks = _backgroundTaskService.GetRunningTaskIds();
                var stats = _taskManager.GetServerStats();

                return Ok(new
                {
                    Success = true,
                    TotalTasks = tasks.Count,
                    RunningTasks = runningTasks.Count,
                    PausedTasks = tasks.Count(t => t.Status == "Paused"),
                    CompletedTasks = tasks.Count(t => t.Status == "Completed"),
                    StoppedTasks = tasks.Count(t => t.Status == "Stopped"),
                    ServerStats = stats,
                    RunningTaskIds = runningTasks
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статуса системы");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        public class CreateTaskRequest
        {
            public string Name { get; set; } = "";
            public string TargetAddress { get; set; } = "";
            public string? KnownWords { get; set; }
            public int WordCount { get; set; } = 12;
            public string? Language { get; set; }
            public long StartIndex { get; set; } = 0;
            public long EndIndex { get; set; } = 0;
            public int BatchSize { get; set; } = 1000;
            public long BlockSize { get; set; } = 100000;
            public int Threads { get; set; } = 1;
        }
    }
} 
using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Models;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TaskController : ControllerBase
    {
        private readonly TaskManager _taskManager;
        private readonly SeedPhraseFinder _seedPhraseFinder;
        private readonly ILogger<TaskController> _logger;

        public TaskController(TaskManager taskManager, SeedPhraseFinder seedPhraseFinder, ILogger<TaskController> logger)
        {
            _taskManager = taskManager;
            _seedPhraseFinder = seedPhraseFinder;
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
                return Ok(tasks);
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

        [HttpGet("validate")]
        public async Task<IActionResult> ValidateSeedPhrase([FromQuery] string seedPhrase)
        {
            try
            {
                var isValid = await _seedPhraseFinder.ValidateSeedPhraseAsync(seedPhrase);
                var address = isValid ? await _seedPhraseFinder.GenerateBitcoinAddressAsync(seedPhrase) : null;

                return Ok(new
                {
                    Success = true,
                    IsValid = isValid,
                    Address = address,
                    Message = isValid ? "Seed-фраза валидна" : "Seed-фраза невалидна"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при валидации seed-фразы");
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

                return Ok(new
                {
                    Success = true,
                    WordCount = wordCount,
                    Language = language,
                    TotalCombinations = totalCombinations,
                    FormattedCombinations = totalCombinations.ToString("N0"),
                    Message = $"Всего комбинаций для {wordCount} слов: {totalCombinations:N0}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при вычислении комбинаций");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("progress/current")]
        public IActionResult GetCurrentProgress()
        {
            var finder = HttpContext.RequestServices.GetService(typeof(BitcoinFinderWebServer.Services.SeedPhraseFinder)) as BitcoinFinderWebServer.Services.SeedPhraseFinder;
            if (finder == null)
                return NotFound();
            var lastPhrases = finder.GetLastSeedPhrases();
            var (idx, phrase) = finder.LoadProgress();
            return Ok(new {
                LastPhrases = lastPhrases,
                LastSavedIndex = idx,
                LastSavedPhrase = phrase
            });
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
        public int Threads { get; set; } = 1; // Added Threads property
    }
} 
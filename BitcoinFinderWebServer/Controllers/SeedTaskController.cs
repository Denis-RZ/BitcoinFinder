using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/seed-task")]
    public class SeedTaskController : ControllerBase
    {
        private readonly BackgroundSeedTaskManager _manager;
        public SeedTaskController(BackgroundSeedTaskManager manager)
        {
            _manager = manager;
        }

        [HttpGet]
        public IActionResult GetAll() => Ok(_manager.GetTasks());

        [HttpPost]
        public IActionResult Add([FromBody] SeedTaskRequest req)
        {
            _manager.AddTask(req.SeedPhrase, req.ExpectedAddress, req.Threads);
            return Ok(new { success = true });
        }

        [HttpPost("start/{id}")]
        public IActionResult Start(string id) { _manager.StartTask(id); return Ok(); }

        [HttpPost("stop/{id}")]
        public IActionResult Stop(string id) { _manager.StopTask(id); return Ok(); }

        [HttpPost("threads/{id}")]
        public IActionResult SetThreads(string id, [FromBody] int threads) { _manager.SetThreads(id, threads); return Ok(); }

        [HttpPatch("threads/{id}")]
        public IActionResult UpdateThreads(string id, [FromBody] int threads)
        {
            if (_manager.GetTasks().FirstOrDefault(t => t.Id == id) is { } task)
            {
                task.Threads = threads;
                return Ok(new { success = true });
            }
            return NotFound(new { success = false, message = "Задача не найдена" });
        }
    }

    public class SeedTaskRequest
    {
        public string SeedPhrase { get; set; } = "";
        public string? ExpectedAddress { get; set; }
        public int? Threads { get; set; }
    }
} 
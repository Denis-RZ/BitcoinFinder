using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/agent")]
    public class AgentApiController : ControllerBase
    {
        // Очередь задач (in-memory, для примера)
        private static ConcurrentQueue<string> _tasks = new();
        private static ConcurrentDictionary<string, string> _activeTasks = new();
        private static ConcurrentDictionary<string, DateTime> _agentHeartbeats = new();

        [HttpPost("get-task")]
        public IActionResult GetTask([FromHeader(Name = "X-Agent-Id")] string agentId)
        {
            if (_tasks.TryDequeue(out var task))
            {
                _activeTasks[agentId] = task;
                return Ok(new { success = true, task });
            }
            return Ok(new { success = false, message = "Нет задач" });
        }

        [HttpPost("result")]
        public IActionResult SubmitResult([FromHeader(Name = "X-Agent-Id")] string agentId, [FromBody] object result)
        {
            _activeTasks.TryRemove(agentId, out _);
            // Здесь можно обработать результат (сохранить, залогировать и т.д.)
            return Ok(new { success = true });
        }

        [HttpPost("heartbeat")]
        public IActionResult Heartbeat([FromHeader(Name = "X-Agent-Id")] string agentId)
        {
            _agentHeartbeats[agentId] = DateTime.UtcNow;
            return Ok(new { success = true });
        }

        [HttpPost("return-task")]
        public IActionResult ReturnTask([FromHeader(Name = "X-Agent-Id")] string agentId)
        {
            if (_activeTasks.TryRemove(agentId, out var task))
            {
                _tasks.Enqueue(task);
            }
            return Ok(new { success = true });
        }

        // Для теста: добавить задачу вручную
        [HttpPost("add-task")]
        public IActionResult AddTask([FromBody] string task)
        {
            _tasks.Enqueue(task);
            return Ok(new { success = true });
        }
    }
} 
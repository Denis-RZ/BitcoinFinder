using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using BitcoinFinderWebServer.Models;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AgentController : ControllerBase
    {
        private readonly AgentManager _agentManager;
        private readonly TaskManager _taskManager;
        private readonly ILogger<AgentController> _logger;

        public AgentController(AgentManager agentManager, TaskManager taskManager, ILogger<AgentController> logger)
        {
            _agentManager = agentManager;
            _taskManager = taskManager;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> RegisterAgent([FromBody] AgentRegistrationRequest request)
        {
            try
            {
                _logger.LogInformation($"Регистрация агента: {request.Name}");

                var agent = new Agent
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = request.Name,
                    Threads = request.Threads,
                    Status = "Connected",
                    LastSeen = DateTime.UtcNow,
                    ConnectedAt = DateTime.UtcNow
                };

                await _agentManager.RegisterAgentAsync(agent);

                return Ok(new { 
                    Success = true, 
                    AgentId = agent.Id,
                    Message = "Агент успешно зарегистрирован" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при регистрации агента");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpDelete("{agentName}")]
        public async Task<IActionResult> UnregisterAgent(string agentName)
        {
            try
            {
                _logger.LogInformation($"Отключение агента: {agentName}");

                await _agentManager.UnregisterAgentAsync(agentName);

                return Ok(new { 
                    Success = true, 
                    Message = "Агент успешно отключен" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отключении агента");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("{agentName}/task")]
        public async Task<IActionResult> GetTask(string agentName)
        {
            try
            {
                _logger.LogInformation($"Запрос задачи для агента: {agentName}");

                // Обновляем время последней активности агента
                await _agentManager.UpdateAgentActivityAsync(agentName);

                // Получаем задачу для агента
                var task = await _taskManager.GetNextTaskAsync(agentName);
                
                if (task == null)
                {
                    return Ok(null); // Нет доступных задач
                }

                return Ok(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении задачи для агента");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        // Новые методы для совместимости с WinForms-агентами
        [HttpGet("{agentName}/block")]
        public async Task<IActionResult> GetBlock(string agentName)
        {
            try
            {
                _logger.LogInformation($"Запрос блока для агента: {agentName}");

                // Обновляем время последней активности агента
                await _agentManager.UpdateAgentActivityAsync(agentName);

                // Получаем блок для агента
                var block = await _taskManager.GetNextBlockAsync(agentName);
                
                if (block == null)
                {
                    return Ok(new { command = "NO_BLOCKS" }); // Нет доступных блоков
                }

                // Формат ответа совместимый с WinForms
                var response = new
                {
                    command = "ASSIGN_BLOCK",
                    blockId = block.BlockId,
                    startIndex = block.StartIndex,
                    endIndex = block.EndIndex,
                    wordCount = block.WordCount,
                    targetAddress = block.TargetAddress
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении блока для агента");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("{agentName}/block-result")]
        public async Task<IActionResult> SubmitBlockResult(string agentName, [FromBody] BlockResult result)
        {
            try
            {
                _logger.LogInformation($"Получен результат блока от агента: {agentName}, блок: {result.BlockId}");

                // Обновляем время последней активности агента
                await _agentManager.UpdateAgentActivityAsync(agentName);

                // Обрабатываем результат блока
                await _taskManager.ProcessBlockResultAsync(agentName, result);

                return Ok(new { 
                    Success = true, 
                    Message = "Результат блока успешно обработан" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке результата блока от агента");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("{agentName}/progress")]
        public async Task<IActionResult> ReportProgress(string agentName, [FromBody] ProgressReport report)
        {
            try
            {
                _logger.LogInformation($"Получен отчет о прогрессе от агента: {agentName}, блок: {report.BlockId}");

                // Обновляем время последней активности агента
                await _agentManager.UpdateAgentActivityAsync(agentName);

                // Обновляем прогресс блока
                await _taskManager.UpdateBlockProgressAsync(report.BlockId, report.CurrentIndex);

                return Ok(new { 
                    Success = true, 
                    Message = "Прогресс обновлен" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке отчета о прогрессе");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("{agentName}/found")]
        public async Task<IActionResult> ReportFound(string agentName, [FromBody] FoundReport report)
        {
            try
            {
                _logger.LogWarning($"НАЙДЕНО РЕШЕНИЕ от агента: {agentName}, блок: {report.BlockId}");

                // Обновляем время последней активности агента
                await _agentManager.UpdateAgentActivityAsync(agentName);

                // Создаем результат блока с найденным решением
                var blockResult = new BlockResult
                {
                    BlockId = report.BlockId,
                    Status = "Found",
                    ProcessedCombinations = report.Index,
                    FoundSeedPhrase = report.SeedPhrase,
                    FoundAddress = report.Address,
                    FoundIndex = report.Index
                };

                // Обрабатываем результат
                await _taskManager.ProcessBlockResultAsync(agentName, blockResult);

                return Ok(new { 
                    Success = true, 
                    Message = "Найденное решение зарегистрировано" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке найденного решения");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("{agentName}/result")]
        public async Task<IActionResult> SubmitResult(string agentName, [FromBody] TaskResult result)
        {
            try
            {
                _logger.LogInformation($"Получен результат от агента: {agentName}");

                // Обновляем время последней активности агента
                await _agentManager.UpdateAgentActivityAsync(agentName);

                // Обрабатываем результат
                await _taskManager.ProcessTaskResultAsync(agentName, result);

                return Ok(new { 
                    Success = true, 
                    Message = "Результат успешно обработан" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке результата от агента");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpPatch("{agentName}/threads")]
        public async Task<IActionResult> SetAgentThreads(string agentName, [FromBody] int threads)
        {
            var ok = await _agentManager.SetAgentThreadsAsync(agentName, threads);
            if (ok) return Ok(new { success = true });
            return NotFound(new { success = false, message = "Агент не найден" });
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetAgents()
        {
            try
            {
                var agents = await _agentManager.GetAllAgentsAsync();
                return Ok(agents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка агентов");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        /// <summary>
        /// Получение статуса агентов
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetAgentsStatus()
        {
            try
            {
                var agents = await _agentManager.GetAllAgentsAsync();
                var activeAgents = agents.Count(a => a.IsOnline);

                return Ok(new
                {
                    success = true,
                    totalAgents = agents.Count,
                    activeAgents = activeAgents,
                    agents = agents.Select(a => new
                    {
                        id = a.Id,
                        name = a.Name,
                        isOnline = a.IsOnline,
                        lastSeen = a.LastSeen,
                        processedBlocks = a.ProcessedBlocks,
                        currentTask = a.CurrentTask
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статуса агентов");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Отключение всех агентов
        /// </summary>
        [HttpPost("disconnect-all")]
        public async Task<IActionResult> DisconnectAllAgents()
        {
            try
            {
                var agents = await _agentManager.GetAllAgentsAsync();
                var disconnectedCount = 0;

                foreach (var agent in agents.Where(a => a.IsOnline))
                {
                    await _agentManager.DisconnectAgentAsync(agent.Id);
                    disconnectedCount++;
                }

                return Ok(new
                {
                    success = true,
                    message = $"Отключено {disconnectedCount} агентов",
                    disconnectedCount = disconnectedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отключения агентов");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получение списка всех агентов
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllAgents()
        {
            try
            {
                var agents = await _agentManager.GetAllAgentsAsync();

                return Ok(new
                {
                    success = true,
                    agents = agents.Select(a => new
                    {
                        id = a.Id,
                        name = a.Name,
                        isOnline = a.IsOnline,
                        lastSeen = a.LastSeen,
                        processedBlocks = a.ProcessedBlocks,
                        currentTask = a.CurrentTask,
                        performance = a.Performance
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения списка агентов");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получение информации об агенте по ID
        /// </summary>
        [HttpGet("{agentId}")]
        public async Task<IActionResult> GetAgent(string agentId)
        {
            try
            {
                var agent = await _agentManager.GetAgentAsync(agentId);
                
                if (agent == null)
                {
                    return NotFound(new { success = false, message = "Агент не найден" });
                }

                return Ok(new
                {
                    success = true,
                    agent = new
                    {
                        id = agent.Id,
                        name = agent.Name,
                        isOnline = agent.IsOnline,
                        lastSeen = agent.LastSeen,
                        processedBlocks = agent.ProcessedBlocks,
                        currentTask = agent.CurrentTask,
                        performance = agent.Performance
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения информации об агенте {AgentId}", agentId);
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Отключение конкретного агента
        /// </summary>
        [HttpPost("{agentId}/disconnect")]
        public async Task<IActionResult> DisconnectAgent(string agentId)
        {
            try
            {
                var agent = await _agentManager.GetAgentAsync(agentId);
                
                if (agent == null)
                {
                    return NotFound(new { success = false, message = "Агент не найден" });
                }

                await _agentManager.DisconnectAgentAsync(agentId);

                return Ok(new
                {
                    success = true,
                    message = $"Агент {agent.Name} отключен"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка отключения агента {AgentId}", agentId);
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получение статистики агентов
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetAgentsStats()
        {
            try
            {
                var agents = await _agentManager.GetAllAgentsAsync();
                var onlineAgents = agents.Where(a => a.IsOnline).ToList();

                var stats = new
                {
                    totalAgents = agents.Count,
                    onlineAgents = onlineAgents.Count,
                    offlineAgents = agents.Count - onlineAgents.Count,
                    totalProcessedBlocks = agents.Sum(a => a.ProcessedBlocks),
                    averagePerformance = onlineAgents.Any() ? onlineAgents.Average(a => a.Performance) : 0,
                    topPerformers = onlineAgents
                        .OrderByDescending(a => a.Performance)
                        .Take(5)
                        .Select(a => new { name = a.Name, performance = a.Performance })
                };

                return Ok(new { success = true, stats = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статистики агентов");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        [HttpGet("found-results")]
        public async Task<IActionResult> GetFoundResults()
        {
            try
            {
                var results = _taskManager.GetFoundResults();
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении найденных результатов");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("agent-states")]
        public async Task<IActionResult> GetAgentStates()
        {
            try
            {
                var agentStates = _taskManager.GetAgentStates();
                return Ok(agentStates);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении состояний агентов");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("sync-state")]
        public async Task<IActionResult> GetSyncState()
        {
            try
            {
                var stats = _taskManager.GetServerStats();
                var agentStates = _taskManager.GetAgentStates();
                var foundResults = _taskManager.GetFoundResults();

                var syncState = new
                {
                    ServerStats = stats,
                    AgentStates = agentStates,
                    FoundResults = foundResults,
                    LastSync = DateTime.UtcNow
                };

                return Ok(syncState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении состояния синхронизации");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }
    }

    public class AgentRegistrationRequest
    {
        public string Name { get; set; } = "";
        public int Threads { get; set; } = 1;
        public string Status { get; set; } = "Connected";
    }

    public class TaskResult
    {
        public string TaskId { get; set; } = "";
        public string Status { get; set; } = "";
        public string Result { get; set; } = "";
        public long ProcessedCombinations { get; set; } = 0;
        public TimeSpan ProcessingTime { get; set; }
        public string? FoundSeedPhrase { get; set; }
        public string? FoundAddress { get; set; }
    }

    public class ProgressReport
    {
        public int BlockId { get; set; }
        public long CurrentIndex { get; set; }
        public long Processed { get; set; } = 0;
        public double Rate { get; set; } = 0;
    }

    public class FoundReport
    {
        public int BlockId { get; set; }
        public string SeedPhrase { get; set; } = "";
        public string Address { get; set; } = "";
        public long Index { get; set; }
    }
} 
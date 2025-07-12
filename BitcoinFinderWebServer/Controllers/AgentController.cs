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

        [HttpGet("status")]
        public async Task<IActionResult> GetAgentStatus()
        {
            try
            {
                var status = await _agentManager.GetAgentStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статуса агентов");
                return BadRequest(new { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetServerStats()
        {
            try
            {
                var stats = _taskManager.GetServerStats();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении статистики сервера");
                return BadRequest(new { Success = false, Message = ex.Message });
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
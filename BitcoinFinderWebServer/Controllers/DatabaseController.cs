using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Models;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseController : ControllerBase
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<DatabaseController> _logger;

        public DatabaseController(IDatabaseService databaseService, ILogger<DatabaseController> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// Тестирует подключение к базе данных
        /// </summary>
        [HttpPost("test-connection")]
        public async Task<IActionResult> TestConnection([FromBody] DatabaseConfig config)
        {
            try
            {
                var isConnected = await _databaseService.TestConnectionAsync(config);
                
                if (isConnected)
                {
                    return Ok(new { success = true, message = "Подключение к базе данных успешно" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Не удалось подключиться к базе данных" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка тестирования подключения к базе данных");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Устанавливает базу данных (создание, схема, начальные данные)
        /// </summary>
        [HttpPost("install")]
        public async Task<IActionResult> InstallDatabase([FromBody] DatabaseInstallRequest request)
        {
            try
            {
                _logger.LogInformation("Запрос на установку базы данных: {DatabaseName}", request.Config.DatabaseName);
                
                var result = await _databaseService.InstallDatabaseAsync(request);
                
                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка установки базы данных");
                return StatusCode(500, new DatabaseInstallResponse 
                { 
                    Success = false, 
                    Message = "Внутренняя ошибка сервера",
                    Errors = { ex.Message }
                });
            }
        }

        /// <summary>
        /// Создает только базу данных
        /// </summary>
        [HttpPost("create")]
        public async Task<IActionResult> CreateDatabase([FromBody] DatabaseConfig config)
        {
            try
            {
                var result = await _databaseService.CreateDatabaseAsync(config);
                
                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка создания базы данных");
                return StatusCode(500, new DatabaseInstallResponse 
                { 
                    Success = false, 
                    Message = "Внутренняя ошибка сервера",
                    Errors = { ex.Message }
                });
            }
        }

        /// <summary>
        /// Устанавливает только схему базы данных
        /// </summary>
        [HttpPost("install-schema")]
        public async Task<IActionResult> InstallSchema([FromBody] DatabaseConfig config)
        {
            try
            {
                var result = await _databaseService.InstallSchemaAsync(config);
                
                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка установки схемы базы данных");
                return StatusCode(500, new DatabaseInstallResponse 
                { 
                    Success = false, 
                    Message = "Внутренняя ошибка сервера",
                    Errors = { ex.Message }
                });
            }
        }

        /// <summary>
        /// Устанавливает начальные данные
        /// </summary>
        [HttpPost("install-seed-data")]
        public async Task<IActionResult> InstallSeedData([FromBody] DatabaseConfig config)
        {
            try
            {
                var result = await _databaseService.InstallSeedDataAsync(config);
                
                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка установки начальных данных");
                return StatusCode(500, new DatabaseInstallResponse 
                { 
                    Success = false, 
                    Message = "Внутренняя ошибка сервера",
                    Errors = { ex.Message }
                });
            }
        }

        /// <summary>
        /// Получает информацию о структуре базы данных
        /// </summary>
        [HttpGet("schema-info")]
        public IActionResult GetSchemaInfo()
        {
            var schemaInfo = new
            {
                Tables = new[]
                {
                    new { Name = "Agents", Description = "Таблица агентов" },
                    new { Name = "Tasks", Description = "Таблица задач" },
                    new { Name = "TaskResults", Description = "Таблица результатов задач" },
                    new { Name = "SystemSettings", Description = "Таблица системных настроек" }
                },
                Version = "1.0.0",
                LastUpdated = DateTime.UtcNow
            };

            return Ok(schemaInfo);
        }
    }
} 
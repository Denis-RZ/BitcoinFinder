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
        /// Устанавливает базу данных
        /// </summary>
        [HttpPost("setup")]
        public async Task<IActionResult> SetupDatabase([FromBody] DatabaseSetupRequest request)
        {
            try
            {
                _logger.LogInformation("Начинаем установку базы данных: {DatabaseName}", request.DatabaseName);

                var config = new DatabaseConfig
                {
                    DatabaseName = request.DatabaseName,
                    Server = request.Server,
                    Port = request.Port,
                    Username = request.Username,
                    Password = request.Password,
                    AuthType = request.AuthType
                };

                var response = new DatabaseSetupResponse();

                // Тестируем подключение
                var isConnected = await _databaseService.TestConnectionAsync(config);
                if (!isConnected)
                {
                    response.Success = false;
                    response.Message = "Не удалось подключиться к серверу базы данных";
                    return BadRequest(response);
                }

                // Создаем базу данных если нужно
                if (request.CreateDatabase)
                {
                    var dbResult = await _databaseService.CreateDatabaseAsync(config);
                    if (!dbResult.Success)
                    {
                        response.Success = false;
                        response.Message = $"Ошибка создания базы данных: {dbResult.Message}";
                        response.Errors.AddRange(dbResult.Errors);
                        return BadRequest(response);
                    }
                    response.DatabaseCreated = true;
                }

                // Создаем таблицы если нужно
                if (request.CreateTables)
                {
                    var tablesResult = await _databaseService.CreateTablesAsync(config);
                    if (!tablesResult.Success)
                    {
                        response.Success = false;
                        response.Message = $"Ошибка создания таблиц: {tablesResult.Message}";
                        response.Errors.AddRange(tablesResult.Errors);
                        return BadRequest(response);
                    }
                    response.TablesCreated = true;
                }

                // Вставляем тестовые данные если нужно
                if (request.InsertSampleData)
                {
                    var dataResult = await _databaseService.InsertSampleDataAsync(config);
                    if (!dataResult.Success)
                    {
                        response.Success = false;
                        response.Message = $"Ошибка вставки тестовых данных: {dataResult.Message}";
                        response.Errors.AddRange(dataResult.Errors);
                        return BadRequest(response);
                    }
                    response.SampleDataInserted = true;
                }

                response.Success = true;
                response.Message = "База данных успешно настроена";
                response.DatabaseName = request.DatabaseName;

                _logger.LogInformation("База данных {DatabaseName} успешно настроена", request.DatabaseName);

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка установки базы данных");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        /// <summary>
        /// Получает статус базы данных
        /// </summary>
        [HttpGet("status")]
        public async Task<IActionResult> GetDatabaseStatus()
        {
            try
            {
                var status = await _databaseService.GetDatabaseStatusAsync();
                return Ok(new { success = true, status = status });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения статуса базы данных");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }
    }

    public class DatabaseSetupRequest
    {
        public string DatabaseName { get; set; } = "";
        public string Server { get; set; } = "";
        public int Port { get; set; } = 1433;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string AuthType { get; set; } = "sql";
        public bool CreateDatabase { get; set; } = true;
        public bool CreateTables { get; set; } = true;
        public bool InsertSampleData { get; set; } = false;
    }

    public class DatabaseSetupResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string DatabaseName { get; set; } = "";
        public bool DatabaseCreated { get; set; }
        public bool TablesCreated { get; set; }
        public bool SampleDataInserted { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
} 
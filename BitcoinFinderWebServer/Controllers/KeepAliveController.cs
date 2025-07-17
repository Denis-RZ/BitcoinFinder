using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Services;
using System.Diagnostics;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/keep-alive")]
    public class KeepAliveController : ControllerBase
    {
        private readonly ILogger<KeepAliveController> _logger;
        private readonly PoolManager _poolManager;

        public KeepAliveController(ILogger<KeepAliveController> logger, PoolManager poolManager)
        {
            _logger = logger;
            _poolManager = poolManager;
        }

        [HttpGet]
        public IActionResult KeepAlive()
        {
            try
            {
                // Обновляем активность пула
                _poolManager.UpdateActivity();
                
                var status = new
                {
                    timestamp = DateTime.UtcNow,
                    status = "alive",
                    poolStatus = _poolManager.GetStatus(),
                    memoryUsage = GC.GetTotalMemory(false) / (1024 * 1024), // MB
                    uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()
                };

                _logger.LogDebug($"Keep-alive ping: {status.status}");
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in keep-alive");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            var health = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            };

            return Ok(health);
        }
    }
} 
using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Models;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var isValid = await _authService.ValidateCredentialsAsync(request.Username, request.Password);
                
                if (isValid)
                {
                    // Простая сессия - в реальном проекте лучше использовать JWT
                    var token = Guid.NewGuid().ToString();
                    HttpContext.Session.SetString("AuthToken", token);
                    HttpContext.Session.SetString("Username", request.Username);
                    
                    _logger.LogInformation("Успешный вход пользователя: {Username}", request.Username);
                    
                    return Ok(new LoginResponse 
                    { 
                        Success = true, 
                        Message = "Вход выполнен успешно",
                        Token = token
                    });
                }
                else
                {
                    _logger.LogWarning("Неудачная попытка входа: {Username}", request.Username);
                    return Unauthorized(new LoginResponse 
                    { 
                        Success = false, 
                        Message = "Неверное имя пользователя или пароль" 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при входе");
                return StatusCode(500, new LoginResponse 
                { 
                    Success = false, 
                    Message = "Внутренняя ошибка сервера" 
                });
            }
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return Ok(new { success = true, message = "Выход выполнен успешно" });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                if (request.NewPassword != request.ConfirmPassword)
                {
                    return BadRequest(new { success = false, message = "Новые пароли не совпадают" });
                }

                var success = await _authService.ChangePasswordAsync(request.CurrentPassword, request.NewPassword);
                
                if (success)
                {
                    _logger.LogInformation("Пароль изменен успешно");
                    return Ok(new { success = true, message = "Пароль изменен успешно" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Неверный текущий пароль" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при смене пароля");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        [HttpGet("config")]
        public async Task<IActionResult> GetConfig()
        {
            try
            {
                var config = await _authService.GetConfigAsync();
                return Ok(new { 
                    success = true, 
                    config = new { 
                        username = config.Username, 
                        requireAuth = config.RequireAuth 
                    } 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения конфигурации");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        [HttpPost("config")]
        public async Task<IActionResult> UpdateConfig([FromBody] AdminConfig config)
        {
            try
            {
                await _authService.SaveConfigAsync(config);
                return Ok(new { success = true, message = "Конфигурация обновлена успешно" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления конфигурации");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }
    }
} 
using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Models;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(IAuthService authService, ILogger<AccountController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpGet]
        [Route("login")]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [Route("login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var isValid = await _authService.ValidateCredentialsAsync(model.Username, model.Password);
            if (isValid)
            {
                // Устанавливаем сессию
                HttpContext.Session.SetString("AuthToken", Guid.NewGuid().ToString());
                HttpContext.Session.SetString("Username", model.Username);
                
                _logger.LogInformation($"Успешный вход: {model.Username}");
                return RedirectToAction("Index", "Home");
            }
            
            ModelState.AddModelError("", "Неверный логин или пароль");
            return View(model);
        }

        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
} 
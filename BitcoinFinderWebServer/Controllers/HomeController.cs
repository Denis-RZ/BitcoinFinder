using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Models;
using BitcoinFinderWebServer.Services;
using Microsoft.AspNetCore.Http;
using System.IO.Compression;

namespace BitcoinFinderWebServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly TaskManager _taskManager;
        private readonly AgentManager _agentManager;

        public HomeController(ILogger<HomeController> logger, TaskManager taskManager, AgentManager agentManager)
        {
            _logger = logger;
            _taskManager = taskManager;
            _agentManager = agentManager;
        }

        public IActionResult Index()
        {
            // Проверяем авторизацию
            var authToken = HttpContext.Session.GetString("AuthToken");
            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Account");
            }

            var username = HttpContext.Session.GetString("Username");
            ViewBag.Username = username;
            ViewBag.TaskCount = 0; // TODO: Получить реальное количество задач
            ViewBag.AgentCount = 0; // TODO: Получить реальное количество агентов
            
            return View();
        }

        public IActionResult SeedSearch()
        {
            var authToken = HttpContext.Session.GetString("AuthToken");
            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        public IActionResult Admin()
        {
            var authToken = HttpContext.Session.GetString("AuthToken");
            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Account");
            }

            var username = HttpContext.Session.GetString("Username");
            if (username != "admin")
            {
                return RedirectToAction("Index");
            }

            return View();
        }

        public IActionResult KeepAlive()
        {
            return Json(new { status = "ok", timestamp = DateTime.UtcNow });
        }

        public IActionResult Database()
        {
            var authToken = HttpContext.Session.GetString("AuthToken");
            if (string.IsNullOrEmpty(authToken))
            {
                return RedirectToAction("Login", "Account");
            }
            return View("DatabaseSetup");
        }
    }
} 
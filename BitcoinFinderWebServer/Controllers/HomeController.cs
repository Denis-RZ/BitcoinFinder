using Microsoft.AspNetCore.Mvc;
using BitcoinFinderWebServer.Services;

namespace BitcoinFinderWebServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult SeedSearch()
        {
            return View();
        }

        public IActionResult TaskManager()
        {
            return View();
        }

        public IActionResult Admin()
        {
            return View();
        }

        public IActionResult DatabaseSetup()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
} 
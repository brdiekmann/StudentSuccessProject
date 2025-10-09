using GeminiDataParsingTestProject.Models;
using GeminiDataParsingTestProject.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace GeminiDataParsingTestProject.Controllers
{
    public class HomeController : Controller
    {
        private readonly GeminiService _geminiService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger, GeminiService geminiService)
        {
            _logger = logger;
            _geminiService = geminiService;
        }
        public async Task<IActionResult> ListModels()
        {
            var modelsJson = await _geminiService.ListModelsAsync();
            return Content(modelsJson, "application/json");
        }
        public IActionResult Index()
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
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

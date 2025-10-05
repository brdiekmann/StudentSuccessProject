using GeminiDataParsingTestProject.Models;
using GeminiDataParsingTestProject.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace GeminiDataParsingTestProject.Controllers
{
    public class PdfController : Controller
    {
        private readonly GeminiService _geminiService;

        public PdfController(GeminiService geminiService)
        {
            _geminiService = geminiService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Analyze(IFormFile pdfFile)
        {
            if (pdfFile == null || pdfFile.Length == 0)
                return BadRequest("Please upload a PDF file.");

            string textContent;
            using (var stream = pdfFile.OpenReadStream())
            using (var pdf = PdfDocument.Open(stream))
            {
                textContent = string.Join("\n", pdf.GetPages().Select(p => p.Text));
            }

            // Use valid model name
            var geminiResponse = await _geminiService.AnalyzePdfTextAsync(textContent);

            //Edit this code to parse into PdfAnalysisResult object
            ViewBag.Result = geminiResponse;
            return View("Result");
            
        }
    }

}

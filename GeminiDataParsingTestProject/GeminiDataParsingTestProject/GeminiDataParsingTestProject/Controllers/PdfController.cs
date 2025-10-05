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

            // Call Gemini API
            var geminiResponse = await _geminiService.AnalyzePdfTextAsync(textContent);

            // Parse Gemini API response
            var doc = JsonDocument.Parse(geminiResponse);
            var candidateText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            // Remove markdown code fences if present
            candidateText = candidateText?.Trim();
            if (candidateText.StartsWith("```json"))
                candidateText = candidateText.Substring(7).Trim();
            if (candidateText.EndsWith("```"))
                candidateText = candidateText.Substring(0, candidateText.Length - 3).Trim();

            // Deserialize into your PdfAnalysisResult
            var analysisResult = JsonSerializer.Deserialize<PdfAnalysisResult>(candidateText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return View("Result", analysisResult);
        }


        /* Working code below, as long as you change PdfAnalysisResult Model to be seperate from Assignment model
         * Commenting out to try new approach
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
        */
    }

}

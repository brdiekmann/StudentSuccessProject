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


            var analysisResult = JsonSerializer.Deserialize<PdfAnalysisResult>(candidateText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return View("Result", analysisResult);
        }
        // Helper method to display results
        [HttpPost]
        public IActionResult DisplayResult(string resultJson)
        {
            var analysisResult = JsonSerializer.Deserialize<PdfAnalysisResult>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return View("Result", analysisResult);
        }

        // New endpoint for progress-enabled analysis
        [HttpPost]
        public async Task AnalyzeWithProgress(IFormFile pdfFile)
        {
            // Set up streaming response
            Response.ContentType = "application/json";
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("X-Accel-Buffering", "no");

            async Task SendProgress(string status, int percentage, string data = null)
            {
                var progressUpdate = JsonSerializer.Serialize(new
                {
                    status = status,
                    percentage = percentage,
                    data = data
                });
                await Response.WriteAsync(progressUpdate + "\n");
                await Response.Body.FlushAsync();
            }

            try
            {
                if (pdfFile == null || pdfFile.Length == 0)
                {
                    await SendProgress("error", 0, "Please upload a PDF file.");
                    return;
                }

                // Step 1: Extract text from PDF
                await SendProgress("Extracting text from PDF...", 20);

                string textContent;
                using (var stream = pdfFile.OpenReadStream())
                using (var pdf = PdfDocument.Open(stream))
                {
                    textContent = string.Join("\n", pdf.GetPages().Select(p => p.Text));
                }

                await SendProgress("Text extracted successfully", 40);

                // Step 2: Call Gemini API
                await SendProgress("Sending to Gemini API...", 50);

                var geminiResponse = await _geminiService.AnalyzePdfTextAsync(textContent);

                await SendProgress("Received response from Gemini", 70);

                // Step 3: Parse response
                await SendProgress("Parsing response...", 80);

                var doc = JsonDocument.Parse(geminiResponse);
                var candidateText = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                // Remove markdown code fences if present
                candidateText = candidateText?.Trim();
                if (candidateText?.StartsWith("```json") == true)
                    candidateText = candidateText.Substring(7).Trim();
                if (candidateText?.EndsWith("```") == true)
                    candidateText = candidateText.Substring(0, candidateText.Length - 3).Trim();

                await SendProgress("Deserializing results...", 90);

                // Deserialize into your PdfAnalysisResult
                var analysisResult = JsonSerializer.Deserialize<PdfAnalysisResult>(candidateText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Step 4: Complete
                var resultJson = JsonSerializer.Serialize(analysisResult);
                await SendProgress("complete", 100, resultJson);
            }
            catch (Exception ex)
            {
                await SendProgress("error", 0, ex.Message);
            }
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

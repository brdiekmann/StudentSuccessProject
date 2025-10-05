using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
namespace GeminiDataParsingTestProject.Services
{
    public class GeminiService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        // Constructor - API key is injected via DI
        public GeminiService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
        }

        // List available models
        public async Task<string> ListModelsAsync()
        {
            var endpoint = $"https://generativelanguage.googleapis.com/v1/models?key={_apiKey}";
            var response = await _httpClient.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API Error: {response.StatusCode}\n{error}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        // Analyze PDF text using a valid model
        public async Task<string> AnalyzePdfTextAsync(string pdfText)
        {
            var modelName = "gemini-2.5-flash"; // Remove "models/" prefix if needed
            var endpoint = $"https://generativelanguage.googleapis.com/v1/models/{modelName}:generateContent?key={_apiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                            role = "user",
                            parts = new[]
                            {
                                new { text = $"Extract all assignments and due dates from this PDF text and return results in JSON format:\n\n{pdfText}" }
                            }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API Error: {response.StatusCode}\n{error}");
            }

            return await response.Content.ReadAsStringAsync();
        }
    }
}

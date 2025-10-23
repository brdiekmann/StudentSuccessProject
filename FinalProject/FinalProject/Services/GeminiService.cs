using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
namespace FinalProject.Services
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
                                //Test this new prompt it is looking to extract assignments and courses as well as meeding times so that it will be able to populate class meeting times as Events 
                                new { text = $@"
                                        You are analyzing a university syllabus. 
                                        Each syllabus corresponds to exactly ONE course.

                                        Extract:
                                        1. The course details.
                                        2. All assignments with due dates.
                                        3. Return only valid JSON — do not include explanations or markdown.

                                        JSON format:

                                        {{
                                            ""course"": {{
                                            ""courseName"": ""..."",
                                            ""courseDescription"": ""..."",
                                            ""startDate"": ""YYYY-MM-DD"",
                                            ""endDate"": ""YYYY-MM-DD"",
                                            ""meetingDays"": ""Monday,Wednesday"",
                                            ""startTime"": ""15:00"",
                                            ""endTime"": ""16:15"",
                                            ""location"": ""...""
                                        }},
                                            ""assignments"": [
                                        {{
                                            ""assignmentName"": ""..."",
                                            ""dueDate"": ""YYYY-MM-DD""
                                        }}
                                                ]
                                        }}

                                        Text:
                                        {pdfText}
                                        "}


                                /* Commenting this out to test new prompt
                                new { text = $"Extract all assignments and their due dates from this text. Return only valid JSON in this format:\n\n{{ \"assignments\": [{{ \"name\": \"...\", \"dueDate\": \"YYYY-MM-DD\" }}] }}\n\nText:\n{pdfText}" }
                                */
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

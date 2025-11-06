using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalProject.Data;
using FinalProject.Models;
using FinalProject.Models.Entities;

namespace FinalProject.Services
{
    public class SyllabusService
    {
        private readonly string _geminiApiKey;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SyllabusService> _logger;

        public SyllabusService(
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<SyllabusService> logger)
        {
            _geminiApiKey = configuration["GeminiApiKey"] ?? configuration["ApiKey"];
            _context = context;
            _logger = logger;
        }

        public async Task<SyllabusProcessResult> ProcessSyllabusAsync(
            IFormFile file,
            string userId,
            int scheduleId)
        {
            var result = new SyllabusProcessResult();

            try
            {
                _logger.LogInformation("Starting syllabus processing for user {UserId}", userId);

                // Validate file
                if (file == null || file.Length == 0)
                {
                    result.Success = false;
                    result.Message = "No file provided";
                    return result;
                }

                if (file.Length > 10 * 1024 * 1024) // 10MB limit
                {
                    result.Success = false;
                    result.Message = "File size exceeds 10MB limit";
                    return result;
                }

                // Extract text from file
                string syllabusText = await ExtractTextFromFileAsync(file);

                if (string.IsNullOrWhiteSpace(syllabusText))
                {
                    result.Success = false;
                    result.Message = "Could not extract text from file";
                    return result;
                }

                _logger.LogInformation("Extracted {Length} characters from syllabus", syllabusText.Length);

                // Parse with Gemini API
                var studyBlocks = await ParseSyllabusWithGeminiAsync(syllabusText);

                if (studyBlocks == null || !studyBlocks.Any())
                {
                    result.Success = false;
                    result.Message = "No study blocks could be generated from the syllabus";
                    return result;
                }

                _logger.LogInformation("Generated {Count} study blocks", studyBlocks.Count);

                // Create Event entities
                var events = new List<Event>();
                foreach (var block in studyBlocks)
                {
                    try
                    {
                        var newEvent = new Event
                        {
                            EventName = TruncateString(block.Title, 30),
                            EventDescription = TruncateString(block.Description ?? $"{block.EventType} event", 200),
                            StartDateTime = block.StartDate,
                            EndDateTime = block.EndDate,
                            Location = "TBD",
                            IsAllDay = false,
                            IsCancelled = false,
                            EventColor = GetColorForEventType(block.EventType),
                            attachedToCourse = false,
                            UserId = userId,
                            ScheduleId = scheduleId,
                            CourseId = null
                        };

                        _context.Events.Add(newEvent);
                        events.Add(newEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create event for block: {Title}", block.Title);
                        result.Errors.Add($"Could not create event: {block.Title}");
                    }
                }

                await _context.SaveChangesAsync();

                result.Success = true;
                result.Message = $"Successfully created {events.Count} study blocks and events";
                result.EventsCreated = events.Count;
                result.Events = events.Cast<object>().ToList(); // Cast to match result type

                _logger.LogInformation("Successfully saved {Count} events to database", events.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing syllabus");
                result.Success = false;
                result.Message = $"Error processing syllabus: {ex.Message}";
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        private string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private string GetColorForEventType(string? eventType)
        {
            return eventType?.ToLower() switch
            {
                "exam" => "#dc3545",        // Red
                "assignment" => "#ffc107",   // Yellow/Orange
                "study" => "#28a745",        // Green
                "project" => "#17a2b8",      // Cyan
                _ => "#007bff"               // Blue (default)
            };
        }

        private async Task<string> ExtractTextFromFileAsync(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName).ToLower();
            using var stream = file.OpenReadStream();

            return extension switch
            {
                ".pdf" => await ExtractTextFromPdfAsync(stream),
                ".docx" => ExtractTextFromDocx(stream),
                ".txt" => await new StreamReader(stream).ReadToEndAsync(),
                _ => throw new NotSupportedException($"File type {extension} is not supported. Please use PDF, DOCX, or TXT.")
            };
        }

        private async Task<string> ExtractTextFromPdfAsync(Stream stream)
        {
            var text = new StringBuilder();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            try
            {
                using var pdfReader = new PdfReader(memoryStream);
                using var pdfDocument = new PdfDocument(pdfReader);

                for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
                {
                    var page = pdfDocument.GetPage(i);
                    var strategy = new SimpleTextExtractionStrategy();
                    string pageText = PdfTextExtractor.GetTextFromPage(page, strategy);
                    text.AppendLine(pageText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF");
                throw new Exception("Could not read PDF file. The file may be corrupted or password-protected.", ex);
            }

            return text.ToString();
        }

        private string ExtractTextFromDocx(Stream stream)
        {
            var text = new StringBuilder();

            try
            {
                using var doc = WordprocessingDocument.Open(stream, false);
                var body = doc.MainDocumentPart?.Document?.Body;

                if (body != null)
                {
                    foreach (var paragraph in body.Elements<Paragraph>())
                    {
                        text.AppendLine(paragraph.InnerText);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from DOCX");
                throw new Exception("Could not read DOCX file. The file may be corrupted.", ex);
            }

            return text.ToString();
        }

        private async Task<List<StudyBlock>> ParseSyllabusWithGeminiAsync(string syllabusText)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(2);

            // Verify API key exists
            if (string.IsNullOrWhiteSpace(_geminiApiKey))
            {
                _logger.LogError("Gemini API key is not configured");
                throw new Exception("Gemini API key is not configured. Please check appsettings.json");
            }

            _logger.LogInformation("Using Gemini API key: {KeyPreview}...", _geminiApiKey.Substring(0, Math.Min(10, _geminiApiKey.Length)));

            var prompt = $@"Analyze this college syllabus and extract all important dates, assignments, exams, and projects. 
For each item, create study blocks leading up to deadlines.

Rules:
1. For exams: Create 3-5 study blocks in the week leading up to the exam (1-2 hours each)
2. For major assignments/projects: Create study blocks starting 1-2 weeks before due date
3. For regular assignments: Create 1-2 study blocks 2-3 days before due date
4. Each study block should be 1-2 hours long
5. Spread study blocks across different days (avoid cramming)
6. Only create events for future dates (after {DateTime.Now:yyyy-MM-dd})
7. If no specific times are mentioned, use reasonable study times (e.g., 14:00-16:00, 18:00-20:00)
8. Event titles must be 30 characters or less
9. Descriptions must be 200 characters or less

Return ONLY a valid JSON array with this exact structure (no markdown, no explanations):
[
  {{
    ""title"": ""Study for Midterm"",
    ""startDate"": ""2025-11-15T14:00:00"",
    ""endDate"": ""2025-11-15T16:00:00"",
    ""eventType"": ""study"",
    ""description"": ""Review chapters 1-5""
  }},
  {{
    ""title"": ""Assignment 1 Due"",
    ""startDate"": ""2025-11-20T23:59:00"",
    ""endDate"": ""2025-11-20T23:59:00"",
    ""eventType"": ""assignment"",
    ""description"": ""Submit on Canvas""
  }}
]

Event types must be one of: ""exam"", ""assignment"", ""study"", ""project""

Syllabus Text:
{syllabusText}";

            var requestBody = new
            {
                contents = new[]
                {
            new
            {
                parts = new[]
                {
                    new { text = prompt }
                }
            }
        },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 8192
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_geminiApiKey}";

            _logger.LogInformation("Calling Gemini API...");

            var response = await httpClient.PostAsync(apiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API error: Status {Status}, Response: {Error}", response.StatusCode, errorContent);

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new Exception("Gemini API key is invalid or doesn't have permission. Please check your API key in appsettings.json");
                }

                throw new Exception($"Gemini API error: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Received response from Gemini API");

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent, options);
            var generatedText = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "";

            // Clean markdown formatting
            generatedText = generatedText.Trim();
            if (generatedText.StartsWith("```json"))
                generatedText = generatedText.Substring(7);
            if (generatedText.StartsWith("```"))
                generatedText = generatedText.Substring(3);
            if (generatedText.EndsWith("```"))
                generatedText = generatedText.Substring(0, generatedText.Length - 3);
            generatedText = generatedText.Trim();

            _logger.LogInformation("Gemini response cleaned: {Length} characters", generatedText.Length);

            try
            {
                var studyBlocks = JsonSerializer.Deserialize<List<StudyBlock>>(generatedText, options);
                return studyBlocks ?? new List<StudyBlock>();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse Gemini response as JSON. Response: {Response}", generatedText);
                throw new Exception("Could not parse AI response. Please try again.", ex);
            }
        }
    }
}
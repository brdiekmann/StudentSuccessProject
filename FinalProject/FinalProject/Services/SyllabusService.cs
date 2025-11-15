using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalProject.Data;
using FinalProject.Models;
using FinalProject.Models.Entities;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

                // Parse with Gemini API (structured result)
                GeminiSyllabusResult parsedResult;

                try
                {
                    parsedResult = await ParseFullSyllabusWithGeminiAsync(syllabusText);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Gemini parsing failed, attempting fallback");

                    // Try fallback parsing
                    var fallbackEvents = CreateFallbackStudyBlocks(syllabusText);

                    if (fallbackEvents.Any())
                    {
                        // Create basic course with fallback
                        var fallbackCourse = new Course
                        {
                            CourseName = ExtractCourseName(syllabusText),
                            CourseDescription = "Automatically extracted course",
                            StartDate = DateOnly.FromDateTime(DateTime.Now),
                            EndDate = DateOnly.FromDateTime(DateTime.Now.AddMonths(4)),
                            ClassMeetingDays = "",
                            ClassStartTime = TimeOnly.Parse("10:00"),
                            ClassEndTime = TimeOnly.Parse("11:30"),
                            Location = "TBD",
                            CourseColor = "#007bff",
                            UserId = userId,
                            ScheduleId = scheduleId
                        };

                        result.RequiresUserInput = true;
                        result.Success = false;
                        result.Message = "AI parsing failed. Please fill in course details manually.";
                        result.CreatedCourse = fallbackCourse;
                        return result;
                    }

                    result.Success = false;
                    result.Message = $"Could not parse syllabus: {ex.Message}";
                    return result;
                }

                if (parsedResult == null || parsedResult.Course == null)
                {
                    result.Success = false;
                    result.Message = "Could not extract course information from syllabus.";
                    return result;
                }

                // ------------------- CREATE COURSE -------------------
                var courseData = parsedResult.Course;

                var course = new Course
                {
                    CourseName = string.IsNullOrWhiteSpace(courseData.CourseName) ? null : courseData.CourseName,
                    CourseDescription = string.IsNullOrWhiteSpace(courseData.CourseDescription) ? null : courseData.CourseDescription,
                    StartDate = ParseDateOrNull(courseData.StartDate),
                    EndDate = ParseDateOrNull(courseData.EndDate),
                    ClassMeetingDays = string.IsNullOrWhiteSpace(courseData.ClassMeetingDays) ? null : courseData.ClassMeetingDays,
                    ClassStartTime = ParseTimeOrNull(courseData.ClassStartTime),
                    ClassEndTime = ParseTimeOrNull(courseData.ClassEndTime),
                    Location = string.IsNullOrWhiteSpace(courseData.Location) ? "TBD" : courseData.Location,
                    CourseColor = string.IsNullOrWhiteSpace(courseData.CourseColor) ? "#007bff" : courseData.CourseColor,
                    UserId = userId,
                    ScheduleId = scheduleId
                };

                // If any required field is missing, prompt user input via modal
                if (string.IsNullOrWhiteSpace(course.CourseName)
                    || string.IsNullOrWhiteSpace(course.CourseDescription)
                    || !course.StartDate.HasValue
                    || !course.EndDate.HasValue
                    || string.IsNullOrWhiteSpace(course.ClassMeetingDays)
                    || !course.ClassStartTime.HasValue
                    || !course.ClassEndTime.HasValue)
                {
                    result.RequiresUserInput = true;
                    result.Success = false;
                    result.Message = "Some course details are missing. Please fill in the missing information.";
                    result.CreatedCourse = course;
                    return result;
                }

                // ------------------- SAVE COURSE -------------------
                _context.Courses.Add(course);
                await _context.SaveChangesAsync();
                result.CoursesCreated = 1;
                result.CreatedCourse = course;

                // ------------------- CREATE ASSIGNMENTS -------------------
                if (parsedResult.Assignments != null && parsedResult.Assignments.Any())
                {
                    foreach (var a in parsedResult.Assignments)
                    {
                        if (string.IsNullOrWhiteSpace(a.AssignmentName) || a.DueDate == null)
                        {
                            _logger.LogWarning("Skipping invalid assignment: {Name}", a.AssignmentName);
                            continue;
                        }

                        var assignment = new Assignment
                        {
                            AssignmentName = TruncateString(a.AssignmentName!, 100),
                            DueDate = a.DueDate.Value,
                            CourseId = course.Id,
                            IsCompleted = false
                        };
                        _context.Assignments.Add(assignment);
                        result.CreatedAssignments.Add(assignment);
                    }

                    if (result.CreatedAssignments.Any())
                    {
                        await _context.SaveChangesAsync();
                        result.AssignmentsCreated = result.CreatedAssignments.Count;
                    }
                }

                // ------------------- CREATE EVENTS -------------------
                if (parsedResult.Events != null && parsedResult.Events.Any())
                {
                    foreach (var block in parsedResult.Events)
                    {
                        try
                        {
                            var newEvent = new Event
                            {
                                EventName = TruncateString(block.Title ?? "Study Session", 30),
                                EventDescription = TruncateString(block.Description ?? $"{block.EventType} event", 200),
                                StartDateTime = block.StartDate,
                                EndDateTime = block.EndDate,
                                Location = course.Location ?? "TBD",
                                IsAllDay = false,
                                IsCancelled = false,
                                EventColor = GetColorForEventType(block.EventType),
                                attachedToCourse = true,
                                UserId = userId,
                                ScheduleId = scheduleId,
                                CourseId = course.Id
                            };

                            _context.Events.Add(newEvent);
                            result.Events.Add(newEvent);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to create event: {Title}", block.Title);
                        }
                    }

                    if (result.Events.Any())
                    {
                        await _context.SaveChangesAsync();
                        result.EventsCreated = result.Events.Count;
                    }
                }

                result.Success = true;
                result.Message = $"Successfully created {result.CoursesCreated} course, {result.AssignmentsCreated} assignments, and {result.EventsCreated} events.";
                _logger.LogInformation(result.Message);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing syllabus for user {UserId}", userId);
                result.Success = false;
                result.Message = $"An error occurred while processing the syllabus: {ex.Message}";
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        public async Task<SyllabusProcessResult> SaveCourseAsync(Course course)
        {
            var result = new SyllabusProcessResult();

            try
            {
                if (course == null)
                {
                    result.Success = false;
                    result.Message = "Course object is null";
                    return result;
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(course.CourseName)
                    || string.IsNullOrWhiteSpace(course.CourseDescription)
                    || !course.StartDate.HasValue
                    || !course.EndDate.HasValue
                    || string.IsNullOrWhiteSpace(course.ClassMeetingDays)
                    || !course.ClassStartTime.HasValue
                    || !course.ClassEndTime.HasValue)
                {
                    result.Success = false;
                    result.Message = "Missing required course information";
                    result.RequiresUserInput = true;
                    result.CreatedCourse = course;
                    return result;
                }

                if (course.Id > 0)
                {
                    _context.Courses.Update(course);
                }
                else
                {
                    _context.Courses.Add(course);
                }

                await _context.SaveChangesAsync();
                result.Success = true;
                result.Message = "Course saved successfully";
                result.CreatedCourse = course;
                result.CoursesCreated = 1;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving course");
                result.Success = false;
                result.Message = "An error occurred while saving the course";
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        // ==================== HELPER METHODS ====================

        private TimeOnly? ParseTimeOrNull(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            return TimeOnly.TryParse(input, out var result) ? result : null;
        }

        private DateOnly? ParseDateOrNull(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            return DateOnly.TryParse(input, out var result) ? result : null;
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
                "exam" => "#dc3545",
                "assignment" => "#ffc107",
                "study" => "#28a745",
                "project" => "#17a2b8",
                _ => "#007bff"
            };
        }

        // ==================== TEXT EXTRACTION ====================

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

        // ==================== GEMINI API PARSING ====================

        private async Task<GeminiSyllabusResult?> ParseFullSyllabusWithGeminiAsync(string syllabusText)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(2);

            if (string.IsNullOrWhiteSpace(_geminiApiKey))
                throw new Exception("Gemini API key is not configured.");

            _logger.LogInformation("Sending syllabus to Gemini API for parsing...");

            var prompt = $@"Analyze this college syllabus and extract structured data as JSON ONLY. No explanations, no markdown, just the JSON object.

CRITICAL RULES:
1. Return ONLY valid JSON - no text before or after
2. ONE course per syllabus
3. Extract ALL assignments, quizzes, exams with due dates
4. Generate study events for major items (exams, projects)
5. Use 'null' for unknown fields (as string, not bare null)
6. Only include dates AFTER {DateTime.Now:yyyy-MM-dd}
7. Event titles: max 30 characters
8. Descriptions: max 200 characters

EXACT JSON STRUCTURE:
{{
  ""course"": {{
    ""courseName"": ""CIS 375"",
    ""courseDescription"": ""System Analysis & Design"",
    ""startDate"": ""2024-01-09"",
    ""endDate"": ""2024-05-02"",
    ""classMeetingDays"": ""Tuesday,Thursday"",
    ""classStartTime"": ""10:30"",
    ""classEndTime"": ""11:45"",
    ""location"": ""Room 101"",
    ""courseColor"": ""#007bff""
  }},
  ""assignments"": [
    {{
      ""assignmentName"": ""Chapter 1 Quiz"",
      ""dueDate"": ""2024-01-18T23:59:00""
    }}
  ],
  ""events"": [
    {{
      ""title"": ""Study Ch 1-2"",
      ""startDate"": ""2024-01-16T14:00:00"",
      ""endDate"": ""2024-01-16T16:00:00"",
      ""eventType"": ""study"",
      ""description"": ""Prepare for quiz""
    }}
  ]
}}

Syllabus:
{syllabusText}";

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = 8192,
                    topK = 1,
                    topP = 0.95
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_geminiApiKey}";
            var response = await httpClient.PostAsync(apiUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {Status} - {Content}", response.StatusCode, responseContent);
                throw new Exception($"Gemini API error: {response.StatusCode}");
            }

            _logger.LogInformation("Received Gemini response, length: {Length}", responseContent.Length);

            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var generatedText = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "";

            _logger.LogInformation("Raw Gemini text (first 500 chars): {Text}",
                generatedText.Length > 500 ? generatedText.Substring(0, 500) + "..." : generatedText);

            if (string.IsNullOrWhiteSpace(generatedText))
            {
                _logger.LogError("Gemini returned empty text");
                throw new Exception("Gemini returned empty response");
            }

            // ==================== AGGRESSIVE CLEANING ====================
            var cleanedText = generatedText.Trim();

            // Remove markdown code blocks
            if (cleanedText.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                cleanedText = cleanedText.Substring(7);
            else if (cleanedText.StartsWith("```"))
                cleanedText = cleanedText.Substring(3);

            if (cleanedText.EndsWith("```"))
                cleanedText = cleanedText.Substring(0, cleanedText.Length - 3);

            cleanedText = cleanedText.Trim();

            // Find JSON object boundaries
            int jsonStart = cleanedText.IndexOf('{');
            int jsonEnd = cleanedText.LastIndexOf('}');

            if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
            {
                _logger.LogError("Could not find valid JSON object. Cleaned text: {Text}", cleanedText);
                throw new Exception("Gemini did not return valid JSON structure");
            }

            // Extract just the JSON object
            cleanedText = cleanedText.Substring(jsonStart, jsonEnd - jsonStart + 1);

            _logger.LogInformation("Final cleaned JSON (first 500 chars): {Text}",
                cleanedText.Length > 500 ? cleanedText.Substring(0, 500) + "..." : cleanedText);

            // ==================== PARSE RESULT ====================
            try
            {
                var result = JsonSerializer.Deserialize<GeminiSyllabusResult>(
                    cleanedText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (result == null || result.Course == null)
                {
                    _logger.LogError("Deserialized result is null or missing course");
                    throw new Exception("Gemini returned invalid course data");
                }

                _logger.LogInformation("✅ Successfully parsed syllabus: {CourseName}", result.Course.CourseName);
                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ JSON parsing failed. Content: {Content}", cleanedText);

                // Save to file for debugging
                try
                {
                    var debugPath = Path.Combine(Path.GetTempPath(), $"gemini_error_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    File.WriteAllText(debugPath, $"Original:\n{generatedText}\n\nCleaned:\n{cleanedText}");
                    _logger.LogError("Debug output saved to: {Path}", debugPath);
                }
                catch { }

                throw new Exception("Could not parse Gemini JSON response. Check logs for details.", ex);
            }
        }

        // ==================== FALLBACK PARSING ====================

        private List<StudyBlock> CreateFallbackStudyBlocks(string syllabusText)
        {
            _logger.LogInformation("Using fallback syllabus parser");

            var blocks = new List<StudyBlock>();
            var lines = syllabusText.Split('\n');

            // Regex for dates: MM/DD/YYYY or M/D/YY
            var datePattern = new Regex(@"(\d{1,2})/(\d{1,2})/(\d{2,4})");

            foreach (var line in lines)
            {
                var match = datePattern.Match(line);
                if (match.Success)
                {
                    try
                    {
                        var dateStr = match.Value;
                        if (DateTime.TryParse(dateStr, out var date) && date > DateTime.Now)
                        {
                            var lowerLine = line.ToLower();
                            string eventType = "study";

                            if (lowerLine.Contains("quiz"))
                                eventType = "assignment";
                            else if (lowerLine.Contains("exam") || lowerLine.Contains("test"))
                                eventType = "exam";
                            else if (lowerLine.Contains("project") || lowerLine.Contains("submission") || lowerLine.Contains("essay"))
                                eventType = "project";

                            var block = new StudyBlock
                            {
                                Title = TruncateString(line.Trim(), 30),
                                StartDate = date.Date.AddHours(23).AddMinutes(59),
                                EndDate = date.Date.AddHours(23).AddMinutes(59),
                                EventType = eventType,
                                Description = TruncateString(line.Trim(), 200)
                            };

                            blocks.Add(block);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse date from line: {Line}", line);
                    }
                }
            }

            _logger.LogInformation("Fallback parser found {Count} events", blocks.Count);
            return blocks;
        }

        private string ExtractCourseName(string syllabusText)
        {
            // Try to find course code like "CIS 375" or similar
            var coursePattern = new Regex(@"([A-Z]{2,4}\s*\d{3})", RegexOptions.IgnoreCase);
            var match = coursePattern.Match(syllabusText);

            if (match.Success)
                return match.Value.Trim();

            // Fallback: use first line
            var lines = syllabusText.Split('\n');
            return lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "Unknown Course";
        }
    }
}
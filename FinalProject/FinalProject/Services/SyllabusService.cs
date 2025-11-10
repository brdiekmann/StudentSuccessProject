using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalProject.Data;
using FinalProject.Models;
using FinalProject.Models.Entities;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text;
using System.Text.Json;

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
                var parsedResult = await ParseFullSyllabusWithGeminiAsync(syllabusText);

                if (parsedResult == null || parsedResult.Course == null)
                {
                    result.Success = false;
                    result.Message = "Could not extract course information from syllabus.";
                    return result;
                }

                // ------------------- CREATE COURSE -------------------
                var courseData = parsedResult.Course;

                bool missingInfo = string.IsNullOrWhiteSpace(courseData.CourseName)
                    || string.IsNullOrWhiteSpace(courseData.CourseDescription)
                    || string.IsNullOrWhiteSpace(courseData.StartDate)
                    || string.IsNullOrWhiteSpace(courseData.EndDate)
                    || string.IsNullOrWhiteSpace(courseData.ClassMeetingDays)
                    || string.IsNullOrWhiteSpace(courseData.ClassStartTime)
                    || string.IsNullOrWhiteSpace(courseData.ClassEndTime)
                    || string.IsNullOrWhiteSpace(courseData.CourseColor);

                if (missingInfo)
                {
                    result.RequiresUserInput = true;
                    result.Success = false;
                    result.Message = "Some course details are missing. Please fill in the missing information.";
                    result.CreatedCourse = new Course
                    {
                        CourseName = courseData.CourseName ?? "",
                        CourseDescription = courseData.CourseDescription ?? "",
                        StartDate = string.IsNullOrEmpty(courseData.StartDate) ? null : DateOnly.Parse(courseData.StartDate),
                        EndDate = string.IsNullOrEmpty(courseData.EndDate) ? null : DateOnly.Parse(courseData.EndDate),
                        ClassMeetingDays = courseData.ClassMeetingDays ?? "",
                        ClassStartTime = string.IsNullOrEmpty(courseData.ClassStartTime) ? null : TimeOnly.Parse(courseData.ClassStartTime),
                        ClassEndTime = string.IsNullOrEmpty(courseData.ClassEndTime) ? null : TimeOnly.Parse(courseData.ClassEndTime),
                        Location = courseData.Location,
                        CourseColor = courseData.CourseColor ?? "#007bff",
                        UserId = userId,
                        ScheduleId = scheduleId
                    };

                    return result; // Controller will prompt user
                }

                // ------------------- CHECK FOR MISSING COURSE INFO -------------------
                var course = new Course
                {
                    CourseName = string.IsNullOrWhiteSpace(courseData.CourseName) ? null : courseData.CourseName,
                    CourseDescription = string.IsNullOrWhiteSpace(courseData.CourseDescription) ? null : courseData.CourseDescription,
                    StartDate = ParseDateOrNull(courseData.StartDate),
                    EndDate = ParseDateOrNull(courseData.EndDate),
                    ClassMeetingDays = string.IsNullOrWhiteSpace(courseData.ClassMeetingDays) ? null : courseData.ClassMeetingDays,
                    ClassStartTime = ParseTimeOrNull(courseData.ClassStartTime),
                    ClassEndTime = ParseTimeOrNull(courseData.ClassEndTime),
                    Location = courseData.Location,
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
                    || !course.ClassEndTime.HasValue
                    || string.IsNullOrWhiteSpace(course.CourseColor))
                {
                    result.RequiresUserInput = true;
                    result.Success = false;
                    result.Message = "Some course details are missing. Please fill in the missing information.";
                    result.CreatedCourse = course;

                    return result; // Controller will trigger modal for user input
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
                            result.RequiresUserInput = true;
                            continue;
                        }

                        var assignment = new Assignment
                        {
                            AssignmentName = a.AssignmentName!,
                            DueDate = a.DueDate.Value,
                            CourseId = course.Id,
                            IsCompleted = false
                        };
                        _context.Assignments.Add(assignment);
                        result.CreatedAssignments.Add(assignment);
                    }
                    await _context.SaveChangesAsync();
                    result.AssignmentsCreated = result.CreatedAssignments.Count;
                }

                // ------------------- CREATE EVENTS -------------------
                if (parsedResult.Events != null)
                {
                    foreach (var block in parsedResult.Events)
                    {
                        var newEvent = new Event
                        {
                            EventName = TruncateString(block.Title, 30),
                            EventDescription = TruncateString(block.Description ?? $"{block.EventType} event", 200),
                            StartDateTime = block.StartDate,
                            EndDateTime = block.EndDate,
                            Location = course.Location,
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

                    await _context.SaveChangesAsync();
                    result.EventsCreated = result.Events.Count;
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
                result.Message = "An error occurred while processing the syllabus.";
                result.Errors.Add(ex.Message);
                return result;
            }
        }
        // Helper function
        private TimeOnly? ParseTimeOrNull(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLower() == "null")
                return null;

            return TimeOnly.TryParse(input, out var result) ? result : null;
        }

        private DateOnly? ParseDateOrNull(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLower() == "null")
                return null;

            return DateOnly.TryParse(input, out var result) ? result : null;
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
                    || !course.ClassEndTime.HasValue
                    || string.IsNullOrWhiteSpace(course.CourseColor))
                {
                    result.Success = false;
                    result.Message = "Missing required course information";
                    result.RequiresUserInput = true;
                    result.CreatedCourse = course;
                    return result;
                }

                // If course has an Id > 0, update; otherwise add new
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

        // NOTE: You have two Gemini helpers. The old one returns List<StudyBlock>.
        // I left it here in case you still use it elsewhere.
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

            var prompt = $@"
Analyze this college syllabus and extract the following structured data as JSON only.

Rules:
- There is only ONE course per syllabus.
- Include all key details like course name, meeting days/times, start/end dates, and location.
- Extract all assignments with due dates.
- Generate study events for exams and assignments leading up to dealines (Rules:
1. For exams: Create 3-5 study blocks in the week leading up to the exam (1-2 hours each)
2. For major assignments/projects: Create study blocks starting 1-2 weeks before due date
3. For regular assignments: Create 1-2 study blocks 2-3 days before due date
4. Each study block should be 1-2 hours long
5. Spread study blocks across different days (avoid cramming)
6. Only create events for future dates (after {DateTime.Now:yyyy-MM-dd})
7. If no specific times are mentioned, use reasonable study times (e.g., 14:00-16:00, 18:00-20:00)
8. Event titles must be 30 characters or less
9. Descriptions must be 200 characters or less)
- Omit any past dates (before {DateTime.Now:yyyy-MM-dd}).
- Use 'null' for unknown fields.

Return only JSON in this exact structure:
{{
  ""course"": {{
    ""courseName"": ""Example 101"",
    ""courseDescription"": ""Intro to Example Concepts"",
    ""startDate"": ""2025-01-20"",
    ""endDate"": ""2025-05-10"",
    ""classMeetingDays"": ""Monday, Wednesday"",
    ""classStartTime"": ""15:00"",
    ""classEndTime"": ""16:15"",
    ""location"": ""Room 210"",
    ""courseColor"": ""#007bff""
  }},
  ""assignments"": [
    {{
      ""assignmentName"": ""Essay 1"",
      ""dueDate"": ""2025-02-10T23:59:00""
    }},
    {{
      ""assignmentName"": ""Midterm Paper"",
      ""dueDate"": ""2025-03-15T23:59:00""
    }}
  ],
  ""events"": [
    {{
      ""title"": ""Study for Midterm"",
      ""startDate"": ""2025-03-10T14:00:00"",
      ""endDate"": ""2025-03-10T16:00:00"",
      ""eventType"": ""study"",
      ""description"": ""Review chapters 1-5""
    }}
  ]
}}

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

        private async Task<GeminiSyllabusResult?> ParseFullSyllabusWithGeminiAsync(string syllabusText)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(2);

            if (string.IsNullOrWhiteSpace(_geminiApiKey))
                throw new Exception("Gemini API key is not configured.");

            // ------------------- PROMPT -------------------
            var prompt = $@"
Analyze this college syllabus and extract the following structured data as JSON only.

Rules:
- There is only ONE course per syllabus.
- Include all key details like course name, meeting days/times, start/end dates, and location.
- Extract all assignments with due dates.
- Generate study events for exams and assignments (see rules in prompt)
- Omit any past dates (before {DateTime.Now:yyyy-MM-dd}).
- Use 'null' for unknown fields.

Return only JSON in this exact structure:
{{
  ""course"": {{
    ""courseName"": ""Example 101"",
    ""courseDescription"": ""Intro to Example Concepts"",
    ""startDate"": ""2025-01-20"",
    ""endDate"": ""2025-05-10"",
    ""classMeetingDays"": ""Monday, Wednesday"",
    ""classStartTime"": ""15:00"",
    ""classEndTime"": ""16:15"",
    ""location"": ""Room 210"",
    ""courseColor"": ""#007bff""
  }},
  ""assignments"": [
    {{
      ""assignmentName"": ""Essay 1"",
      ""dueDate"": ""2025-02-10T23:59:00""
    }}
  ],
  ""events"": [
    {{
      ""title"": ""Study for Midterm"",
      ""startDate"": ""2025-03-10T14:00:00"",
      ""endDate"": ""2025-03-10T16:00:00"",
      ""eventType"": ""study"",
      ""description"": ""Review chapters 1–5""
    }}
  ]
}}

Syllabus Text:
{syllabusText}";

            // ------------------- REQUEST -------------------
            var requestBody = new
            {
                contents = new[]
                {
            new { parts = new[] { new { text = prompt } } }
        },
                generationConfig = new { temperature = 0.2, maxOutputTokens = 8192 }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_geminiApiKey}";
            var response = await httpClient.PostAsync(apiUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Gemini API error: {response.StatusCode} - {responseContent}");

            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var generatedText = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "";

            // ------------------- 🧹 CLEANUP SNIPPET -------------------
            generatedText = generatedText.Trim();

            // Remove markdown code fences or labels like ```json
            if (generatedText.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                generatedText = generatedText.Substring(7);
            if (generatedText.StartsWith("```"))
                generatedText = generatedText.Substring(3);
            if (generatedText.EndsWith("```"))
                generatedText = generatedText.Substring(0, generatedText.Length - 3);

            // Find the first '{' in case Gemini added preamble text
            var jsonStart = generatedText.IndexOf('{');
            if (jsonStart > 0)
                generatedText = generatedText.Substring(jsonStart);

            generatedText = generatedText.Trim();

            // ------------------- PARSE RESULT -------------------
            try
            {
                var result = JsonSerializer.Deserialize<GeminiSyllabusResult>(
                    generatedText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (result == null)
                    throw new Exception("Gemini returned empty or invalid JSON.");

                _logger.LogInformation("✅ Successfully parsed Gemini structured syllabus response.");
                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "❌ Failed to parse Gemini structured response: {Response}", generatedText);
                throw new Exception("Could not parse Gemini structured response. Check logs for details.", ex);
            }
        }


    }

}

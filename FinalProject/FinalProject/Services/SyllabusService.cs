using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FinalProject.Data;
using FinalProject.Models;
using FinalProject.Models.Entities;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Signatures;
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
            _geminiApiKey = configuration["Gemini:ApiKey"];
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

                if (file.Length > 10 * 1024 * 1024)
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
                GeminiSyllabusResult parsedResult;

                try
                {
                    _logger.LogInformation("Syllabus text length: {Length}", syllabusText.Length);
                    parsedResult = await ParseFullSyllabusWithGeminiAsync(syllabusText);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("Syllabus text length: {Length}", syllabusText.Length);
                    _logger.LogError(ex, "Gemini parsing failed");
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

                // STORE THE PARSED DATA FOR LATER USE
                result.ParsedData = parsedResult;

                // Create Course
                var courseData = parsedResult.Course;
                int difficultyLevel = ExtractCourseDifficulty(courseData.CourseName);

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
                    DifficultyLevel = difficultyLevel, 
                    UserId = userId,
                    ScheduleId = scheduleId
                };

                // Check if course details are incomplete
                if (string.IsNullOrWhiteSpace(course.CourseName) ||
                    string.IsNullOrWhiteSpace(course.CourseDescription) ||
                    !course.StartDate.HasValue ||
                    !course.EndDate.HasValue ||
                    string.IsNullOrWhiteSpace(course.ClassMeetingDays) ||
                    !course.ClassStartTime.HasValue ||
                    !course.ClassEndTime.HasValue)
                {
                    result.RequiresUserInput = true;
                    result.Success = false;
                    result.Message = "Some course details are missing. Please fill in the missing information.";
                    result.CreatedCourse = course;
                    // ParsedData is already stored above
                    return result;
                }

                // Course is complete - save everything
                _context.Courses.Add(course);
                await _context.SaveChangesAsync();
                result.CoursesCreated = 1;
                result.CreatedCourse = course;

                // Create Assignments
                if (parsedResult.Assignments != null && parsedResult.Assignments.Any())
                {
                    foreach (var a in parsedResult.Assignments)
                    {
                        if (string.IsNullOrWhiteSpace(a.AssignmentName) || a.DueDate == null)
                            continue;

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

                // Create Events (study blocks, exams, etc.)
                if (parsedResult.Events != null && parsedResult.Events.Any())
                {
                    _logger.LogInformation("Creating {Count} study blocks/events", parsedResult.Events.Count);

                    foreach (var block in parsedResult.Events)
                    {
                        try
                        {
                            // Log each study block being created
                            _logger.LogInformation("Creating study block: {Title}, Type: {Type}, Start: {Start}",
                                block.Title, block.EventType, block.StartDate);

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
                }



                // Create recurring class meeting events
                var classMeetingEvents = CreateRecurringClassEvents(course, userId, scheduleId);
                foreach (var evt in classMeetingEvents)
                {
                    _context.Events.Add(evt);
                    result.Events.Add(evt);
                }

                if (result.Events.Any())
                {
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
                result.Message = $"An error occurred while processing the syllabus: {ex.Message}";
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        private List<Event> CreateRecurringClassEvents(Course course, string userId, int scheduleId)
        {
            var events = new List<Event>();

            // Validate required fields before creating events
            if (string.IsNullOrWhiteSpace(course.ClassMeetingDays) ||
                !course.StartDate.HasValue ||
                !course.EndDate.HasValue ||
                !course.ClassStartTime.HasValue ||
                !course.ClassEndTime.HasValue)
            {
                _logger.LogWarning("Cannot create recurring class events - missing required course information");
                return events;
            }

            var meetingDays = course.ClassMeetingDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim())
                .ToList();

            var dayMap = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
    {
        { "Monday", DayOfWeek.Monday },
        { "Tuesday", DayOfWeek.Tuesday },
        { "Wednesday", DayOfWeek.Wednesday },
        { "Thursday", DayOfWeek.Thursday },
        { "Friday", DayOfWeek.Friday },
        { "Saturday", DayOfWeek.Saturday },
        { "Sunday", DayOfWeek.Sunday }
    };

            var validDays = meetingDays
                .Where(d => dayMap.ContainsKey(d))
                .Select(d => dayMap[d])
                .ToList();

            if (!validDays.Any())
            {
                _logger.LogWarning("No valid meeting days found in: {Days}", course.ClassMeetingDays);
                return events;
            }

            for (var date = course.StartDate.Value.ToDateTime(TimeOnly.MinValue);
                 date <= course.EndDate.Value.ToDateTime(TimeOnly.MinValue);
                 date = date.AddDays(1))
            {
                if (validDays.Contains(date.DayOfWeek))
                {
                    var startDateTime = date.Add(course.ClassStartTime.Value.ToTimeSpan());
                    var endDateTime = date.Add(course.ClassEndTime.Value.ToTimeSpan());

                    var classEvent = new Event
                    {
                        EventName = TruncateString($"{course.CourseName ?? "Class"} Meeting", 30),
                        EventDescription = TruncateString($"Class meeting for {course.CourseName ?? "course"}", 200),
                        StartDateTime = startDateTime,
                        EndDateTime = endDateTime,
                        Location = !string.IsNullOrWhiteSpace(course.Location) ? course.Location : "TBD",
                        EventColor = !string.IsNullOrWhiteSpace(course.CourseColor) ? course.CourseColor : "#007bff",
                        IsAllDay = false,
                        IsCancelled = false,
                        attachedToCourse = true,
                        UserId = userId,
                        ScheduleId = scheduleId,
                        CourseId = course.Id
                    };

                    events.Add(classEvent);
                }
            }

            _logger.LogInformation("Created {Count} recurring class events", events.Count);
            return events;
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

        private string FixMalformedJson(string json)
        {
            _logger.LogInformation("Attempting to fix malformed JSON");

            // Count opening and closing brackets/braces
            int openBraces = json.Count(c => c == '{');
            int closeBraces = json.Count(c => c == '}');
            int openBrackets = json.Count(c => c == '[');
            int closeBrackets = json.Count(c => c == ']');

            _logger.LogInformation("JSON structure - Braces: {Open}/{Close}, Brackets: {OpenBracket}/{CloseBracket}",
                openBraces, closeBraces, openBrackets, closeBrackets);

            // Add missing closing brackets first (for arrays)
            while (openBrackets > closeBrackets)
            {
                json += "\n]";
                closeBrackets++;
                _logger.LogInformation("Added closing bracket");
            }

            // Then add missing closing braces (for objects)
            while (openBraces > closeBraces)
            {
                json += "\n}";
                closeBraces++;
                _logger.LogInformation("Added closing brace");
            }

            return json;
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
2. Find ALL assignments, exams, projects, quizzes, and due dates in the syllabus
3. Look for keywords like: 'due', 'exam', 'test', 'quiz', 'project', 'assignment', 'midterm', 'final'
4. Extract EVERY date mentioned with an associated task
5. Create study blocks for each assignment/exam found
6. For each assignment, create study blocks before the assignment's due datetime
7. For each exam, create a study block 2-3 days BEFORE the exam
8. Base study block duration on course difficulty
9. Make sure no study block times overlap
10. Use 'null' for unknown fields (as string, not bare null)
11. Only include dates AFTER {DateTime.Now:yyyy-MM-dd}
12. Event titles: max 30 characters
13. Descriptions: max 500 characters
14. ALWAYS close all brackets and braces
15. ALL datetime values MUST be in format: ""YYYY-MM-DDTHH:MM:SS"" (e.g., ""2025-11-15T23:59:00"")
16. If no time is specified, use 23:59:00 as the default time
17. DifficultyLevel should be 1-4 based on course number (100s=1, 200s=2, 300s=3, 400s=4)

IMPORTANT: Extract EVERY assignment, exam, quiz, and project mentioned. Don't skip any!

EXACT JSON STRUCTURE:
{{
  ""course"": {{
    ""courseName"": ""CS 101"",
    ""courseDescription"": ""Introduction to Programming"",
    ""startDate"": ""2025-08-25"",
    ""endDate"": ""2025-12-15"",
    ""classMeetingDays"": ""Monday,Wednesday,Friday"",
    ""classStartTime"": ""10:00"",
    ""classEndTime"": ""11:00"",
    ""location"": ""TBD"",
    ""courseColor"": ""#007bff"",
    ""DifficultyLevel"": 1
  }},
  ""assignments"": [
    {{
      ""assignmentName"": ""Assignment 1"",
      ""dueDate"": ""2025-11-01T23:59:00""
    }},
    {{
      ""assignmentName"": ""Midterm Exam"",
      ""dueDate"": ""2025-11-15T23:59:00""
    }},
    {{
      ""assignmentName"": ""Final Project"",
      ""dueDate"": ""2025-12-10T23:59:00""
    }}
  ],
  ""events"": [
    {{
      ""title"": ""Study for Assignment 1"",
      ""startDate"": ""2025-10-30T14:00:00"",
      ""endDate"": ""2025-10-30T16:00:00"",
      ""eventType"": ""study"",
      ""description"": ""Prepare for Assignment 1""
    }},
    {{
      ""title"": ""Study for Midterm"",
      ""startDate"": ""2025-11-13T14:00:00"",
      ""endDate"": ""2025-11-13T17:00:00"",
      ""eventType"": ""study"",
      ""description"": ""Review for midterm exam""
    }}
  ]
}}

IMPORTANT: Ensure the JSON is COMPLETE. Close all arrays with ] and all objects with }}

Syllabus:
{syllabusText}

Return ONLY the complete JSON object:";

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
                    //topK = 1,
                    topP = 0.95,
                    stopSequences = new string[] { } // Ensure no premature stopping
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_geminiApiKey}";
            var response = await httpClient.PostAsync(apiUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("❌ GEMINI BAD REQUEST");
                _logger.LogError("Status: {Status}", response.StatusCode);
                _logger.LogError("Body:\n{Body}", responseContent);

                throw new Exception($"Gemini API error: {response.StatusCode}. Body: {responseContent}");
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
            // ==================== PARSE RESULT ====================
            try
            {
                // First attempt: try to parse as-is
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
                _logger.LogWarning(ex, "First parse attempt failed, trying to fix JSON structure");

                try
                {
                    // Attempt to fix the JSON
                    var fixedJson = FixMalformedJson(cleanedText);

                    _logger.LogInformation("Fixed JSON (last 300 chars): {Text}",
                        fixedJson.Length > 300 ? "..." + fixedJson.Substring(fixedJson.Length - 300) : fixedJson);

                    var result = JsonSerializer.Deserialize<GeminiSyllabusResult>(
                        fixedJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (result == null || result.Course == null)
                    {
                        throw new Exception("Fixed JSON still contains invalid course data");
                    }

                    _logger.LogInformation("✅ Successfully parsed syllabus after fix: {CourseName}", result.Course.CourseName);
                    return result;
                }
                catch (JsonException innerEx)
                {
                    _logger.LogError(innerEx, "❌ JSON parsing failed even after fix attempt");

                    // Save to file for debugging
                    try
                    {
                        var debugPath = Path.Combine(Path.GetTempPath(), $"gemini_error_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                        File.WriteAllText(debugPath, $"Original:\n{generatedText}\n\nCleaned:\n{cleanedText}\n\nError:\n{innerEx.Message}");
                        _logger.LogError("Debug output saved to: {Path}", debugPath);
                    }
                    catch { }

                    throw new Exception($"Could not parse Gemini JSON response. Error at Line {innerEx.LineNumber}, Position {innerEx.BytePositionInLine}. Check logs for details.", innerEx);
                }
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
        private int ExtractCourseDifficulty(string courseName)
        {
            if (string.IsNullOrWhiteSpace(courseName))
                return 0;

            // Extract digits from the string (e.g., "CS 145" → "145")
            var digits = new string(courseName.Where(char.IsDigit).ToArray());

            if (!int.TryParse(digits, out int number))
                return 0;

            // Check ranges
            if (number >= 100 && number <= 199)
                return 1;

            if (number >= 200 && number <= 299)
                return 2;

            if (number >= 300 && number <= 399)
                return 3;

            if (number >= 400 && number <= 499)
                return 4;

            return 0;
        }

    }
}
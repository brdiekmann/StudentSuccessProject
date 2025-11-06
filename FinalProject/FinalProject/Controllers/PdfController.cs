using FinalProject.Models;
using FinalProject.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using FinalProject.Data;
using FinalProject.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.AspNetCore.Authorization;

namespace FinalProject.Controllers
{
    [Authorize]
    public class PdfController : Controller
    {
        private readonly GeminiService _geminiService;
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<PdfController> _logger;

        public PdfController(
            GeminiService geminiService,
            ApplicationDbContext dbContext,
            UserManager<User> userManager,
            ILogger<PdfController> logger)
        {
            _geminiService = geminiService;
            _dbContext = dbContext;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var schedules = _dbContext.Schedules
                .Where(s => s.UserId == user.Id)
                .ToList();

            var model = new SchedulesViewModel
            {
                Schedule = new Schedule(),
                ScheduleList = schedules
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> UploadSyllabus(IFormFile pdfFile, int scheduleId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["Error"] = "User not authenticated";
                return Unauthorized();
            }

            if (pdfFile == null || pdfFile.Length == 0)
            {
                TempData["Error"] = "No file uploaded";
                return BadRequest("No file uploaded.");
            }

            if (scheduleId <= 0)
            {
                TempData["Error"] = "Please select a valid schedule";
                return BadRequest("Invalid schedule ID.");
            }

            try
            {
                // Extract text from PDF
                string pdfText;
                using (var reader = new StreamReader(pdfFile.OpenReadStream()))
                {
                    pdfText = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(pdfText))
                {
                    TempData["Error"] = "Could not extract text from PDF";
                    return BadRequest("Could not extract text from PDF.");
                }

                _logger.LogInformation("Extracted {Length} characters from PDF", pdfText.Length);

                // Send to Gemini
                string apiResponse = await _geminiService.AnalyzePdfTextAsync(pdfText);

                // Parse Gemini response
                string candidateText;
                try
                {
                    using var doc = JsonDocument.Parse(apiResponse);
                    candidateText = doc.RootElement
                        .GetProperty("candidates")[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse Gemini response");
                    TempData["Error"] = "Gemini did not return valid JSON";
                    return BadRequest("Gemini did not return valid JSON.");
                }

                // Clean markdown
                candidateText = candidateText?.Trim();
                if (candidateText.StartsWith("```json"))
                    candidateText = candidateText.Substring(7).Trim();
                if (candidateText.EndsWith("```"))
                    candidateText = candidateText.Substring(0, candidateText.Length - 3).Trim();

                _logger.LogInformation("Cleaned Gemini response: {Response}", candidateText);

                using var result = JsonDocument.Parse(candidateText);

                // Extract Course with SAFE parsing
                var courseData = result.RootElement.GetProperty("course");

                // Helper function to safely get string values
                string GetStringOrNull(JsonElement element, string propertyName)
                {
                    if (element.TryGetProperty(propertyName, out var prop))
                    {
                        var value = prop.GetString();
                        return string.IsNullOrWhiteSpace(value) ? null : value;
                    }
                    return null;
                }

                // Get all course properties
                var courseName = GetStringOrNull(courseData, "courseName");
                var courseDescription = GetStringOrNull(courseData, "courseDescription");
                var startDateStr = GetStringOrNull(courseData, "startDate");
                var endDateStr = GetStringOrNull(courseData, "endDate");
                var meetingDays = GetStringOrNull(courseData, "meetingDays");
                var startTimeStr = GetStringOrNull(courseData, "startTime");
                var endTimeStr = GetStringOrNull(courseData, "endTime");
                var location = GetStringOrNull(courseData, "location");

                // Validate required fields
                if (string.IsNullOrWhiteSpace(courseName))
                {
                    TempData["Error"] = "Could not extract course name from syllabus";
                    return BadRequest("Could not extract course name from syllabus.");
                }

                if (string.IsNullOrWhiteSpace(startDateStr) ||
                    string.IsNullOrWhiteSpace(endDateStr) ||
                    string.IsNullOrWhiteSpace(startTimeStr) ||
                    string.IsNullOrWhiteSpace(endTimeStr))
                {
                    TempData["Error"] = "Could not extract complete course schedule from syllabus. Please ensure the syllabus contains clear dates and times.";
                    return BadRequest("Incomplete course schedule information.");
                }

                // Try to parse dates and times
                if (!DateOnly.TryParse(startDateStr, out var startDate))
                {
                    TempData["Error"] = $"Invalid start date format: {startDateStr}";
                    return BadRequest($"Invalid start date format: {startDateStr}");
                }

                if (!DateOnly.TryParse(endDateStr, out var endDate))
                {
                    TempData["Error"] = $"Invalid end date format: {endDateStr}";
                    return BadRequest($"Invalid end date format: {endDateStr}");
                }

                if (!TimeOnly.TryParse(startTimeStr, out var startTime))
                {
                    TempData["Error"] = $"Invalid start time format: {startTimeStr}";
                    return BadRequest($"Invalid start time format: {startTimeStr}");
                }

                if (!TimeOnly.TryParse(endTimeStr, out var endTime))
                {
                    TempData["Error"] = $"Invalid end time format: {endTimeStr}";
                    return BadRequest($"Invalid end time format: {endTimeStr}");
                }

                // Create Course entity
                var course = new Course
                {
                    CourseName = courseName,
                    CourseDescription = courseDescription ?? "No description provided",
                    StartDate = startDate,
                    EndDate = endDate,
                    ClassMeetingDays = meetingDays ?? "",
                    ClassStartTime = startTime,
                    ClassEndTime = endTime,
                    Location = location ?? "TBD",
                    ScheduleId = scheduleId,
                    UserId = user.Id,
                    CourseColor = "#4287f5"
                };

                _dbContext.Courses.Add(course);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Created course: {CourseName} with ID: {CourseId}", course.CourseName, course.Id);

                // Extract assignments
                int assignmentCount = 0;
                if (result.RootElement.TryGetProperty("assignments", out var assignments))
                {
                    foreach (var assignment in assignments.EnumerateArray())
                    {
                        var assignmentName = GetStringOrNull(assignment, "assignmentName");
                        var dueDateStr = GetStringOrNull(assignment, "dueDate");

                        if (!string.IsNullOrWhiteSpace(assignmentName) &&
                            !string.IsNullOrWhiteSpace(dueDateStr) &&
                            DateTime.TryParse(dueDateStr, out var dueDate))
                        {
                            var newAssignment = new Assignment
                            {
                                AssignmentName = assignmentName,
                                DueDate = dueDate,
                                IsCompleted = false,
                                CourseId = course.Id
                            };
                            _dbContext.Assignments.Add(newAssignment);
                            assignmentCount++;
                        }
                        else
                        {
                            _logger.LogWarning("Skipped invalid assignment: {Name}, {DueDate}", assignmentName, dueDateStr);
                        }
                    }

                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Created {Count} assignments", assignmentCount);
                }

                // Generate Events for class meetings
                int eventCount = 0;
                if (!string.IsNullOrWhiteSpace(meetingDays))
                {
                    var meetingDaysList = meetingDays
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

                    var validDays = meetingDaysList
                        .Where(d => dayMap.ContainsKey(d))
                        .Select(d => dayMap[d])
                        .ToList();

                    if (validDays.Any())
                    {
                        for (var date = course.StartDate.ToDateTime(TimeOnly.MinValue);
                             date <= course.EndDate.ToDateTime(TimeOnly.MinValue);
                             date = date.AddDays(1))
                        {
                            if (validDays.Contains(date.DayOfWeek))
                            {
                                var startDateTime = date.Add(course.ClassStartTime.ToTimeSpan());
                                var endDateTime = date.Add(course.ClassEndTime.ToTimeSpan());

                                var newEvent = new Event
                                {
                                    EventName = TruncateString($"{course.CourseName} Class", 30),
                                    EventDescription = TruncateString($"Class meeting for {course.CourseName}", 200),
                                    StartDateTime = startDateTime,
                                    EndDateTime = endDateTime,
                                    Location = course.Location ?? "TBD",
                                    EventColor = course.CourseColor,
                                    IsAllDay = false,
                                    IsCancelled = false,
                                    attachedToCourse = true,
                                    UserId = course.UserId,
                                    ScheduleId = course.ScheduleId,
                                    CourseId = course.Id
                                };

                                _dbContext.Events.Add(newEvent);
                                eventCount++;
                            }
                        }

                        await _dbContext.SaveChangesAsync();
                        _logger.LogInformation("Created {Count} class meeting events", eventCount);
                    }
                }

                TempData["Success"] = $"Successfully processed syllabus! Created course '{course.CourseName}' with {assignmentCount} assignments and {eventCount} class meetings.";
                return RedirectToAction("Result", "Pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing syllabus");
                TempData["Error"] = $"Error processing syllabus: {ex.Message}";
                return BadRequest($"Error: {ex.Message}");
            }
        }

        private string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
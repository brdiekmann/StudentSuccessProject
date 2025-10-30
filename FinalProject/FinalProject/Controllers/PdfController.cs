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



        public PdfController(GeminiService geminiService, ApplicationDbContext dbContext, UserManager<User> userManager)
        {
            _geminiService = geminiService;
            _dbContext = dbContext;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Get currently logged-in user
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            // Fetch schedules belonging to this user
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
                return Unauthorized();

            if (pdfFile == null || pdfFile.Length == 0)
                return BadRequest("No file uploaded.");

            string pdfText;
            using (var reader = new StreamReader(pdfFile.OpenReadStream()))
            {
                pdfText = await reader.ReadToEndAsync();
            }

            // 🔹 Send to Gemini
            string apiResponse = await _geminiService.AnalyzePdfTextAsync(pdfText);

            // 🔹 Parse Gemini response
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
            catch
            {
                return BadRequest("Gemini did not return valid JSON.");
            }

            candidateText = candidateText?.Trim();
            if (candidateText.StartsWith("```json"))
                candidateText = candidateText.Substring(7).Trim();
            if (candidateText.EndsWith("```"))
                candidateText = candidateText.Substring(0, candidateText.Length - 3).Trim();

            using var result = JsonDocument.Parse(candidateText);

            // 🔹 Extract Course
            var courseData = result.RootElement.GetProperty("course");
            var course = new Course
            {
                CourseName = courseData.GetProperty("courseName").GetString(),
                CourseDescription = courseData.GetProperty("courseDescription").GetString(),
                StartDate = DateOnly.Parse(courseData.GetProperty("startDate").GetString()),
                EndDate = DateOnly.Parse(courseData.GetProperty("endDate").GetString()),
                ClassMeetingDays = courseData.GetProperty("meetingDays").GetString(),
                ClassStartTime = TimeOnly.Parse(courseData.GetProperty("startTime").GetString()),
                ClassEndTime = TimeOnly.Parse(courseData.GetProperty("endTime").GetString()),
                Location = courseData.GetProperty("location").GetString(),
                ScheduleId = scheduleId, // ✅ selected by user
                UserId = user.Id,
                CourseColor = "#4287f5"
            };

            _dbContext.Courses.Add(course);
            await _dbContext.SaveChangesAsync();

            // 🔹 Extract assignments
            var assignmentList = new List<Assignment>();
            if (result.RootElement.TryGetProperty("assignments", out var assignments))
            {
                foreach (var assignment in assignments.EnumerateArray())
                {
                    var newAssignment = new Assignment
                    {
                        AssignmentName = assignment.GetProperty("assignmentName").GetString(),
                        DueDate = DateTime.Parse(assignment.GetProperty("dueDate").GetString()),
                        IsCompleted = false,
                        CourseId = course.Id
                    };
                    _dbContext.Assignments.Add(newAssignment);
                    assignmentList.Add(newAssignment);
                }

                await _dbContext.SaveChangesAsync();
            }

            // 🔹 Generate Events (the code we added earlier)
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
                        EventName = $"{course.CourseName} Class",
                        EventDescription = $"Class meeting for {course.CourseName}",
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
                }
            }

            await _dbContext.SaveChangesAsync();

            return RedirectToAction("Index", "Assignments");
        }
    }
}

/* Old Analyze Method testing out new PDF extraction and Gemini call
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
*/




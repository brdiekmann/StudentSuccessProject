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
using Microsoft.CodeAnalysis.Diagnostics;

namespace FinalProject.Controllers
{
    //Currently not in working condition because views have not been created yet, but logic should work properly
    public class PdfController : Controller
    {
        private readonly GeminiService _geminiService;
        private readonly ApplicationDbContext _dbContext;


        public PdfController(GeminiService geminiService, ApplicationDbContext dbContext)
        {
            _geminiService = geminiService;
            _dbContext = dbContext;

        }

        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> UploadSyllabus(IFormFile file, int scheduleId, string userId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            // 1️⃣ Extract text (placeholder — replace with PdfPig later)
            string pdfText;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                pdfText = await reader.ReadToEndAsync();
            }

            // 2️⃣ Send to Gemini
            string apiResponse = await _geminiService.AnalyzePdfTextAsync(pdfText);

            // 3️⃣ Extract inner text
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

            // Clean up JSON code block formatting
            candidateText = candidateText?.Trim();
            if (candidateText.StartsWith("```json"))
                candidateText = candidateText.Substring(7).Trim();
            if (candidateText.EndsWith("```"))
                candidateText = candidateText.Substring(0, candidateText.Length - 3).Trim();

            // 4️⃣ Parse JSON result
            using var result = JsonDocument.Parse(candidateText);

            // 5️⃣ Extract Course
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
                ScheduleId = scheduleId,
                UserId = userId,
                CourseColor = "#4287f5"
            };

            _dbContext.Courses.Add(course);
            await _dbContext.SaveChangesAsync();

            // 6️⃣ Extract and save Assignments
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

            // 7️⃣ Build AssignmentsViewModel
            var model = new AssignmentsViewModel
            {
                Assignment = null,
                AssignmentList = assignmentList
            };

            // 8️⃣ Redirect to the Assignments table view
            return View("Result", );
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




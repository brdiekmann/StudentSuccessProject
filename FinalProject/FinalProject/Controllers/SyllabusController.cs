using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FinalProject.Models;
using FinalProject.Models.Entities;
using FinalProject.Data;
using FinalProject.Services;

namespace FinalProject.Controllers
{
    [Authorize]
    public class SyllabusController : Controller
    {
        private readonly SyllabusService _syllabusService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<SyllabusController> _logger;

        public SyllabusController(
            SyllabusService syllabusService,
            ApplicationDbContext context,
            UserManager<User> userManager,
            ILogger<SyllabusController> logger)
        {
            _syllabusService = syllabusService;
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: Syllabus/Calendar
        [HttpGet]
        public async Task<IActionResult> Calendar()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var schedules = await _context.Schedules
                .Where(s => s.UserId == user.Id && s.IsActive)
                .ToListAsync();

            ViewBag.Schedules = schedules;
            return View();
        }

        // PUT: Syllabus/UpdateEvent/5
        [HttpPut]
        public async Task<IActionResult> UpdateEvent(int id, [FromBody] EventUpdateDto eventData)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var eventEntity = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == user.Id);

            if (eventEntity == null)
                return NotFound("Event not found");

            try
            {
                // Update event properties
                eventEntity.EventName = eventData.EventName?.Substring(0, Math.Min(30, eventData.EventName.Length)) ?? eventEntity.EventName;
                eventEntity.EventDescription = eventData.EventDescription?.Substring(0, Math.Min(200, eventData.EventDescription.Length)) ?? eventEntity.EventDescription;
                eventEntity.StartDateTime = eventData.StartDateTime;
                eventEntity.EndDateTime = eventData.EndDateTime;
                eventEntity.Location = eventData.Location?.Substring(0, Math.Min(50, eventData.Location.Length)) ?? eventEntity.Location;
                eventEntity.IsAllDay = eventData.IsAllDay;
                eventEntity.EventColor = eventData.EventColor ?? eventEntity.EventColor;

                await _context.SaveChangesAsync();

                return Ok(new { message = "Event updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating event {EventId}", id);
                return StatusCode(500, "Error updating event");
            }
        }

        // DELETE: Syllabus/DeleteEvent/5
        [HttpDelete]
        public async Task<IActionResult> DeleteEvent(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var eventEntity = await _context.Events
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == user.Id);

            if (eventEntity == null)
                return NotFound("Event not found");

            try
            {
                _context.Events.Remove(eventEntity);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Event deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting event {EventId}", id);
                return StatusCode(500, "Error deleting event");
            }
        }


        // API: Upload Syllabus
        [HttpPost]
        public async Task<IActionResult> UploadSyllabus(IFormFile syllabusFile, int scheduleId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized(new { success = false, message = "User not authenticated" });

            if (syllabusFile == null || syllabusFile.Length == 0)
                return BadRequest(new { success = false, message = "No file uploaded" });

            if (scheduleId <= 0)
                return BadRequest(new { success = false, message = "Please select a valid schedule" });

            try
            {
                var result = await _syllabusService.ProcessSyllabusAsync(
                    syllabusFile,
                    user.Id,
                    scheduleId
                );

                // Check if some course details are missing
                if (result.RequiresUserInput && result.CreatedCourse != null)
                {
                    var course = result.CreatedCourse;

                    return Ok(new
                    {
                        success = false,
                        message = "⚠️ Some course details are missing. Please fill in the missing information.",
                        requiresUserInput = true,
                        createdCourse = new
                        {
                            course.CourseName,
                            course.CourseDescription,
                            startDate = course.StartDate?.ToString("yyyy-MM-dd") ?? "",
                            endDate = course.EndDate?.ToString("yyyy-MM-dd") ?? "",
                            course.ClassMeetingDays,
                            classStartTime = course.ClassStartTime?.ToString("HH:mm") ?? "",
                            classEndTime = course.ClassEndTime?.ToString("HH:mm") ?? "",
                            course.Location,
                            course.CourseColor
                        }
                    });
                }

                // Normal success
                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = result.Message,
                        eventsCreated = result.EventsCreated
                    });
                }

                // Normal failure
                return BadRequest(new
                {
                    success = false,
                    message = result.Message,
                    errors = result.Errors
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing syllabus upload");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while processing your syllabus"
                });
            }
        }

        public async Task<IActionResult> SaveCourseFromModal([FromBody] CourseModalDto courseDto)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized(new { success = false, message = "User not authenticated" });

            try
            {
                var course = new Course
                {
                    CourseName = courseDto.CourseName,
                    CourseDescription = courseDto.CourseDescription,
                    StartDate = string.IsNullOrWhiteSpace(courseDto.StartDate) ? null : DateOnly.Parse(courseDto.StartDate),
                    EndDate = string.IsNullOrWhiteSpace(courseDto.EndDate) ? null : DateOnly.Parse(courseDto.EndDate),
                    ClassMeetingDays = courseDto.ClassMeetingDays,
                    ClassStartTime = string.IsNullOrWhiteSpace(courseDto.ClassStartTime) ? null : TimeOnly.Parse(courseDto.ClassStartTime),
                    ClassEndTime = string.IsNullOrWhiteSpace(courseDto.ClassEndTime) ? null : TimeOnly.Parse(courseDto.ClassEndTime),
                    Location = courseDto.Location,
                    CourseColor = courseDto.CourseColor,
                    ScheduleId = courseDto.ScheduleId,
                    UserId = user.Id
                };

                if (course.StartDate.HasValue && course.EndDate.HasValue)
                {
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    course.IsActive = today >= course.StartDate.Value && today <= course.EndDate.Value;
                }
                else
                {
                    course.IsActive = false;
                }

                var result = await _syllabusService.SaveCourseAsync(course);

                if (result.Success)
                    return Ok(new { success = true, message = result.Message, course = result.CreatedCourse });
                else if (result.RequiresUserInput)
                    return BadRequest(new { success = false, message = result.Message, course = result.CreatedCourse });
                else
                    return StatusCode(500, new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving course from modal");
                return StatusCode(500, new { success = false, message = "Error saving course details." });
            }
        }

        // POST: Syllabus/CompleteCourseDetails
        [HttpPost]
        public async Task<IActionResult> CompleteCourseDetails([FromBody] CourseCompletionDto courseData)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                    return Unauthorized(new { success = false, message = "User not authenticated" });

                // Validate required fields
                if (string.IsNullOrWhiteSpace(courseData.CourseName) ||
                    string.IsNullOrWhiteSpace(courseData.CourseDescription) ||
                    string.IsNullOrWhiteSpace(courseData.StartDate) ||
                    string.IsNullOrWhiteSpace(courseData.EndDate) ||
                    string.IsNullOrWhiteSpace(courseData.ClassMeetingDays) ||
                    string.IsNullOrWhiteSpace(courseData.ClassStartTime) ||
                    string.IsNullOrWhiteSpace(courseData.ClassEndTime))
                {
                    return BadRequest(new { success = false, message = "Missing required course information" });
                }

                // Parse dates and times
                if (!DateOnly.TryParse(courseData.StartDate, out var startDate) ||
                    !DateOnly.TryParse(courseData.EndDate, out var endDate) ||
                    !TimeOnly.TryParse(courseData.ClassStartTime, out var startTime) ||
                    !TimeOnly.TryParse(courseData.ClassEndTime, out var endTime))
                {
                    return BadRequest(new { success = false, message = "Invalid date or time format" });
                }

                // Create the Course entity
                var course = new Course
                {
                    CourseName = courseData.CourseName,
                    CourseDescription = courseData.CourseDescription,
                    StartDate = startDate,
                    EndDate = endDate,
                    ClassMeetingDays = courseData.ClassMeetingDays,
                    ClassStartTime = startTime,
                    ClassEndTime = endTime,
                    Location = string.IsNullOrWhiteSpace(courseData.Location) ? "TBD" : courseData.Location,
                    CourseColor = string.IsNullOrWhiteSpace(courseData.CourseColor) ? "#007bff" : courseData.CourseColor,
                    UserId = user.Id,
                    ScheduleId = courseData.ScheduleId
                };

                _context.Courses.Add(course);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created course {CourseName} with ID {CourseId}", course.CourseName, course.Id);

                int totalEventsCreated = 0;
                int assignmentsCreated = 0;

                // If we have the parsed data, create assignments and study events
                if (courseData.ParsedAssignments != null && courseData.ParsedAssignments.Any())
                {
                    foreach (var a in courseData.ParsedAssignments)
                    {
                        if (string.IsNullOrWhiteSpace(a.AssignmentName) || !a.DueDate.HasValue)
                            continue;

                        var assignment = new Assignment
                        {
                            AssignmentName = TruncateString(a.AssignmentName, 100),
                            DueDate = a.DueDate.Value,
                            CourseId = course.Id,
                            IsCompleted = false
                        };
                        _context.Assignments.Add(assignment);
                        assignmentsCreated++;
                    }
                }

                // Create study blocks, exam events, etc. from parsed data
                if (courseData.ParsedEvents != null && courseData.ParsedEvents.Any())
                {
                    foreach (var block in courseData.ParsedEvents)
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
                                UserId = user.Id,
                                ScheduleId = course.ScheduleId,
                                CourseId = course.Id
                            };

                            _context.Events.Add(newEvent);
                            totalEventsCreated++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to create event: {Title}", block.Title);
                        }
                    }
                }

                // Generate recurring class meeting events
                var meetingDays = course.ClassMeetingDays
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => d.Trim())
                    .ToList();

                if (meetingDays.Any())
                {
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

                    for (var date = course.StartDate.HasValue
                    ? course.StartDate.Value.ToDateTime(TimeOnly.MinValue)
                    : DateTime.MinValue;
                    date <= (course.EndDate.HasValue
                        ? course.EndDate.Value.ToDateTime(TimeOnly.MinValue)
                        : DateTime.MaxValue);
                    date = date.AddDays(1))
                    {
                        if (validDays.Contains(date.DayOfWeek))
                        {
                            var startDateTime = date.Add(course.ClassStartTime?.ToTimeSpan() ?? TimeSpan.Zero);
                            var endDateTime = date.Add(course.ClassEndTime?.ToTimeSpan() ?? TimeSpan.Zero);

                            var classEvent = new Event
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
                                UserId = user.Id,
                                ScheduleId = course.ScheduleId,
                                CourseId = course.Id
                            };

                            _context.Events.Add(classEvent);
                            totalEventsCreated++;
                        }
                    }
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Created {EventCount} events and {AssignmentCount} assignments for course {CourseName}",
                    totalEventsCreated, assignmentsCreated, course.CourseName);

                return Ok(new
                {
                    success = true,
                    message = $"Course saved successfully with {totalEventsCreated} events and {assignmentsCreated} assignments",
                    courseId = course.Id,
                    eventsCreated = totalEventsCreated,
                    assignmentsCreated = assignmentsCreated
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing course details");
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred while saving the course"
                });
            }
        }

        private string GetColorForEventType(string? eventType)
        {
            return eventType?.ToLower() switch
            {
                "exam" => "#dc3545",        // Red
                "assignment" => "#ffc107",   // Yellow
                "study" => "#28a745",        // Green
                "project" => "#17a2b8",      // Cyan
                _ => "#007bff"               // Blue
            };
        }

        private string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }




        // API: Get Events
        [HttpGet]
        public async Task<IActionResult> GetEvents(int? scheduleId = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized();

            var query = _context.Events.Where(e => e.UserId == user.Id);

            if (scheduleId.HasValue)
            {
                query = query.Where(e => e.ScheduleId == scheduleId.Value);
            }

            var events = await query
                .Select(e => new
                {
                    id = e.Id.ToString(),
                    title = e.EventName,
                    start = e.StartDateTime,
                    end = e.EndDateTime,
                    allDay = e.IsAllDay,
                    color = e.EventColor,
                    description = e.EventDescription,
                    location = e.Location
                })
                .ToListAsync();

            return Json(events);
        }
        

    }
}
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

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = result.Message,
                        eventsCreated = result.EventsCreated
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = result.Message,
                        errors = result.Errors
                    });
                }
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
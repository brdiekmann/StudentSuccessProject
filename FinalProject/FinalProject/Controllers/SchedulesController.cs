using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using FinalProject.Models.Entities;
using FinalProject.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FinalProject.Controllers
{
    [Authorize]
    public class SchedulesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<SchedulesController> _logger;

        public SchedulesController(
            ApplicationDbContext context,
            UserManager<User> userManager,
            ILogger<SchedulesController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: Schedules
        public async Task<IActionResult> Index()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                // REMOVED: && s.IsActive - this was causing the error
                var schedules = await _context.Schedules
                    .Where(s => s.UserId == user.Id)
                    .OrderByDescending(s => s.IsActive)
                    .ThenBy(s => s.Title)
                    .ToListAsync();

                return View(schedules);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading schedules");
                TempData["Error"] = "Error loading schedules. Please try again.";
                return View(new List<Schedule>());
            }
        }

        // GET: Schedules/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Schedules/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Schedule schedule)
        {
            try
            {
                // Remove properties that aren't from the form
                ModelState.Remove("UserId");
                ModelState.Remove("User");
                ModelState.Remove("IsActive"); // Keep this removal
                ModelState.Remove("Id");

                if (ModelState.IsValid)
                {
                    var user = await _userManager.GetUserAsync(User);
                    if (user == null)
                    {
                        TempData["Error"] = "User not authenticated";
                        return RedirectToAction("Login", "Account");
                    }

                    schedule.UserId = user.Id;

                    // REMOVED: schedule.IsActive = true; - don't set this if column doesn't exist

                    _context.Schedules.Add(schedule);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Schedule created successfully!";
                    return RedirectToAction(nameof(Index));
                }

                // Log validation errors
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    _logger.LogWarning("Validation error: {Error}", error.ErrorMessage);
                }

                return View(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating schedule");
                TempData["Error"] = "Error creating schedule. Please try again.";
                return View(schedule);
            }
        }

        // GET: Schedules/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);

                if (schedule == null)
                {
                    TempData["Error"] = "Schedule not found or you don't have permission to edit it.";
                    return RedirectToAction(nameof(Index));
                }

                return View(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading schedule for edit");
                TempData["Error"] = "Error loading schedule. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Schedules/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Schedule schedule)
        {
            if (id != schedule.Id)
            {
                return NotFound();
            }

            try
            {
                // Remove properties that aren't from the form
                ModelState.Remove("UserId");
                ModelState.Remove("User");
                ModelState.Remove("IsActive");

                if (ModelState.IsValid)
                {
                    var user = await _userManager.GetUserAsync(User);
                    if (user == null)
                    {
                        return RedirectToAction("Login", "Account");
                    }

                    var existing = await _context.Schedules
                        .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);

                    if (existing == null)
                    {
                        TempData["Error"] = "Schedule not found or you don't have permission to edit it.";
                        return RedirectToAction(nameof(Index));
                    }

                    // Update only the properties that exist and should be editable
                    existing.Title = schedule.Title;
                    existing.ScheduleDescription = schedule.ScheduleDescription;
                    existing.StartDateTime = existing.StartDateTime;
                    existing.EndDateTime = existing.EndDateTime;

                    // DON'T update IsActive since it doesn't exist in database

                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Schedule updated successfully!";
                    return RedirectToAction(nameof(Index));
                }

                return View(schedule);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency error updating schedule {Id}", id);
                TempData["Error"] = "The schedule was modified by another user. Please try again.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating schedule {Id}", id);
                TempData["Error"] = "Error updating schedule. Please try again.";
                return View(schedule);
            }
        }

        // GET: Schedules/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);

                if (schedule == null)
                {
                    TempData["Error"] = "Schedule not found or you don't have permission to delete it.";
                    return RedirectToAction(nameof(Index));
                }

                return View(schedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading schedule for deletion");
                TempData["Error"] = "Error loading schedule. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: Schedules/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return RedirectToAction("Login", "Account");
                }

                var schedule = await _context.Schedules
                    .FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);

                if (schedule != null)
                {
                    _context.Events.RemoveRange(_context.Events.Where(e => e.ScheduleId == schedule.Id));
                    _context.Assignments.RemoveRange(_context.Assignments.Where(a => a.course.ScheduleId == schedule.Id));
                    _context.Courses.RemoveRange(_context.Courses.Where(c => c.ScheduleId == schedule.Id));
                    _context.Schedules.Remove(schedule);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Schedule deleted successfully!";
                }
                else
                {
                    TempData["Error"] = "Schedule not found or you don't have permission to delete it.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting schedule {Id}", id);
                TempData["Error"] = "Error deleting schedule. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
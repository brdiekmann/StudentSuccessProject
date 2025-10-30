using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using FinalProject.Models.Entities;
using FinalProject.Data; // your DbContext namespace
using Microsoft.AspNetCore.Authorization;


namespace FinalProject.Controllers
{
    [Authorize]
    public class SchedulesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;

        public SchedulesController(ApplicationDbContext context, UserManager<User> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Schedules
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var schedules = _context.Schedules.Where(s => s.UserId == user.Id).ToList();

            return View(schedules);
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
            if (ModelState.IsValid)
            {
                var user = await _userManager.GetUserAsync(User);
                schedule.UserId = user.Id;
                schedule.IsActive = true;
                _context.Schedules.Add(schedule);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View(schedule);
        }

        // GET: Schedules/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var schedule = await _context.Schedules.FindAsync(id);
            if (schedule == null)
                return NotFound();

            return View(schedule);
        }

        // POST: Schedules/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Schedule schedule)
        {
            if (id != schedule.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                var existing = await _context.Schedules.FindAsync(id);
                if (existing == null)
                    return NotFound();

                existing.Title = schedule.Title;
                existing.ScheduleDescription = schedule.ScheduleDescription;
                existing.StartDateTime = schedule.StartDateTime;
                existing.EndDateTime = schedule.EndDateTime;

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(schedule);
        }

        // GET: Schedules/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var schedule = await _context.Schedules.FindAsync(id);
            if (schedule == null)
                return NotFound();

            return View(schedule);
        }

        // POST: Schedules/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var schedule = await _context.Schedules.FindAsync(id);
            if (schedule != null)
            {
                _context.Schedules.Remove(schedule);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}

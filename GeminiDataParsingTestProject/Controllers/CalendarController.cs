using GeminiDataParsingTestProject.Data;
using GeminiDataParsingTestProject.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class CalendarController : ControllerBase
{
    private readonly CalendarDbContext _context;

    public CalendarController(CalendarDbContext context)
    {
        _context = context;
    }

    // GET: api/calendar
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CalendarEvent>>> GetEvents()
    {
        return await _context.Events.ToListAsync();
    }

    // GET: api/calendar/5
    [HttpGet("{id}")]
    public async Task<ActionResult<CalendarEvent>> GetEvent(int id)
    {
        var calendarEvent = await _context.Events.FindAsync(id);
        if (calendarEvent == null)
        {
            return NotFound();
        }
        return calendarEvent;
    }

    // POST: api/calendar
    [HttpPost]
    public async Task<ActionResult<CalendarEvent>> CreateEvent(CalendarEvent calendarEvent)
    {
        _context.Events.Add(calendarEvent);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetEvent), new { id = calendarEvent.Id }, calendarEvent);
    }

    // PUT: api/calendar/5
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateEvent(int id, CalendarEvent calendarEvent)
    {
        if (id != calendarEvent.Id)
        {
            return BadRequest();
        }

        _context.Entry(calendarEvent).State = EntityState.Modified;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EventExists(id))
            {
                return NotFound();
            }
            throw;
        }

        return NoContent();
    }

    // DELETE: api/calendar/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var calendarEvent = await _context.Events.FindAsync(id);
        if (calendarEvent == null)
        {
            return NotFound();
        }

        _context.Events.Remove(calendarEvent);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool EventExists(int id)
    {
        return _context.Events.Any(e => e.Id == id);
    }
}

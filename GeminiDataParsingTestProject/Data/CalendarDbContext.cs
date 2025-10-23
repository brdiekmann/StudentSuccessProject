using System.Collections.Generic;
using GeminiDataParsingTestProject.Models;
using Microsoft.EntityFrameworkCore;

namespace GeminiDataParsingTestProject.Data
{
    public class CalendarDbContext : DbContext
    {
        public CalendarDbContext(DbContextOptions<CalendarDbContext> options) : base(options) { }

        public DbSet<CalendarEvent> Events { get; set; }
    }
}

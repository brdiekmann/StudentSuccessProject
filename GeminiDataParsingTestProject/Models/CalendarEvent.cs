namespace GeminiDataParsingTestProject.Models
{
    public class CalendarEvent
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public bool AllDay { get; set; }
        public string Color { get; set; }
    }
}

namespace GeminiDataParsingTestProject.Models
{
    public class SyllabusUploadResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int EventsCreated { get; set; }
        public List<CalendarEvent>? Events { get; set; }
    }
}

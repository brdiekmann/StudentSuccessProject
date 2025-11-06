namespace FinalProject.Models
{
    public class SyllabusProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int EventsCreated { get; set; }
        public List<object> Events { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}

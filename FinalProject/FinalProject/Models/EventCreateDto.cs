namespace FinalProject.Models
{
    public class EventCreateDto
    {
        public string EventName { get; set; }
        public string EventDescription { get; set; }
        public string EventType { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string Location { get; set; }
        public bool IsAllDay { get; set; }
        public string EventColor { get; set; }
        public int ScheduleId { get; set; }
    }
}

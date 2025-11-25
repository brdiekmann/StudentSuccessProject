namespace FinalProject.Models
{
    public class AddEventRequest
    {
        public int ScheduleId { get; set; }
        public string EventName { get; set; }
        public string EventDescription { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string Location { get; set; }
        public bool IsAllDay { get; set; }
        public string EventColor { get; set; }
        public bool AttachedToCourse { get; set; }
        public int? CourseId { get; set; }
    }
}

namespace FinalProject.Models
{
    public class EventUpdateDto
    {
        public int Id { get; set; }
        public string EventName { get; set; }
        public string EventDescription { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string Location { get; set; }
        public bool IsAllDay { get; set; }
        public string EventColor { get; set; }
    }
}

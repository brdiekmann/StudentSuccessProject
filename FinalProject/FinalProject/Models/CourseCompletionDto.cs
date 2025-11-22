namespace FinalProject.Models
{
    public class CourseCompletionDto
    {
        public string CourseName { get; set; }
        public string CourseDescription { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public string ClassMeetingDays { get; set; }
        public string ClassStartTime { get; set; }
        public string ClassEndTime { get; set; }
        public string Location { get; set; }
        public string CourseColor { get; set; }
        public int ScheduleId { get; set; }

        // Include the parsed assignments and events
        public List<ParsedAssignment> ParsedAssignments { get; set; }
        public List<StudyBlock> ParsedEvents { get; set; }
    }
}

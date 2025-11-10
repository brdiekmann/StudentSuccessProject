using FinalProject.Models.Entities;

namespace FinalProject.Models
{
    public class SyllabusProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> Errors { get; set; } = new();
        public int EventsCreated { get; set; }
        public int AssignmentsCreated { get; set; }
        public int CoursesCreated { get; set; }

        public Course? CreatedCourse { get; set; }
        public List<Assignment> CreatedAssignments { get; set; } = new();
        public List<Event> Events { get; set; } = new();
        public bool RequiresUserInput { get; set; } = false;
    }

}

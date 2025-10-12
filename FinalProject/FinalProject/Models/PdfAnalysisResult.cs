using FinalProject.Models.Entities;

namespace FinalProject.Models
{
    public class PdfAnalysisResult
    {
        public Assignment[] Assignments { get; set; }
        public Course[] Courses { get; set; }
        public Event[] Events { get; set; }
    }
}

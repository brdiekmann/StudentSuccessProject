namespace FinalProject.Models
{
    public class GeminiSyllabusResult
    {
        public GeminiCourse? Course { get; set; }
        public List<GeminiAssignment>? Assignments { get; set; }
        public List<StudyBlock>? Events { get; set; }
    }
}

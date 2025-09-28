using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinalProject.Models.Entities
{
    public class Assignment
    {
        [Key]
        public int Id { get; set; }
        [Required(ErrorMessage = "Assignment name is required"), MaxLength(50, ErrorMessage = "Assignment names must be less than 50 characters")]
        public string AssignmentName { get; set; }
        [Required(ErrorMessage = "Due date is required")]
        public DateTime DueDate { get; set; }
        [Required]
        public bool IsCompleted { get; set; } = false;

        //Foreign Keys
        [ForeignKey("courseId")]
        [Required(ErrorMessage = "CourseId is required")]
        public int CourseId { get; set; }
        public Course? course { get; set; }
    }
}

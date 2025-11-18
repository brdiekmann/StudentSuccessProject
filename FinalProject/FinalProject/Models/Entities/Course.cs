using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinalProject.Models.Entities
{
    public class Course
    {
        [Key]
        public int Id { get; set; }
        [Required(ErrorMessage = "Course name is required"), MaxLength(50, ErrorMessage = "Course names must be less than 50 characters")]
        public string CourseName { get; set; }
        [Required(ErrorMessage = "Course description is required"), MaxLength(200, ErrorMessage = "Course descriptions must be less than 200 characters")]
        public string CourseDescription { get; set; }
        [Required(ErrorMessage = "Start date is required")]
        public DateOnly? StartDate { get; set; }
        [Required(ErrorMessage = "End date is required")]
        public DateOnly? EndDate { get; set; }
        [Required(ErrorMessage = "Class meeting days are required"), MaxLength(50, ErrorMessage = "Class meeting days must be less than 50 characters")]
        public string ClassMeetingDays { get; set; }
        [Required(ErrorMessage = "Class start time is required")]
        public TimeOnly? ClassStartTime { get; set; }
        [Required(ErrorMessage = "Class end time is required")]
        public TimeOnly? ClassEndTime { get; set; }
        public string? Location { get; set; }
        public bool IsActive { get; set; }

        [Required (ErrorMessage = "Course color is required"), MinLength(6, ErrorMessage = "RGB Hex Code must be at least 6 digits"), MaxLength(8, ErrorMessage = "RGB Hex Code can not be larger than 8 digits")]
        public string CourseColor { get; set; }

        //Foreign Keys
        [ForeignKey("userId")]
        [Required(ErrorMessage = "UserId is required")]
        public string UserId { get; set; }
        public User? user { get; set; }

        [ForeignKey("scheduleId")]
        [Required(ErrorMessage = "ScheduleId is required")]
        public int ScheduleId { get; set; }
        public Schedule? schedule { get; set; }

    }
}

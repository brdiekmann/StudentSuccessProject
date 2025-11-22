using System.ComponentModel.DataAnnotations;

namespace FinalProject.Models
{
    public class CourseModalDto
    {
        [Required(ErrorMessage = "Course name is required")]
        [MaxLength(50, ErrorMessage = "Course name must be less than 50 characters")]
        public string CourseName { get; set; }

        [Required(ErrorMessage = "Course description is required")]
        [MaxLength(200, ErrorMessage = "Course description must be less than 200 characters")]
        public string CourseDescription { get; set; }

        [Required(ErrorMessage = "Start date is required")]
        public string StartDate { get; set; } // yyyy-MM-dd from JS

        [Required(ErrorMessage = "End date is required")]
        public string EndDate { get; set; } // yyyy-MM-dd from JS

        [Required(ErrorMessage = "Class meeting days are required")]
        [MaxLength(50, ErrorMessage = "Class meeting days must be less than 50 characters")]
        public string ClassMeetingDays { get; set; }

        [Required(ErrorMessage = "Class start time is required")]
        public string ClassStartTime { get; set; } // HH:mm from JS

        [Required(ErrorMessage = "Class end time is required")]
        public string ClassEndTime { get; set; } // HH:mm from JS

        [Required(ErrorMessage = "Difficulty level is required")]
        public string DifficultyLevel { get; set; }

        public string Location { get; set; }


        [Required(ErrorMessage = "Course color is required")]
        [MinLength(6, ErrorMessage = "RGB Hex Code must be at least 6 digits")]
        [MaxLength(8, ErrorMessage = "RGB Hex Code can not be larger than 8 digits")]
        public string CourseColor { get; set; }

        [Required(ErrorMessage = "Schedule ID is required")]
        public int ScheduleId { get; set; }
    }
}

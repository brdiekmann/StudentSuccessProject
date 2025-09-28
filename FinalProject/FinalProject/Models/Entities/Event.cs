using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.Contracts;

namespace FinalProject.Models.Entities
{
    public class Event
    {
        [Key]
        public int Id { get; set; }
        [Required(ErrorMessage = "Event name is required"), MaxLength(30, ErrorMessage = "Event names must be less than 30 characters")]
        public string EventName { get; set; }
        [Required(ErrorMessage = "Event description is required"), MaxLength(200, ErrorMessage = "Event descriptions must be less than 200 characters")]
        public string EventDescription { get; set; }
        [Required(ErrorMessage = "Event start date and time is required")]
        public DateTime StartDateTime { get; set; }
        [Required(ErrorMessage = "Event end date and time is required")]
        public DateTime EndDateTime { get; set; }
        [Required(ErrorMessage = "Location is required"), MaxLength(50, ErrorMessage = "Location must be less than 50 characters")]
        public string Location { get; set; }
        [Required]
        bool IsAllDay { get; set; } = false;
        [Required]
        bool IsCanceled { get; set; } = true;
        [Required(ErrorMessage = "Course color is required"), MinLength(6, ErrorMessage = "RGB Hex Code must be at least 6 digits"), MaxLength(8, ErrorMessage = "RGB Hex Code can not be larger than 8 digits")]
        public string EventColor { get; set; }
        [Required]
        public bool attachedToCourse { get; set; } = false;

        //Foreign Keys
        [ForeignKey("userId")]
        [Required(ErrorMessage = "UserId is required")]
        public string UserId { get; set; }
        public User? user { get; set; }

        [ForeignKey("scheduleId")]
        [Required(ErrorMessage = "ScheduleId is required")]
        public int ScheduleId { get; set; }
        public Schedule? schedule { get; set; }

        [ForeignKey("courseId")]
        public int? CourseId { get; set; }
        public Course? course { get; set; }


    }
}

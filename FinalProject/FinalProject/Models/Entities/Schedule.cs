using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace FinalProject.Models.Entities
{
    public class Schedule
    {
        [Key]
        public int Id { get; set; }
        [Required(ErrorMessage = "Title is required"), MaxLength(30, ErrorMessage = "Titles must be less than 30 characters")]
        public string Title { get; set; }
        [Required(ErrorMessage = "Schedule description is required"), MaxLength(200, ErrorMessage = "Descriptions must be less than 200 characters")]
        public string ScheduleDescription { get; set; }
        [Required(ErrorMessage="Start date and time is required")]
        public DateTime StartDateTime { get; set; }
        [Required(ErrorMessage = "End date and time is required")]
        public DateTime EndDateTime { get; set; }
        [Required]
        bool IsActive { get; set; } = true;

        [ForeignKey("userId")]
        [Required(ErrorMessage = "UserId is required")]
        public string UserId { get; set; }
        public User? user { get; set; }
    }
}

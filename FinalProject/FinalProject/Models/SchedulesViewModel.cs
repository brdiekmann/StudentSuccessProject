using FinalProject.Models.Entities;
namespace FinalProject.Models
{
    public class SchedulesViewModel
    {
        public Schedule Schedule { get; set; } = new Schedule();
        public List<Schedule> ScheduleList { get; set; } = new List<Schedule>();
    }
}

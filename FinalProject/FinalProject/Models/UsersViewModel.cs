using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinalProject.Models
{
    public class UsersViewModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string UserName { get; set; }
        public string PasswordHash { get; set; }
        public string TimeZone { get; set; }
    }
}

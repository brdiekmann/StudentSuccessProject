using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
namespace FinalProject.Models.Entities
{
    public class User : IdentityUser
    {
        [Required(ErrorMessage = "Fist name is required."), StringLength(15)]
        public string? FirstName { get; set; }
        [Required(ErrorMessage = "Last name is required."), StringLength(15)]
        public string? LastName { get; set; }
        [Required(ErrorMessage = "Email is required."), StringLength(70)]
        public string? Email { get; set; }
        [Required(ErrorMessage = "Username is required."), StringLength(25)]
        public string UserName { get; set; }
        [Required(ErrorMessage = "Time zone is required."), StringLength(50)]

        public string TimeZone { get; set; }
    }
}

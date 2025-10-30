using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
namespace FinalProject.Models.Entities
{
    public class User : IdentityUser
    {
        [Required, StringLength(15)]
        public string FirstName { get; set; } = string.Empty;

        [Required, StringLength(15)]
        public string LastName { get; set; } = string.Empty;

        [Required, StringLength(70)]
        public override string Email { get; set; } = string.Empty;

        [Required, StringLength(25)]
        public override string UserName { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string TimeZone { get; set; } = string.Empty;
    }
}

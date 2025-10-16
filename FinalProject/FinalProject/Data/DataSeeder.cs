using FinalProject.Models.Entities;
using Microsoft.AspNetCore.Identity;

namespace FinalProject.Data
{
    public class DataSeeder
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _appDbContext;

        public DataSeeder(UserManager<User> userManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext appDbContext)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _appDbContext = appDbContext;
        }

        public async Task SeedDataAsync()
        {
            //Seed roles
            var roles = new[] {"User" };
            foreach (var role in roles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    await _roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            //Seed user

            var guestAccount = await _userManager.FindByNameAsync("user");
            if (guestAccount == null)
            {
                guestAccount = new User
                {
                    UserName = "user",
                    FirstName = "Regular",
                    LastName = "User",
                    Email = "user@example.com",
                    TimeZone = "Default TimeZone",
                    EmailConfirmed = true
                };

                var result = await _userManager.CreateAsync(guestAccount, "User@123");

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(guestAccount, "User"); // Assign Admin role to the user
                }
            }
        }
    }
}

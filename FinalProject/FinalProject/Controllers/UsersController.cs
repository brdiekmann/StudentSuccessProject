using FinalProject.Data;
using FinalProject.Models;
using FinalProject.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net.NetworkInformation;

namespace FinalProject.Controllers
{
    /* This contoller is used to manage user profiles, including viewing and editing profile details.
     * The users should be able to view their own profile information and update fields like FirstName, LastName, and TimeZone.
     */
    [Authorize]
    public class UsersController : Controller
    {
        private readonly UserManager<User> _userManager;

        public UsersController(UserManager<User> userManager)
        {
            _userManager = userManager;
        }

        // Show logged-in user's profile
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return NotFound();

            return View(currentUser);
        }

        // Edit profile details (not password or roles)
        [HttpPost]
        public async Task<IActionResult> Profile(User userViewModel)
        {
            if (!ModelState.IsValid) return View(userViewModel);

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            user.FirstName = userViewModel.FirstName;
            user.LastName = userViewModel.LastName;
            user.TimeZone = userViewModel.TimeZone;

            await _userManager.UpdateAsync(user);
            TempData["SuccessMessage"] = "Profile updated successfully!";

            return RedirectToAction("Profile");
        }
    }

}

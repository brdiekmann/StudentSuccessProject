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
    // This is not currently completed and still needs to be connected to views and edited to meet project requirements
    [Authorize]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext dbContext;
        private readonly UserManager<User> _userManager;
        public UsersController(ApplicationDbContext dbContext, UserManager<User> userManager)
        {
            this.dbContext = dbContext;
            this._userManager = userManager;
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult Add()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(User userViewModel)
        {
            if (!ModelState.IsValid) return View(userViewModel);

            var user = new User
            {
                FirstName = userViewModel.FirstName,
                LastName = userViewModel.LastName,
                Email = userViewModel.Email,
                UserName = userViewModel.UserName,
                TimeZone = userViewModel.TimeZone
            };

            var result = await _userManager.CreateAsync(user, "DefaultPassword123!");

            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = $"User {user.FirstName} (ID: {user.Id}) added successfully!";
                return RedirectToAction("List");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(userViewModel);
        }
    }
}

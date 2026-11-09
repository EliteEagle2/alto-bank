using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>View and update the user's name and email, and see past account applications.</summary>
[Authorize(Roles = SeedData.CustomerRole)] // customers only; admins use /admin
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IBankService _bank;

    public ProfileController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IBankService bank)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _bank = bank;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        return View(new ProfileViewModel
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email ?? "",
            Applications = await _bank.GetApplicationsAsync(user.Id)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        model.Applications = await _bank.GetApplicationsAsync(user.Id); // shown below the form either way
        if (!ModelState.IsValid) return View(model);

        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();

        // Email doubles as the username, so change both together.
        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var taken = await _userManager.FindByEmailAsync(model.Email);
            if (taken != null && taken.Id != user.Id)
            {
                ModelState.AddModelError(nameof(model.Email), "That email is already in use.");
                return View(model);
            }
            user.Email = model.Email;
            user.UserName = model.Email;
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        // Refresh the login cookie so the new name/email take effect immediately.
        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "Profile updated.";
        return RedirectToAction(nameof(Index));
    }
}

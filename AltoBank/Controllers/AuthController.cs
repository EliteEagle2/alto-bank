using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// Register, log in, and log out using ASP.NET Core Identity.
/// (Named AuthController so it isn't confused with AccountsController, which handles bank accounts.)
/// </summary>
public class AuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;     // creates/finds users
    private readonly SignInManager<ApplicationUser> _signInManager; // handles the login cookie
    private readonly IBankService _bank;
    private readonly IWebHostEnvironment _env;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
                          IBankService bank, IWebHostEnvironment env)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _bank = bank;
        _env = env;
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim()
        };

        // Identity hashes the password for us — we never store plain text.
        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        // New sign-ups are customers. Add the role BEFORE signing in so it's inside the login cookie.
        await _userManager.AddToRoleAsync(user, SeedData.CustomerRole);

        // Every new customer starts with an empty checking account.
        await _bank.OpenAccountAsync(user.Id, new OpenAccountRequest(
            AccountType.Checking, "Everyday Checking", CardDesigns.Aurora, null, null, 0m, null));

        await _signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = $"Welcome to Alto Bank, {user.FirstName}! Your checking account is ready.";
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        ViewData["ShowDemo"] = _env.IsDevelopment();   // the "Try the demo" buttons are Development-only
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["ShowDemo"] = _env.IsDevelopment();
        if (!ModelState.IsValid) return View(model);

        // UserName == Email in this app, so we can sign in with the email address.
        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // Only follow local return URLs (prevents "open redirect" attacks).
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) && model.ReturnUrl != "/")
                return Redirect(model.ReturnUrl);
            return await HomeFor(model.Email);
        }

        ModelState.AddModelError(string.Empty,
            result.IsLockedOut ? "Account locked after too many attempts. Try again later." : "Invalid email or password.");
        return View(model);
    }

    /// <summary>
    /// DEMO MODE: one-click login as a seeded user. Only works in the Development environment,
    /// so it can never be used on a deployed site.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoLogin(string who)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var email = who == "admin" ? SeedData.AdminEmail : SeedData.DemoEmail;
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            TempData["Error"] = "Demo user not found. Restart the app so the seeder can create it.";
            return RedirectToAction(nameof(Login));
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        TempData["Success"] = $"You're signed in as the demo {(who == "admin" ? "admin" : "customer")}, {user.FirstName}.";
        return await HomeFor(email);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    /// <summary>
    /// Styled "Access denied" page. Program.cs points the cookie's AccessDeniedPath here, so a
    /// customer who opens /admin (or an admin who opens /dashboard) lands on this page.
    /// </summary>
    [HttpGet]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        ViewData["Title"] = "Access denied";
        ViewData["IsAdminArea"] = returnUrl?.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) == true;
        return View();
    }

    // Admins land on the admin portal (/admin); customers on their own dashboard (/dashboard).
    private async Task<IActionResult> HomeFor(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user != null && await _userManager.IsInRoleAsync(user, SeedData.AdminRole))
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        return RedirectToAction("Index", "Dashboard", new { area = "" });
    }
}

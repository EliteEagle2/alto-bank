using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AltoBank.Controllers;

/// <summary>
/// PROFILE &amp; SETTINGS (/profile): one page, several sections, each with its own small form
/// (Post/Redirect/Get back to that section's #anchor with a TempData message):
///   Personal info · Security (password, sign out other sessions, recent sign-ins) · Alerts (what lights up the
///   bell, low-balance and large-purchase amounts) · Preferences (default account, privacy mode, theme, find me
///   by phone, paperless statements) · Applications.
/// Every change is audited. Antiforgery on every POST.
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[AutoValidateAntiforgeryToken]
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IBankService _bank;
    private readonly IPreferencesService _prefs;
    private readonly IAuditLogger _audit;

    public ProfileController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager,
                             IBankService bank, IPreferencesService prefs, IAuditLogger audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _bank = bank;
        _prefs = prefs;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        var prefs = await _prefs.GetAsync(user.Id);
        return View(await BuildPageAsync(user, prefs, new ProfileViewModel
        {
            FirstName = user.FirstName, LastName = user.LastName, Email = user.Email ?? "", PreferredName = prefs.PreferredName,
            Phone = user.PhoneNumber, Street = user.Street, Apt = user.Apt, City = user.City, State = user.State, Zip = user.Zip
        }));
    }

    // ---------- Personal info ----------
    [HttpPost]
    public async Task<IActionResult> Index([Bind(Prefix = "Personal")] ProfileViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        string? phone = null;
        if (!string.IsNullOrWhiteSpace(model.Phone))
        {
            var digits = ApplicationRules.NormalizePhone(model.Phone);
            if (digits == null) ModelState.AddModelError("Personal.Phone", "Enter a valid 10-digit U.S. mobile number.");
            else phone = ApplicationRules.FormatPhone(digits);
        }
        var state = string.IsNullOrWhiteSpace(model.State) ? null : model.State.Trim().ToUpperInvariant();
        if (state != null && !ApplicationOptions.States.ContainsKey(state)) ModelState.AddModelError("Personal.State", "Choose a state from the list.");
        if (!ModelState.IsValid) return View(await BuildPageAsync(user, await _prefs.GetAsync(user.Id), model));

        var before = new { user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.Street, user.Apt, user.City, user.State, user.Zip };
        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();
        user.PhoneNumber = phone;
        user.Street = Blank(model.Street); user.Apt = Blank(model.Apt); user.City = Blank(model.City);
        user.State = state; user.Zip = Blank(model.Zip);

        // Email doubles as the username, so change both together.
        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var taken = await _userManager.FindByEmailAsync(model.Email);
            if (taken != null && taken.Id != user.Id)
            {
                ModelState.AddModelError("Personal.Email", "That email is already in use.");
                return View(await BuildPageAsync(user, await _prefs.GetAsync(user.Id), model));
            }
            user.Email = model.Email.Trim();
            user.UserName = model.Email.Trim();
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(await BuildPageAsync(user, await _prefs.GetAsync(user.Id), model));
        }
        var named = await _prefs.SetPreferredNameAsync(user.Id, model.PreferredName);
        await _audit.WriteAsync("Profile.Update", "Updated personal information", "User", user.Id, before,
            new { user.FirstName, user.LastName, user.Email, user.PhoneNumber, user.Street, user.Apt, user.City, user.State, user.Zip });

        // Refresh the login cookie so the new name/email take effect immediately.
        await _signInManager.RefreshSignInAsync(user);
        TempData[named.Success ? "Success" : "Error"] = named.Success ? "Profile updated." : named.Message;
        return Section("personal");
    }

    // ---------- Security ----------
    [HttpPost("Profile/Password")]
    public async Task<IActionResult> Password(ChangePasswordViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!ModelState.IsValid) return Flash(false, ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage, "security");

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            await _audit.WriteAsync("Profile.PasswordChangeFailed", "Password change failed (wrong current password or weak new one)", "User", user.Id);
            return Flash(false, string.Join(" ", result.Errors.Select(e => e.Description)), "security");
        }
        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _audit.WriteAsync("Profile.PasswordChanged", "Changed password", "User", user.Id);
        await _signInManager.RefreshSignInAsync(user);   // this session stays signed in
        return Flash(true, "Password changed.", "security");
    }

    [HttpPost("Profile/SignOutOthers")]
    public async Task<IActionResult> SignOutOthers()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        // A new security stamp invalidates every other login cookie (checked on each request); then this
        // browser gets a fresh cookie with the new stamp so it stays signed in.
        await _userManager.UpdateSecurityStampAsync(user);
        await _signInManager.RefreshSignInAsync(user);
        await _audit.WriteAsync("Profile.SignOutOthers", "Signed out of all other sessions", "User", user.Id);
        return Flash(true, "Signed out everywhere else. This browser is still signed in.", "security");
    }

    // ---------- Alerts ----------
    [HttpPost("Profile/Alerts")]
    public async Task<IActionResult> Alerts(AlertSettingsInput input)
    {
        if (!ModelState.IsValid) return Flash(false, ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage, "alerts");
        var muted = Enum.GetValues<AlertCategory>().Where(c => c != AlertCategory.Security && !input.Enabled.Contains(c)).ToList();
        var r = await _prefs.SaveAlertsAsync(UserId, new AlertSettings(muted, input.LowBalanceAlert, input.LargeDebitAlert));
        return Flash(r.Success, r.Message, "alerts");
    }

    // ---------- Preferences ----------
    [HttpPost("Profile/Preferences")]
    public async Task<IActionResult> Preferences(PreferencesInput input)
    {
        if (!ModelState.IsValid) return Flash(false, "Check your choices and try again.", "preferences");
        var r = await _prefs.SavePreferencesAsync(UserId,
            new PreferenceSettings(input.DefaultAccountId, input.HideBalances, input.Theme, input.Paperless, input.DiscoverableByPhone));
        return Flash(r.Success, r.Message, "preferences");
    }

    // ---------- helpers ----------
    private string UserId => _userManager.GetUserId(User)!;

    private async Task<ProfilePageViewModel> BuildPageAsync(ApplicationUser user, UserPreferences prefs, ProfileViewModel personal)
    {
        ViewData["Title"] = "Profile & settings";
        personal.Applications = await _bank.GetApplicationsAsync(user.Id);
        var accounts = (await _bank.GetAccountsAsync(user.Id)).Where(a => a.Status != AccountStatus.Closed).ToList();

        var checks = new (bool Done, string Label)[]
        {
            (!string.IsNullOrEmpty(user.PhoneNumber), "mobile number"),
            (!string.IsNullOrEmpty(user.Street) && !string.IsNullOrEmpty(user.Zip), "mailing address"),
            (prefs.LowBalanceAlert != null || prefs.LargeDebitAlert != null, "a balance alert"),
            (prefs.DefaultAccountId != null, "a default account"),
            (prefs.Paperless, "paperless statements"),
        };
        return new ProfilePageViewModel
        {
            Personal = personal,
            Prefs = prefs,
            DateOfBirth = user.DateOfBirth,
            MemberSince = user.CreatedAt,
            AccountOptions = accounts.Select(a => new SelectListItem($"{a.DisplayName} ••{a.Last4}", a.Id.ToString(), a.Id == prefs.DefaultAccountId)).ToList(),
            States = ApplicationOptions.States.Select(s => new SelectListItem(s.Value, s.Key, s.Key == personal.State)).ToList(),
            RecentSignIns = await _prefs.RecentSignInsAsync(user.Id),
            CompletionPercent = 50 + 50 * checks.Count(c => c.Done) / checks.Length,   // name + email are always there
            Missing = checks.Where(c => !c.Done).Select(c => c.Label).ToList()
        };
    }

    private IActionResult Flash(bool ok, string message, string section)
    {
        TempData[ok ? "Success" : "Error"] = message;
        return Section(section);
    }

    private IActionResult Section(string section) => Redirect(Url.Action(nameof(Index))! + "#" + section);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

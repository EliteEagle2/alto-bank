using AltoBank.Data;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AltoBank.Controllers;

/// <summary>
/// Lists the user's bank accounts and runs the 7-step online account APPLICATION:
///   GET  /Accounts/Apply            → the application (one page; JavaScript shows one step at a time)
///   POST /Accounts/VerifyIdentity   → simulated identity check (called by JavaScript in step 5)
///   POST /Accounts/Apply            → ONE submit with every answer; re-validated here
///   GET  /Accounts/Decision/{id}    → "Approved!" screen (Post/Redirect/Get)
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)] // customers only; admins use /admin
[ApiExplorerSettings(IgnoreApi = true)]     // HTML pages — keep them out of the Swagger API docs
public class AccountsController : Controller
{
    private readonly IBankService _bank;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITimeLimitedDataProtector _identityProtector;

    public AccountsController(IBankService bank, UserManager<ApplicationUser> userManager, IDataProtectionProvider dataProtection)
    {
        _bank = bank;
        _userManager = userManager;
        // Used to sign the "identity verified" token so the browser can't fake it.
        _identityProtector = dataProtection.CreateProtector("AltoBank.IdentityCheck.v1").ToTimeLimitedDataProtector();
    }

    private string UserId => _userManager.GetUserId(User)!;

    // GET /Accounts
    public async Task<IActionResult> Index() => View(await _bank.GetAccountsAsync(UserId));

    // GET /accounts/5 — account details page. (Part 1: forwards to the history view; Part 3 builds the full page.)
    [HttpGet("accounts/{id:int}")]
    public IActionResult Details(int id) => RedirectToAction("History", "Transactions", new { id });

    // Old URL from earlier versions — send people to the new application.
    [HttpGet]
    public IActionResult Open() => RedirectToAction(nameof(Apply));

    // =====================================================================
    // GET /Accounts/Apply?type=Savings — prefilled from the profile and the last application
    // =====================================================================
    [HttpGet]
    public async Task<IActionResult> Apply(AccountType? type)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        var model = new AccountApplicationViewModel
        {
            AccountType = type,
            FirstName = user.FirstName,
            MiddleInitial = user.MiddleInitial,
            LastName = user.LastName,
            DobMonth = user.DateOfBirth?.Month,
            DobDay = user.DateOfBirth?.Day,
            DobYear = user.DateOfBirth?.Year,
            Phone = user.PhoneNumber,
            Street = user.Street, Apt = user.Apt, City = user.City, State = user.State, Zip = user.Zip,
        };

        // Answers that only live on applications (citizenship, employment...) come from the latest one.
        var last = await _bank.GetLatestApplicationAsync(user.Id);
        if (last != null)
        {
            model.Citizenship = last.Citizenship;
            model.LivedTwoYears = last.LivedAtAddressTwoYears;
            model.PrevStreet = last.PrevStreet; model.PrevCity = last.PrevCity;
            model.PrevState = last.PrevState; model.PrevZip = last.PrevZip;
            model.EmploymentStatus = last.EmploymentStatus;
            model.Employer = last.Employer; model.Occupation = last.Occupation;
            model.IncomeRange = last.IncomeRange;
            model.SourceOfFunds = last.SourceOfFunds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        await PrepareFormAsync(model, user);
        model.FundingAccountId = model.FundingAccounts.FirstOrDefault()?.Id; // default: fund from an Alto account
        return View(model);
    }

    // =====================================================================
    // POST /Accounts/VerifyIdentity — SIMULATED identity check (AJAX from step 5)
    // No SSN is collected. We invent 4 random digits, and return them with a signed,
    // time-limited token tied to this user. Apply() only accepts the application if the
    // token is valid, so skipping the check in the browser doesn't work.
    // =====================================================================
    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult VerifyIdentity()
    {
        var last4 = RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");
        var token = _identityProtector.Protect($"{UserId}|{last4}", TimeSpan.FromHours(2));
        return Json(new { last4, token });
    }

    // =====================================================================
    // POST /Accounts/Apply — re-validate EVERYTHING, then open the account
    // =====================================================================
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(AccountApplicationViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();
        await PrepareFormAsync(model, user);

        // [Required], [Range], [RegularExpression], [MinimumAge(18)] etc. already ran during model
        // binding. Now the rules that depend on several fields at once:
        var ssnLast4 = ValidateApplication(model, out var deposit, out var phoneDigits);

        if (!ModelState.IsValid) return View(model);

        var dob = model.DateOfBirth!.Value;
        // A readable label for where the opening deposit came from.
        string? fundingLabel = null;
        if (deposit > 0)
        {
            if (model.FundingAccountId is int fundingId)
            {
                var src = model.FundingAccounts.First(a => a.Id == fundingId);
                fundingLabel = $"{src.DisplayName} ••{src.Last4}";
            }
            else
            {
                fundingLabel = $"{model.ExternalBankName!.Trim()} ••{model.ExternalLast4}";
            }
        }

        var application = new AccountApplication
        {
            SubmittedAt = DateTime.UtcNow,
            AccountType = model.AccountType!.Value,
            Nickname = Trim(model.Nickname),
            FirstName = model.FirstName.Trim(),
            MiddleInitial = Trim(model.MiddleInitial)?.ToUpperInvariant(),
            LastName = model.LastName.Trim(),
            DateOfBirth = dob,
            Citizenship = model.Citizenship!,
            Email = user.Email!,                                  // always the login email
            Phone = ApplicationRules.FormatPhone(phoneDigits!),
            Street = model.Street!.Trim(), Apt = Trim(model.Apt), City = model.City!.Trim(),
            State = model.State!, Zip = model.Zip!,
            LivedAtAddressTwoYears = model.LivedTwoYears == true,
            PrevStreet = model.LivedTwoYears == true ? null : Trim(model.PrevStreet),
            PrevCity = model.LivedTwoYears == true ? null : Trim(model.PrevCity),
            PrevState = model.LivedTwoYears == true ? null : model.PrevState,
            PrevZip = model.LivedTwoYears == true ? null : model.PrevZip,
            EmploymentStatus = model.EmploymentStatus!,
            Employer = model.EmploymentStatus == "Employed" ? Trim(model.Employer) : null,
            Occupation = model.EmploymentStatus == "Employed" ? Trim(model.Occupation) : null,
            IncomeRange = model.IncomeRange!,
            SourceOfFunds = string.Join(", ", model.SourceOfFunds.Distinct()),
            SsnLast4 = ssnLast4!,
            OpeningDeposit = deposit,
            FundingSource = deposit > 0 ? (model.FundingAccountId == null ? $"External: {fundingLabel}" : fundingLabel) : null,
            ESignName = model.ESignName!.Trim(),
            ESignedAt = DateTime.UtcNow
        };

        var request = new OpenAccountRequest(
            application.AccountType, application.Nickname, model.CardDesign, null, null, deposit,
            deposit > 0 ? model.FundingAccountId : null,
            deposit > 0 && model.FundingAccountId == null ? fundingLabel : null);

        // BankService opens the account, moves the money, saves the application, and updates the
        // saved profile inside ONE database transaction (and re-checks the balance itself).
        var (result, saved) = await _bank.SubmitApplicationAsync(user.Id, application, request);
        if (!result.Success || saved == null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(model);
        }

        // TempData survives exactly one redirect (Post/Redirect/Get).
        // (A Pending application shows its own "We're reviewing" page, so no success banner there.)
        if (saved.Status == ApplicationStatus.Approved) TempData["Success"] = result.Message;
        TempData["AnimateDecision"] = true;
        return RedirectToAction(nameof(Decision), new { id = saved.Id });
    }

    // =====================================================================
    // GET /Accounts/Decision/5
    // =====================================================================
    [HttpGet]
    public async Task<IActionResult> Decision(int id)
    {
        // Ownership check: only the applicant's own application is found (IDOR protection).
        var app = await _bank.GetApplicationAsync(id, UserId);
        if (app == null)
        {
            TempData["Error"] = "We couldn't find that application.";
            return RedirectToAction(nameof(Index));
        }

        // Waiting for an admin (auto-approve is OFF) or denied: show the status page instead.
        if (app.Status != ApplicationStatus.Approved || app.Account == null)
            return View("ApplicationStatus", app);

        return View(new ApplicationDecisionViewModel
        {
            Application = app,
            Account = app.Account,
            OpeningBalance = app.OpeningDeposit,
            RoutingNumber = BankService.RoutingNumber,
            HolderName = $"{app.FirstName} {app.LastName}",
            Animate = TempData["AnimateDecision"] is true,
            UserKey = UserId
        });
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>
    /// Cross-field rules for the application. Adds ModelState errors and returns the
    /// simulated SSN last 4 from the identity token (null if the check wasn't done).
    /// </summary>
    private string? ValidateApplication(AccountApplicationViewModel m, out decimal deposit, out string? phoneDigits)
    {
        deposit = m.InitialDeposit ?? 0m;

        // ----- Step 1 -----
        if (m.AccountType == AccountType.Checking && !CardDesigns.All.Contains(m.CardDesign))
            ModelState.AddModelError(nameof(m.CardDesign), "Choose a debit card design.");

        // ----- Step 2 -----
        if (m.DobMonth != null && m.DobDay != null && m.DobYear != null)
        {
            if (m.DateOfBirth == null)
                ModelState.AddModelError(nameof(m.DateOfBirth), "Enter a real date of birth (MM / DD / YYYY).");
            else if (ApplicationRules.AgeOn(m.DateOfBirth.Value, DateOnly.FromDateTime(DateTime.Today)) > 120)
                ModelState.AddModelError(nameof(m.DateOfBirth), "Please check the year you were born.");
        }
        if (m.Citizenship != null && !ApplicationOptions.Citizenship.ContainsKey(m.Citizenship))
            ModelState.AddModelError(nameof(m.Citizenship), "Select your citizenship.");
        else if (m.Citizenship == "Other")
            ModelState.AddModelError(nameof(m.Citizenship), "We're sorry. This demo can only open accounts for U.S. citizens and permanent residents.");

        // ----- Step 3 -----
        phoneDigits = ApplicationRules.NormalizePhone(m.Phone);
        if (m.Phone != null && phoneDigits == null)
            ModelState.AddModelError(nameof(m.Phone), "Enter a valid 10-digit U.S. mobile number.");
        if (m.State != null && !ApplicationOptions.States.ContainsKey(m.State))
            ModelState.AddModelError(nameof(m.State), "Select your state.");
        if (m.LivedTwoYears == false)
        {
            if (string.IsNullOrWhiteSpace(m.PrevStreet)) ModelState.AddModelError(nameof(m.PrevStreet), "Enter your previous street address.");
            if (string.IsNullOrWhiteSpace(m.PrevCity)) ModelState.AddModelError(nameof(m.PrevCity), "Enter your previous city.");
            if (m.PrevState == null || !ApplicationOptions.States.ContainsKey(m.PrevState)) ModelState.AddModelError(nameof(m.PrevState), "Select your previous state.");
            if (string.IsNullOrWhiteSpace(m.PrevZip)) ModelState.AddModelError(nameof(m.PrevZip), "Enter your previous ZIP code.");
        }

        // ----- Step 4 -----
        if (m.EmploymentStatus != null && !ApplicationOptions.EmploymentStatuses.Contains(m.EmploymentStatus))
            ModelState.AddModelError(nameof(m.EmploymentStatus), "Select your employment status.");
        if (m.EmploymentStatus == "Employed")
        {
            if (string.IsNullOrWhiteSpace(m.Employer)) ModelState.AddModelError(nameof(m.Employer), "Enter your employer's name.");
            if (string.IsNullOrWhiteSpace(m.Occupation)) ModelState.AddModelError(nameof(m.Occupation), "Enter your occupation.");
        }
        if (m.IncomeRange != null && !ApplicationOptions.IncomeRanges.Contains(m.IncomeRange))
            ModelState.AddModelError(nameof(m.IncomeRange), "Select your annual income range.");
        if (m.SourceOfFunds.Count == 0)
            ModelState.AddModelError(nameof(m.SourceOfFunds), "Choose at least one source of funds.");
        else if (m.SourceOfFunds.Any(s => !ApplicationOptions.FundSources.Contains(s)))
            ModelState.AddModelError(nameof(m.SourceOfFunds), "Choose sources from the list.");

        // ----- Step 5 (simulated identity) -----
        var last4 = ReadIdentityToken(m.IdentityToken);
        m.VerifiedLast4 = last4;
        if (last4 == null)
            ModelState.AddModelError(nameof(m.IdentityToken), "Please verify your identity.");
        else
        {
            if (!ApplicationRules.SameText(m.QuizStreet, m.Street))
                ModelState.AddModelError(nameof(m.QuizStreet), "That street doesn't match the address you gave us.");
            if (m.QuizZip == null || m.QuizZip != m.Zip)
                ModelState.AddModelError(nameof(m.QuizZip), "That ZIP code doesn't match the address you gave us.");
        }

        // ----- Step 6 -----
        if (decimal.Round(deposit, 2) != deposit)
            ModelState.AddModelError(nameof(m.InitialDeposit), "Opening deposit can have at most 2 decimal places.");
        if (deposit > 0)
        {
            if (m.FundingAccountId is int sourceId)
            {
                var source = m.FundingAccounts.FirstOrDefault(a => a.Id == sourceId);
                if (source == null)
                    ModelState.AddModelError(nameof(m.FundingAccountId), "Choose one of your Alto accounts or an external bank.");
                else if (deposit > source.Balance)
                    ModelState.AddModelError(nameof(m.InitialDeposit), $"That's more than the {source.Balance:C} available in {source.DisplayName}.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(m.ExternalBankName) || m.ExternalBankName.Trim().Length < 2)
                    ModelState.AddModelError(nameof(m.ExternalBankName), "Enter the name of your other bank.");
                if (m.ExternalLast4 == null || !Regex.IsMatch(m.ExternalLast4, @"^\d{4}$"))
                    ModelState.AddModelError(nameof(m.ExternalLast4), "Enter the last 4 digits of that account.");
            }
        }

        // ----- Step 7 -----
        if (!m.AgreeAccountAgreement) ModelState.AddModelError(nameof(m.AgreeAccountAgreement), "Please agree to the Account Agreement.");
        if (!m.AgreePrivacy) ModelState.AddModelError(nameof(m.AgreePrivacy), "Please acknowledge the Privacy Notice.");
        if (!m.AgreeESign) ModelState.AddModelError(nameof(m.AgreeESign), "Please agree to the E-Sign Consent.");
        if (m.ESignName != null && !ApplicationRules.SignatureMatches(m.ESignName, m.FirstName, m.MiddleInitial, m.LastName))
            ModelState.AddModelError(nameof(m.ESignName), $"Your signature must match your name: {m.FirstName} {m.LastName}.");

        return last4;
    }

    // Returns the simulated SSN last 4 if the token is genuine, unexpired, and for this user.
    private string? ReadIdentityToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = _identityProtector.Unprotect(token).Split('|');
            return parts.Length == 2 && parts[0] == UserId && Regex.IsMatch(parts[1], @"^\d{4}$") ? parts[1] : null;
        }
        catch (CryptographicException)
        {
            return null; // tampered with or expired
        }
    }

    /// <summary>Loads what the form needs. Dropdown lists go in ViewBag (they're not part of the posted data).</summary>
    private async Task PrepareFormAsync(AccountApplicationViewModel model, ApplicationUser user)
    {
        // Only ACTIVE accounts can fund a new account (frozen/closed ones can't send money).
        model.FundingAccounts = (await _bank.GetAccountsAsync(user.Id)).Where(a => a.Status == AccountStatus.Active).ToList();
        model.Email = user.Email ?? "";
        model.IsReturning = user.HasApplicationProfile;
        model.UserKey = user.Id;
        model.VerifiedLast4 ??= ReadIdentityToken(model.IdentityToken);

        ViewBag.States = new SelectList(ApplicationOptions.States, "Key", "Value");
        ViewBag.EmploymentStatuses = new SelectList(ApplicationOptions.EmploymentStatuses);
        ViewBag.IncomeRanges = new SelectList(ApplicationOptions.IncomeRanges);
        ViewBag.FundSources = ApplicationOptions.FundSources;
        ViewBag.Citizenship = ApplicationOptions.Citizenship;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

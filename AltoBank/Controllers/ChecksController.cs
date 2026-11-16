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
/// MOBILE CHECK DEPOSIT — /checks
///   GET  /checks                         the form (amount, account, front + back photos, endorsement checklist), limits,
///                                        and your recent check deposits
///   POST /checks                         multipart/form-data upload → Pending deposit (Post/Redirect/Get to its status page)
///   GET  /checks/{id}                    status: Submitted → Approved / Rejected, with dates and the reason
///   GET  /checks/{id}/image/{side}       your own image ("front" / "back"), never cached
/// Another customer's deposit id returns 404 (every lookup is filtered by the signed-in customer).
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("checks")]
public class ChecksController : Controller
{
    // Two photos of up to 5 MB each, plus the form fields. Anything bigger is refused before it's read.
    public const long MaxRequestBytes = 2 * IFileStore.MaxBytes + 512 * 1024;

    private readonly ICheckDepositService _checks;
    private readonly UserManager<ApplicationUser> _userManager;

    public ChecksController(ICheckDepositService checks, UserManager<ApplicationUser> userManager)
    {
        _checks = checks;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(int? accountId) =>
        View(await BuildAsync(new CheckDepositForm { AccountId = accountId }));

    [HttpPost("")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> Submit([Bind(Prefix = "Form")] CheckDepositForm form)
    {
        if (ModelState.IsValid)
        {
            var (result, deposit) = await _checks.SubmitAsync(UserId, form.AccountId!.Value, form.Amount!.Value, form.Front, form.Back);
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Status), new { id = deposit!.Id });
            }
            ModelState.AddModelError(string.Empty, result.Message);
        }
        // Browsers never re-send files, so say so: the customer has to pick the photos again.
        ViewData["PhotosCleared"] = form.Front != null || form.Back != null;
        return View(nameof(Index), await BuildAsync(form));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Status(int id)
    {
        var deposit = await _checks.GetAsync(UserId, id);
        if (deposit == null) return NotFound();
        ViewData["Title"] = $"Check {deposit.Reference}";
        return View(deposit);
    }

    [HttpGet("{id:int}/image/{side}")]
    public async Task<IActionResult> Image(int id, string side)
    {
        var file = await _checks.OpenImageForCustomerAsync(UserId, id, side);
        if (file == null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Value.Stream, file.Value.ContentType);
    }

    private async Task<CheckDepositPageViewModel> BuildAsync(CheckDepositForm form)
    {
        ViewData["Title"] = "Deposit a check";
        var accounts = await _checks.GetEligibleAccountsAsync(UserId);
        // Default to the first checking account when nothing (or something not eligible) was asked for.
        if (form.AccountId == null || accounts.All(a => a.Id != form.AccountId))
            form.AccountId = accounts.FirstOrDefault(a => a.AccountType == AccountType.Checking)?.Id ?? accounts.FirstOrDefault()?.Id;
        return new CheckDepositPageViewModel
        {
            Form = form,
            Accounts = accounts.Select(a => new SelectListItem($"{a.DisplayName} ••{a.Last4} — {a.Balance:C}", a.Id.ToString(), a.Id == form.AccountId)).ToList(),
            Limits = await _checks.GetLimitsAsync(UserId),
            Recent = await _checks.ListAsync(UserId, take: 8)
        };
    }
}

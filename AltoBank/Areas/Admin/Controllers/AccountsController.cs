using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin/accounts — every account in the bank. Status changes and money adjustments all go through
/// IAdminService (transaction + audit log + required reason). Each POST redirects back (PRG).
/// </summary>
public class AccountsController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public AccountsController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] AccountListQuery query)
    {
        ViewData["Title"] = "Accounts";
        ViewBag.Query = query;
        return View(await _admin.SearchAccountsAsync(query));
    }

    public async Task<IActionResult> Details(int id)
    {
        var vm = await _admin.GetAccountAsync(id);
        if (vm == null) return NotFound();
        // An Alto Peak card has its own page (statements, limit, fee waivers, close at $0).
        if (vm.Account.IsCreditCard) return RedirectToAction("Details", "CreditCards", new { id });
        if (vm.Account.IsLoan) return RedirectToAction("Details", "Loans", new { id });
        ViewData["Title"] = $"{vm.Account.DisplayName} ••{vm.Account.Last4}";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Freeze(int id, string reason) => Done(id, await _admin.FreezeAccountAsync(id, reason));

    [HttpPost]
    public async Task<IActionResult> Unfreeze(int id, string reason) => Done(id, await _admin.UnfreezeAccountAsync(id, reason));

    [HttpPost]
    public async Task<IActionResult> Close(int id, int? moveTo, string reason) => Done(id, await _admin.CloseAccountAsync(id, moveTo, reason));

    [HttpPost]
    public async Task<IActionResult> Reopen(int id, string reason) => Done(id, await _admin.ReopenAccountAsync(id, reason));

    [HttpPost]
    public async Task<IActionResult> Adjust(int id, string direction, decimal? amount, string reason) =>
        Done(id, amount is decimal a
            ? await _admin.AdjustBalanceAsync(id, direction == "credit", a, reason)
            : new AdminResult(false, "Enter an amount."));

    [HttpPost]
    public async Task<IActionResult> UpdateSettings(int id, string? nickname, decimal? dailyLimit, string reason) =>
        Done(id, await _admin.UpdateAccountSettingsAsync(id, nickname, dailyLimit, reason));

    [HttpPost]
    public async Task<IActionResult> PostInterest(int id, string reason) => Done(id, await _admin.PostInterestAsync(id, reason));

    private IActionResult Done(int id, AdminResult result)
    {
        Flash(result);
        // area = "Admin" is explicit: the customer side has an attribute route "accounts/{id}" for an action
        // also named Details, and without it link generation would pick the customer URL.
        return RedirectToAction(nameof(Details), new { id, area = "Admin" });
    }
}

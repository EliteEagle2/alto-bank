using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AltoBank.Controllers;

/// <summary>Deposit, withdraw, transfer, and view history. Login required.</summary>
[Authorize(Roles = SeedData.CustomerRole)] // customers only; admins use /admin
[ApiExplorerSettings(IgnoreApi = true)] // HTML pages — keep them out of the Swagger API docs
public class TransactionsController : Controller
{
    private readonly IBankService _bank;
    private readonly UserManager<ApplicationUser> _userManager;

    public TransactionsController(IBankService bank, UserManager<ApplicationUser> userManager)
    {
        _bank = bank;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    // ---------- Deposit ----------

    [HttpGet]
    public async Task<IActionResult> Deposit(int? accountId)
    {
        var model = new MoneyMovementViewModel { AccountId = accountId ?? 0 };
        await FillOptionsAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Deposit(MoneyMovementViewModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await _bank.DepositAsync(UserId, model.AccountId, model.Amount, model.Description);
            if (result.Success)
            {
                TempData["Success"] = result.Message;   // flash message for the next page (PRG)
                return RedirectToAction(nameof(History), new { id = model.AccountId });
            }
            ModelState.AddModelError(string.Empty, result.Message);
        }
        await FillOptionsAsync(model);
        return View(model);
    }

    // ---------- Withdraw ----------

    [HttpGet]
    public async Task<IActionResult> Withdraw(int? accountId)
    {
        var model = new MoneyMovementViewModel { AccountId = accountId ?? 0 };
        await FillOptionsAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(MoneyMovementViewModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await _bank.WithdrawAsync(UserId, model.AccountId, model.Amount, model.Description);
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(History), new { id = model.AccountId });
            }
            ModelState.AddModelError(string.Empty, result.Message); // e.g. "Insufficient funds"
        }
        await FillOptionsAsync(model);
        return View(model);
    }

    // ---------- Transfer ----------

    [HttpGet]
    public async Task<IActionResult> Transfer(int? fromAccountId)
    {
        var model = new TransferViewModel { FromAccountId = fromAccountId ?? 0 };
        model.AccountOptions = await BuildOptionsAsync();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(TransferViewModel model)
    {
        if (model.FromAccountId == model.ToAccountId)
            ModelState.AddModelError(nameof(model.ToAccountId), "Choose a different account to transfer to.");

        if (ModelState.IsValid)
        {
            var result = await _bank.TransferAsync(UserId, model.FromAccountId, model.ToAccountId, model.Amount, model.Memo);
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction("Index", "Dashboard");
            }
            ModelState.AddModelError(string.Empty, result.Message);
        }
        model.AccountOptions = await BuildOptionsAsync();
        return View(model);
    }

    // ---------- History ----------

    // ATTRIBUTE ROUTE: this action lives at /accounts/5/history instead of the conventional
    // /Transactions/History/5. "{id:int}" is a route constraint (only numbers match).
    // Tag helpers like asp-action="History" asp-route-id="5" generate this URL automatically.
    [HttpGet("accounts/{id:int}/history")]
    public async Task<IActionResult> History(int id)
    {
        // Explicit loading: the account is loaded first, then its Transactions (see BankService).
        var account = await _bank.GetAccountWithHistoryAsync(id, UserId);
        if (account == null)
        {
            TempData["Error"] = "That account wasn't found.";
            return RedirectToAction("Index", "Accounts");
        }

        return View(new HistoryViewModel
        {
            Account = account,
            Transactions = account.Transactions
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .ToList()
        });
    }

    // ---------- Helpers ----------

    // Builds the account dropdown, e.g. "Everyday Checking ••4821 — $1,234.56".
    private async Task<List<SelectListItem>> BuildOptionsAsync() =>
        (await _bank.GetAccountsAsync(UserId))
            .Select(a => new SelectListItem($"{a.DisplayName} ••{a.Last4} — {a.Balance:C}", a.Id.ToString()))
            .ToList();

    private async Task FillOptionsAsync(MoneyMovementViewModel model) =>
        model.AccountOptions = await BuildOptionsAsync();
}

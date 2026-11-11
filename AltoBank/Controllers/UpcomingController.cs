using System.Globalization;
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
/// UPCOMING — /scheduled: everything that will be paid or moved automatically.
///   GET  /scheduled?month=2026-12       calendar for a month + the next 45 days as a list + every item + recent runs
///   POST /scheduled/transfer            schedule a one-time or recurring transfer between your own accounts
///   GET  /scheduled/{id}/edit           edit amount / account / next date / end date
///   POST /scheduled/{id}/edit
///   POST /scheduled/{id}/skip           skip the next occurrence (or /unskip)
///   POST /scheduled/{id}/cancel
/// The background SchedulerService does the paying; these pages only plan it (IBillPayService, owner-checked).
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("scheduled")]
public class UpcomingController : Controller
{
    private readonly IBankService _bank;
    private readonly IBillPayService _bills;
    private readonly UserManager<ApplicationUser> _userManager;

    public UpcomingController(IBankService bank, IBillPayService bills, UserManager<ApplicationUser> userManager)
    {
        _bank = bank;
        _bills = bills;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? month)
    {
        ViewData["Title"] = "Upcoming";
        var today = BankTime.Today;
        var first = DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
            ? new DateOnly(m.Year, m.Month, 1) : new DateOnly(today.Year, today.Month, 1);
        if (first < new DateOnly(today.Year, today.Month, 1).AddMonths(-1) || first > today.AddYears(1)) first = new DateOnly(today.Year, today.Month, 1);
        var accounts = await ActiveAccountsAsync();
        return View(new UpcomingPageViewModel
        {
            Month = first,
            Today = today,
            MonthItems = await _bills.GetUpcomingAsync(UserId, first, first.AddMonths(1).AddDays(-1)),
            Next45Days = await _bills.GetUpcomingAsync(UserId, today, today.AddDays(45)),
            Items = await _bills.GetSchedulesAsync(UserId),
            RecentRuns = await _bills.GetRecentRunsAsync(UserId, 15),
            Accounts = accounts,
            Transfer = new SchedulePaymentInput { When = "weekly", Date = today, DayOfWeek = DayOfWeek.Friday, DayOfMonth = today.Day }
        });
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> ScheduleTransfer([Bind(Prefix = "Transfer")] SchedulePaymentInput input)
    {
        if (!ModelState.IsValid) return Flash(false, FirstError());
        if (input.When == "now") input.When = "once";
        var (result, _) = await _bills.CreateScheduleAsync(UserId, input.ToData(ScheduleKind.Transfer));
        return Flash(result.Success, result.Message);
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await _bills.GetScheduleAsync(UserId, id);
        if (item == null || item.Status != ScheduleStatus.Active || item.NextRunDate == null) return NotFound();
        ViewData["Title"] = "Edit scheduled payment";
        return View(new ScheduleEditInput
        {
            Id = id, Item = item, FromAccountId = item.FromAccountId, Amount = item.Amount, Memo = item.Memo,
            NextRunDate = item.NextRunDate.Value, EndDate = item.EndDate, Accounts = await ActiveAccountsAsync()
        });
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, ScheduleEditInput input)
    {
        if (!ModelState.IsValid) return Flash(false, FirstError());
        var result = await _bills.UpdateScheduleAsync(UserId, id, new ScheduleEdit(input.FromAccountId, input.Amount, input.Memo, input.NextRunDate, input.EndDate));
        return Flash(result.Success, result.Message);
    }

    [HttpPost("{id:int}/skip")]
    public async Task<IActionResult> Skip(int id) { var r = await _bills.SetSkipNextAsync(UserId, id, true); return Flash(r.Success, r.Message); }

    [HttpPost("{id:int}/unskip")]
    public async Task<IActionResult> Unskip(int id) { var r = await _bills.SetSkipNextAsync(UserId, id, false); return Flash(r.Success, r.Message); }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id) { var r = await _bills.CancelScheduleAsync(UserId, id); return Flash(r.Success, r.Message); }

    private async Task<List<SelectListItem>> ActiveAccountsAsync() =>
        (await _bank.GetAccountsAsync(UserId)).Where(a => a.Status != AccountStatus.Closed)
            .Select(a => new SelectListItem($"{a.DisplayName} ••{a.Last4} — {a.Balance:C}", a.Id.ToString())).ToList();

    private IActionResult Flash(bool ok, string message)
    {
        TempData[ok ? "Success" : "Error"] = message;
        return RedirectToAction(nameof(Index));
    }

    private string FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m))
        ?? "Please check the form and try again.";
}

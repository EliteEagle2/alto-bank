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
/// BILL PAY — /bills
///   GET  /bills                       payees, "Pay a bill" (now, on a date, weekly or monthly), upcoming bills
///   POST /bills/pay                   pay now (IBankService.PayBillAsync) or schedule it (IBillPayService)
///   POST /bills/payees                add a payee
///   POST /bills/payees/{id}           edit a payee
///   POST /bills/payees/{id}/remove    remove a payee (soft delete; its schedules are cancelled)
/// Payees and accounts are always looked up with the signed-in user's id; anyone else's is "not found".
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("bills")]
public class BillPayController : Controller
{
    private readonly IBankService _bank;
    private readonly IBillPayService _bills;
    private readonly UserManager<ApplicationUser> _userManager;

    public BillPayController(IBankService bank, IBillPayService bills, UserManager<ApplicationUser> userManager)
    {
        _bank = bank;
        _bills = bills;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(int? payee)
    {
        ViewData["Title"] = "Bill Pay";
        var accounts = (await _bank.GetAccountsAsync(UserId)).Where(a => a.Status == AccountStatus.Active).ToList();
        var schedules = await _bills.GetSchedulesAsync(UserId);
        var today = BankTime.Today;
        return View(new BillPayPageViewModel
        {
            Payees = await _bills.GetPayeesAsync(UserId),
            FromAccounts = accounts.Select(a => new SelectListItem($"{a.DisplayName} ••{a.Last4} — {a.Balance:C}", a.Id.ToString())).ToList(),
            Pay = new SchedulePaymentInput { PayeeId = payee, FromAccountId = accounts.FirstOrDefault(a => a.AccountType == AccountType.Checking)?.Id ?? 0, Date = today },
            UpcomingBills = (await _bills.GetUpcomingAsync(UserId, today, today.AddDays(30))).Where(u => u.Item.Kind == ScheduleKind.BillPay).Take(8).ToList(),
            ActiveSchedulesByPayee = schedules.Where(s => s.Status == ScheduleStatus.Active && s.PayeeId != null)
                .GroupBy(s => s.PayeeId!.Value).ToDictionary(g => g.Key, g => g.Count())
        });
    }

    [HttpPost("pay")]
    public async Task<IActionResult> Pay([Bind(Prefix = "Pay")] SchedulePaymentInput input)
    {
        if (!ModelState.IsValid) return Flash(false, FirstError());
        if (input.PayeeId is not int payeeId) return Flash(false, "Choose who to pay.");
        if (input.When == "now")
        {
            var paid = await _bank.PayBillAsync(UserId, input.FromAccountId, payeeId, input.Amount, input.Memo);   // money: IBankService
            return Flash(paid.Success, paid.Message);
        }
        var (result, _) = await _bills.CreateScheduleAsync(UserId, input.ToData(ScheduleKind.BillPay));
        return result.Success ? FlashTo(true, result.Message, "/scheduled") : Flash(false, result.Message);
    }

    [HttpPost("payees")]
    public async Task<IActionResult> AddPayee([Bind(Prefix = "NewPayee")] PayeeInput input)
    {
        if (!ModelState.IsValid) return Flash(false, FirstError(), "#payees");
        var (result, _) = await _bills.AddPayeeAsync(UserId, input.ToData());
        return Flash(result.Success, result.Message, "#payees");
    }

    [HttpPost("payees/{id:int}")]
    public async Task<IActionResult> EditPayee(int id, PayeeInput input)
    {
        if (await _bills.GetPayeeAsync(UserId, id) == null) return NotFound();   // another user's (or removed) payee
        if (!ModelState.IsValid) return Flash(false, FirstError(), "#payees");
        var result = await _bills.UpdatePayeeAsync(UserId, id, input.ToData());
        return Flash(result.Success, result.Message, "#payees");
    }

    [HttpPost("payees/{id:int}/remove")]
    public async Task<IActionResult> RemovePayee(int id)
    {
        if (await _bills.GetPayeeAsync(UserId, id) == null) return NotFound();
        var result = await _bills.RemovePayeeAsync(UserId, id);
        return Flash(result.Success, result.Message, "#payees");
    }

    // TempData + Post/Redirect/Get.
    private IActionResult Flash(bool ok, string message, string hash = "") => FlashTo(ok, message, Url.Action(nameof(Index))! + hash);

    private IActionResult FlashTo(bool ok, string message, string url)
    {
        TempData[ok ? "Success" : "Error"] = message;
        return LocalRedirect(url);
    }

    private string FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m))
        ?? "Please check the form and try again.";
}

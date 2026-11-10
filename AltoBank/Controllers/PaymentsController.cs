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
/// SEND &amp; REQUEST — /payments
///   GET  /payments                          tabs: Send · Request · Inbox · Activity (kept in the URL hash)
///   POST /payments/send/review              shows "Sending $X to Maya C." — nothing moves yet
///   POST /payments/send                     sends (IBankService), then Post/Redirect/Get
///   POST /payments/request                  request money / split a bill (IPaymentRequestService)
///   POST /payments/requests/{id}/pay        pay a request asked of you (IBankService)
///   POST /payments/requests/{id}/decline    decline a request asked of you
///   POST /payments/requests/{id}/cancel     cancel a request you made
/// Requests belong to two people; every service call passes the signed-in user's id and checks they're
/// the right one (payer for pay/decline, requester for cancel). Anyone else gets "not found".
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]                 // every POST below needs the antiforgery token
[Route("payments")]
public class PaymentsController : Controller
{
    private readonly IBankService _bank;
    private readonly IPaymentRequestService _requests;
    private readonly UserManager<ApplicationUser> _userManager;

    public PaymentsController(IBankService bank, IPaymentRequestService requests, UserManager<ApplicationUser> userManager)
    {
        _bank = bank;
        _requests = requests;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Send & Request";
        await _requests.ExpireDueAsync();   // the scheduler does this too; this keeps the page exact between runs
        return View(await BuildPageAsync());
    }

    // Step 1: look up the recipient and show who the money is going to. No money moves here.
    [HttpPost("send/review")]
    public async Task<IActionResult> Review([Bind(Prefix = "Send")] SendPaymentInput input)
    {
        if (!ModelState.IsValid) return Flash(false, FirstError(), "send");
        var who = await _bank.FindRecipientAsync(UserId, input.To);
        if (!who.Found) return Flash(false, who.Error!, "send");
        var from = await _bank.GetAccountAsync(input.FromAccountId, UserId);
        if (from == null) return Flash(false, "Account not found.", "send");
        if (BankService.CanSend(from) is { } blocked) return Flash(false, blocked.Message, "send");

        ViewData["Title"] = "Review payment";
        return View(new SendReviewViewModel
        {
            Input = input, RecipientName = who.ShortName!, FromLabel = $"{from.DisplayName} ••{from.Last4} ({from.Balance:C})"
        });
    }

    // Step 2: confirmed. IBankService re-checks everything (ownership, status, limit, balance) inside one transaction.
    [HttpPost("send")]
    public async Task<IActionResult> Send([Bind(Prefix = "Input")] SendPaymentInput input)
    {
        if (!ModelState.IsValid) return Flash(false, FirstError(), "send");
        var (result, _) = await _bank.SendPaymentAsync(UserId, input.FromAccountId, input.To, input.Amount, input.Memo);
        return Flash(result.Success, result.Message, result.Success ? "activity" : "send");
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestMoney([Bind(Prefix = "Request")] RequestMoneyInput input)
    {
        if (!ModelState.IsValid) return Flash(false, FirstError(), "request");
        var (result, _) = await _requests.CreateAsync(UserId, input.ToNewRequest());
        return Flash(result.Success, result.Message, result.Success ? "requests" : "request");
    }

    [HttpPost("requests/{id:int}/pay")]
    public async Task<IActionResult> Pay(int id, int fromAccountId)
    {
        var (result, _) = await _bank.PayRequestAsync(UserId, id, fromAccountId);
        return Flash(result.Success, result.Message, "inbox");
    }

    [HttpPost("requests/{id:int}/decline")]
    public async Task<IActionResult> Decline(int id)
    {
        var result = await _requests.DeclineAsync(UserId, id);
        return Flash(result.Success, result.Message, "inbox");
    }

    [HttpPost("requests/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var result = await _requests.CancelAsync(UserId, id);
        return Flash(result.Success, result.Message, "requests");
    }

    // ---------- helpers ----------

    private async Task<PaymentsPageViewModel> BuildPageAsync()
    {
        var accounts = await _bank.GetAccountsAsync(UserId);
        var canSend = accounts.Where(a => a.Status == AccountStatus.Active).ToList();
        var first = canSend.FirstOrDefault(a => a.AccountType == AccountType.Checking) ?? canSend.FirstOrDefault();
        var settings = await _bank.GetSettingsAsync();
        return new PaymentsPageViewModel
        {
            Overview = await _requests.GetOverviewAsync(UserId),
            SourceAccounts = canSend.Select(a => new SelectListItem($"{a.DisplayName} ••{a.Last4} — {a.Balance:C}", a.Id.ToString(), a.Id == first?.Id)).ToList(),
            Send = new SendPaymentInput { FromAccountId = first?.Id ?? 0 },
            DailyLimit = first?.DailyTransferLimit ?? settings.DefaultDailyTransferLimit,
            HasCheckingToReceive = accounts.Any(a => a.AccountType == AccountType.Checking && a.Status == AccountStatus.Active)
        };
    }

    // TempData + Post/Redirect/Get: the message survives the redirect and _StatusAlert shows it once.
    private IActionResult Flash(bool ok, string message, string tab)
    {
        TempData[ok ? "Success" : "Error"] = message;
        return Redirect(Url.Action(nameof(Index))! + "#" + tab);
    }

    private string FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m))
        ?? "Please check the form and try again.";
}

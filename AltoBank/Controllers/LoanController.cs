using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// LOANS — /loans (the loan's own page is /accounts/{id}; this controller holds its actions)
///   GET  /loans/offer/{applicationId}          the offer: terms + Truth-in-Lending box, accept or decline
///   POST /loans/offer/{applicationId}/accept   → the money goes to checking, PRG to the new loan
///   POST /loans/offer/{applicationId}/decline
///   GET  /loans/{id}/pay                       Make a payment (scheduled / extra principal / pay off)
///   POST /loans/{id}/pay                       → PRG back to the loan
///   POST /loans/{id}/autopay                   autopay on/off
///   GET  /loans/{id}/letter                    printable paid-in-full letter (paid-off loans only)
/// Another customer's loan or offer returns 404. Money moves go through IBankService.
/// (Not named "Loans": that's the admin controller, and same-named controllers mix up area links.)
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("loans")]
public class LoanController : Controller
{
    private readonly IBankService _bank;
    private readonly ILoanService _loans;
    private readonly ICreditCardService _cards;
    private readonly UserManager<ApplicationUser> _userManager;

    public LoanController(IBankService bank, ILoanService loans, ICreditCardService cards, UserManager<ApplicationUser> userManager)
    {
        _bank = bank; _loans = loans; _cards = cards; _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    private IActionResult BackToLoan(int id, BankResult r, string? tab = null)
    {
        TempData[r.Success ? "Success" : "Error"] = r.Message;
        return Redirect(Url.Action("Details", "Accounts", new { id })! + (tab == null ? "" : "#" + tab));
    }

    // ---------------------------------------------------------------- the offer

    [HttpGet("offer/{applicationId:int}")]
    public async Task<IActionResult> Offer(int applicationId) => await OfferPageAsync(applicationId, null, false);

    private async Task<IActionResult> OfferPageAsync(int applicationId, int? toAccountId, bool agreed)
    {
        var app = await _loans.GetOfferAsync(UserId, applicationId);   // ownership check
        if (app == null) return NotFound();
        // Accepted: the loan itself. Pending / denied / declined: the status page.
        if (app.Status == ApplicationStatus.Approved && app.AccountId is int loanId) return RedirectToAction("Details", "Accounts", new { id = loanId });
        if (app.Status != ApplicationStatus.Offered) return View("~/Views/Accounts/ApplicationStatus.cshtml", app);

        var settings = await _bank.GetSettingsAsync();
        var amount = app.LoanAmount!.Value;
        var apr = app.LoanAprPercent!.Value;
        var term = app.LoanTermMonths!.Value;
        var firstDue = BankTime.Today.AddMonths(1);
        if (firstDue.Day > 28) firstDue = new DateOnly(firstDue.Year, firstDue.Month, 28);
        var schedule = LoanMath.Schedule(amount, apr, LoanMath.MonthlyPayment(amount, apr, term), firstDue, 1, term);
        var checking = await _loans.GetDisburseAccountsAsync(UserId);
        ViewData["Title"] = "Your loan offer";
        return View("Offer", new LoanOfferViewModel
        {
            Application = app, CheckingAccounts = checking,
            FinanceCharge = schedule.Sum(r => r.Interest), TotalOfPayments = schedule.Sum(r => r.Payment),
            FinalPayment = schedule.Last().Payment, FirstDueDate = firstDue,
            ExpiresOn = BankService.OfferExpiresOn(app), Expired = BankTime.Today > BankService.OfferExpiresOn(app),
            LateFee = settings.LoanLateFee, GraceDays = settings.LoanGraceDays,
            ToAccountId = toAccountId ?? (checking.Any(c => c.Id == app.DisburseToAccountId) ? app.DisburseToAccountId : checking.FirstOrDefault()?.Id),
            Agreed = agreed
        });
    }

    [HttpPost("offer/{applicationId:int}/accept")]
    public async Task<IActionResult> Accept(int applicationId, int? toAccountId, bool agree)
    {
        if (await _loans.GetOfferAsync(UserId, applicationId) == null) return NotFound();
        if (!agree) ModelState.AddModelError("agree", "Please confirm you've read the Truth in Lending disclosure and the Loan Agreement.");
        if (toAccountId is not int to) { ModelState.AddModelError("toAccountId", "Choose the checking account for the money."); to = 0; }
        if (!ModelState.IsValid) return await OfferPageAsync(applicationId, toAccountId, agree);

        var (r, loan) = await _bank.AcceptLoanOfferAsync(UserId, applicationId, to);
        if (!r.Success || loan == null)
        {
            ModelState.AddModelError("", r.Message);
            return await OfferPageAsync(applicationId, toAccountId, agree);
        }
        return BackToLoan(loan.Id, r);
    }

    [HttpPost("offer/{applicationId:int}/decline")]
    public async Task<IActionResult> Decline(int applicationId)
    {
        if (await _loans.GetOfferAsync(UserId, applicationId) == null) return NotFound();
        var r = await _bank.DeclineLoanOfferAsync(UserId, applicationId);
        TempData[r.Success ? "Success" : "Error"] = r.Message;
        return RedirectToAction(nameof(Offer), new { applicationId });
    }

    // ---------------------------------------------------------------- paying

    [HttpGet("{id:int}/pay")]
    public async Task<IActionResult> Pay(int id) => await PayPageAsync(id, new PayLoanForm());

    [HttpPost("{id:int}/pay")]
    public async Task<IActionResult> Pay(int id, PayLoanForm form)
    {
        if (await _loans.GetLoanAsync(UserId, id) == null) return NotFound();
        if (form.Kind == LoanPaymentKind.ExtraPrincipal && form.ExtraAmount is not > 0)
            ModelState.AddModelError(nameof(form.ExtraAmount), "Enter how much extra you want to pay.");
        if (form.FromAccountId is not int from) { ModelState.AddModelError(nameof(form.FromAccountId), "Choose the account to pay from."); from = 0; }
        if (!ModelState.IsValid) return await PayPageAsync(id, form);

        var r = await _bank.PayLoanAsync(UserId, id, from, form.Kind, form.ExtraAmount, form.ForDueDate);
        if (!r.Success)
        {
            ModelState.AddModelError("", r.Message);
            return await PayPageAsync(id, form);
        }
        return BackToLoan(id, r);
    }

    private async Task<IActionResult> PayPageAsync(int id, PayLoanForm form)
    {
        var loan = await _loans.GetLoanAsync(UserId, id);
        if (loan == null) return NotFound();
        if (loan.Loan.Status == LoanStatus.PaidOff) return BackToLoan(id, new BankResult(true, "This loan is paid in full. There's nothing to pay."));
        var from = await _cards.GetPayFromAccountsAsync(UserId);
        // Default: the autopay account, else where the money went, else the first checking/savings account.
        form.FromAccountId ??= from.FirstOrDefault(a => a.Id == loan.Loan.AutopayFromAccountId)?.Id
                            ?? from.FirstOrDefault(a => a.Id == loan.Loan.DisbursedToAccountId)?.Id
                            ?? from.FirstOrDefault()?.Id;
        ViewData["Title"] = "Make a loan payment";
        return View("Pay", new PayLoanViewModel { Loan = loan, FromAccounts = from, Form = form });
    }

    [HttpPost("{id:int}/autopay")]
    public async Task<IActionResult> Autopay(int id, bool enabled, int? fromAccountId)
    {
        var r = await _loans.SetAutopayAsync(UserId, id, enabled, fromAccountId);
        return r == null ? NotFound() : BackToLoan(id, r, "payments");
    }

    // ---------------------------------------------------------------- paid in full

    [HttpGet("{id:int}/letter")]
    public async Task<IActionResult> Letter(int id)
    {
        var loan = await _loans.GetLoanAsync(UserId, id);
        if (loan == null) return NotFound();
        if (loan.Loan.Status != LoanStatus.PaidOff) return BackToLoan(id, new BankResult(false, "The paid-in-full letter is ready once the loan is paid off."));
        var user = await _userManager.GetUserAsync(User);
        ViewData["Owner"] = user;
        return View("Letter", loan);
    }
}

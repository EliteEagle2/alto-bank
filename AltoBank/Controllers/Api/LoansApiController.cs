using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers.Api;

/// <summary>
/// LOANS JSON API.
///   GET /api/loans/calculator?amount=&amp;apr=&amp;months=   public: the payment, totals and full schedule (LoanMath)
///   GET /api/loans                                       the caller's loans
///   GET /api/loans/{accountId}                           one loan (another customer's → 404)
///   GET /api/loans/{accountId}/schedule                  the schedule from here
/// Responses are DTOs, never EF entities. Paying goes through the website (antiforgery + Post/Redirect/Get).
/// </summary>
[ApiController]
[Route("api/loans")]
[Authorize(Roles = SeedData.CustomerRole)]
[Produces("application/json")]
public class LoansApiController : ControllerBase
{
    private readonly ILoanService _loans;
    private readonly UserManager<ApplicationUser> _userManager;

    public LoansApiController(ILoanService loans, UserManager<ApplicationUser> userManager)
    {
        _loans = loans;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    /// <summary>The loan calculator (no sign-in needed): monthly payment, total interest and the amortization schedule.</summary>
    /// <param name="amount">Amount borrowed, $1 to $1,000,000.</param>
    /// <param name="apr">Annual rate in percent, 0 to 36 (e.g. 6.5).</param>
    /// <param name="months">Number of monthly payments, 1 to 360.</param>
    [HttpGet("calculator")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoanQuoteDto), 200)]
    [ProducesResponseType(400)]
    public ActionResult<LoanQuoteDto> Calculator(decimal amount, decimal apr, int months)
    {
        if (amount is <= 0 or > 1_000_000 || decimal.Round(amount, 2) != amount) return BadRequest(new ProblemDetails { Title = "Amount must be $0.01 to $1,000,000 (whole cents).", Status = 400 });
        if (apr is < 0 or > 36) return BadRequest(new ProblemDetails { Title = "APR must be 0 to 36 percent.", Status = 400 });
        if (months is < 1 or > 360) return BadRequest(new ProblemDetails { Title = "Months must be 1 to 360.", Status = 400 });
        var payment = LoanMath.MonthlyPayment(amount, apr, months);
        var rows = LoanMath.Schedule(amount, apr, payment, BankTime.Today.AddMonths(1), 1, months);
        return new LoanQuoteDto(amount, apr, months, payment, rows.Last().Payment, rows.Sum(r => r.Interest), rows.Sum(r => r.Payment),
            rows.Select(ToDto).ToList());
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<LoanDto>), 200)]
    public async Task<List<LoanDto>> GetLoans() => (await _loans.GetLoansAsync(UserId)).Select(ToDto).ToList();

    [HttpGet("{accountId:int}")]
    [ProducesResponseType(typeof(LoanDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<LoanDto>> GetLoan(int accountId) =>
        await _loans.GetLoanAsync(UserId, accountId) is { } v ? ToDto(v) : NotFound();

    [HttpGet("{accountId:int}/schedule")]
    [ProducesResponseType(typeof(List<LoanScheduleRowDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<List<LoanScheduleRowDto>>> GetSchedule(int accountId) =>
        await _loans.GetLoanAsync(UserId, accountId) is { } v ? v.Upcoming.Select(ToDto).ToList() : NotFound();

    private static LoanScheduleRowDto ToDto(LoanScheduleRow r) => new(r.Number, r.DueDate, r.Payment, r.Interest, r.Principal, r.BalanceAfter);

    private static LoanDto ToDto(LoanView v)
    {
        var l = v.Loan;
        return new LoanDto(v.Account.Id, l.Kind.ToString(), v.Account.DisplayName, $"••••{v.Account.Last4}",
            l.Status == LoanStatus.PaidOff ? "PaidOff" : v.Overdue ? "Overdue" : "Active",
            l.Principal, l.AprPercent, l.TermMonths, l.MonthlyPayment, l.PrincipalRemaining, l.FeesOwed, l.PercentPaid,
            l.PaymentsMade, v.PaymentsRemaining, l.NextDueDate, v.Quote.ScheduledAmount, v.Overdue, l.AutopayEnabled,
            v.Quote.PayoffAmount, l.Vehicle, l.Purpose);
    }
}

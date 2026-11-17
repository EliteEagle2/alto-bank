using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>A loan with its account, payments and what can be paid today (for the loan page, Make a payment and the API).</summary>
public record LoanView(Loan Loan, Account Account, LoanQuote Quote, List<LoanPayment> Payments, List<LoanScheduleRow> Upcoming, int GraceDays, decimal LateFee)
{
    public bool Overdue => Loan.IsOverdue(BankTime.Today, GraceDays);
    public bool PastDue => Loan.IsPastDue(BankTime.Today);
    public int PaymentsRemaining => Upcoming.Count;
    public DateOnly? PayoffDate => Upcoming.LastOrDefault()?.DueDate;
}

/// <summary>
/// LOANS, customer side without money: the loan page data, the schedule, the offer, autopay settings and the
/// paid-in-full letter. (Accepting, paying and the scheduler move money, so they're in IBankService.) Everything is
/// filtered by the signed-in customer: another customer's loan or offer is "not found".
/// </summary>
public interface ILoanService
{
    Task<LoanView?> GetLoanAsync(string userId, int loanAccountId);
    Task<List<LoanView>> GetLoansAsync(string userId);
    /// <summary>A loan application of this customer (an offer to accept, or its status).</summary>
    Task<AccountApplication?> GetOfferAsync(string userId, int applicationId);
    /// <summary>The customer's checking accounts the money can go to (active or frozen; not closed).</summary>
    Task<List<Account>> GetDisburseAccountsAsync(string userId);
    Task<BankResult?> SetAutopayAsync(string userId, int loanAccountId, bool enabled, int? fromAccountId);
}

public class LoanService : ILoanService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public LoanService(ApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public static LoanView Build(Loan loan, Account account, BankSetting s, DateOnly today)
    {
        var upcoming = loan.Status == LoanStatus.Active && loan.NextDueDate is DateOnly due
            ? LoanMath.Schedule(loan.PrincipalRemaining, loan.AprPercent, loan.MonthlyPayment, due, loan.PaymentsMade + 1, loan.TermMonths)
            : new List<LoanScheduleRow>();
        return new LoanView(loan, account, LoanQuote.For(loan, today), loan.Payments.OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.Id).ToList(),
                            upcoming, s.LoanGraceDays, s.LoanLateFee);
    }

    public async Task<LoanView?> GetLoanAsync(string userId, int loanAccountId)
    {
        var loan = await _db.Loans.AsNoTracking().Include(l => l.Account).Include(l => l.Payments)
            .FirstOrDefaultAsync(l => l.AccountId == loanAccountId && l.UserId == userId);   // ownership check
        return loan == null ? null : Build(loan, loan.Account!, await _db.GetSettingsAsync(), BankTime.Today);
    }

    public async Task<List<LoanView>> GetLoansAsync(string userId)
    {
        var s = await _db.GetSettingsAsync();
        var loans = await _db.Loans.AsNoTracking().Include(l => l.Account).Include(l => l.Payments)
            .Where(l => l.UserId == userId).OrderBy(l => l.Status).ThenBy(l => l.DisbursedAt).ToListAsync();
        return loans.Select(l => Build(l, l.Account!, s, BankTime.Today)).ToList();
    }

    public Task<AccountApplication?> GetOfferAsync(string userId, int applicationId) =>
        _db.AccountApplications.AsNoTracking().Include(a => a.Account)
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId && a.AccountType == AccountType.Loan);

    public Task<List<Account>> GetDisburseAccountsAsync(string userId) =>
        _db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.AccountType == AccountType.Checking && a.Status != AccountStatus.Closed)
            .OrderBy(a => a.OpenedAt).ToListAsync();

    public async Task<BankResult?> SetAutopayAsync(string userId, int loanAccountId, bool enabled, int? fromAccountId)
    {
        var loan = await _db.Loans.Include(l => l.Account).FirstOrDefaultAsync(l => l.AccountId == loanAccountId && l.UserId == userId);
        if (loan == null) return null;
        if (loan.Status == LoanStatus.PaidOff) return new(false, "This loan is paid off.");
        if (enabled)
        {
            var ok = fromAccountId is int id && await _db.Accounts.AnyAsync(a => a.Id == id && a.UserId == userId && a.Status == AccountStatus.Active
                                                                       && (a.AccountType == AccountType.Checking || a.AccountType == AccountType.Savings));
            if (!ok) return new(false, "Choose the checking or savings account autopay pays from.");
        }
        var before = new { autopay = loan.AutopayEnabled, from = loan.AutopayFromAccountId };
        loan.AutopayEnabled = enabled;
        loan.AutopayFromAccountId = enabled ? fromAccountId : null;
        _audit.Add("Loan.Autopay.Set", $"Autopay on loan ••{loan.Account!.Last4}: {(enabled ? "on" : "off")}", "Account", loan.AccountId.ToString(),
                   before, new { autopay = enabled, from = loan.AutopayFromAccountId });
        await _db.SaveChangesAsync();
        return new(true, enabled
            ? $"Autopay is on: we'll make each monthly payment on its due date (next: {loan.NextDueDate:MMM d})."
            : "Autopay is off. Remember to pay by each due date.");
    }
}

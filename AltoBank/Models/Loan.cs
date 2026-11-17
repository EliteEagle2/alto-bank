using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>Personal ($1,000-$25,000, 12-60 months) or auto ($5,000-$50,000, 24-72 months, with the vehicle).</summary>
public enum LoanKind { Personal, Auto }

/// <summary>Active until the last dollar is repaid; then PaidOff (the loan account is Closed too). "Overdue" is worked out from the dates.</summary>
public enum LoanStatus { Active, PaidOff }

/// <summary>What a payment was: one monthly installment, extra toward principal, or paying the whole loan off.</summary>
public enum LoanPaymentKind { Scheduled, ExtraPrincipal, Payoff }

/// <summary>
/// LOAN (personal or auto). Its money lives on a Loan ACCOUNT (Account.AccountType = Loan) whose Balance is negative
/// while money is owed, like the credit card: Balance = -(PrincipalRemaining + FeesOwed). This row holds the terms
/// and where the loan is in its schedule. Changed only inside BankService / AdminService transactions, together with
/// the account's Balance (its concurrency check stops two payments from both using the same state).
///
/// The schedule (LoanMath): fixed monthly payment M = P·r / (1 - (1+r)^-n), r = APR / 12, rounded to the cent. Each
/// installment pays that month's interest (principal remaining × r) first, the rest goes to principal; the last one
/// is whatever is left, so the loan ends at exactly $0.00.
/// </summary>
public class Loan
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }
    [Required] public string UserId { get; set; } = string.Empty;
    public int? ApplicationId { get; set; }

    public LoanKind Kind { get; set; }
    [StringLength(60)] public string? Purpose { get; set; }
    public int? VehicleYear { get; set; }
    [StringLength(30)] public string? VehicleMake { get; set; }
    [StringLength(40)] public string? VehicleModel { get; set; }

    // ----- Terms (fixed when the offer is accepted) -----
    [Column(TypeName = "decimal(18,2)")] public decimal Principal { get; set; }        // the amount borrowed
    [Column(TypeName = "decimal(5,2)")] public decimal AprPercent { get; set; }         // e.g. 6.49 (no fees, so APR = rate)
    public int TermMonths { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MonthlyPayment { get; set; }
    [StringLength(1)] public string? RateTier { get; set; }

    public DateTime DisbursedAt { get; set; } = DateTime.UtcNow;
    public int? DisbursedToAccountId { get; set; }
    public DateOnly FirstDueDate { get; set; }

    // ----- Where it is now -----
    [Column(TypeName = "decimal(18,2)")] public decimal PrincipalRemaining { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal FeesOwed { get; set; }          // unpaid late fees
    public int PaymentsMade { get; set; }                                               // installments paid
    public DateOnly? NextDueDate { get; set; }                                          // null once paid off

    // The due date a late fee was charged for: once per installment. ConcurrencyCheck: two scheduler runs can't both charge it.
    [ConcurrencyCheck] public DateOnly? LateFeeForDueDate { get; set; }

    // Autopay: the scheduled payment on each due date, from a checking or savings account.
    public bool AutopayEnabled { get; set; }
    public int? AutopayFromAccountId { get; set; }
    public DateOnly? AutopayAttemptedFor { get; set; }                                  // the due date autopay last tried

    public LoanStatus Status { get; set; } = LoanStatus.Active;
    public DateTime? PaidOffAt { get; set; }

    public ICollection<LoanPayment> Payments { get; set; } = new List<LoanPayment>();

    // ----- Helpers (not stored) -----
    [NotMapped] public string KindLabel => Kind == LoanKind.Auto ? "Auto loan" : "Personal loan";
    [NotMapped] public string? Vehicle => Kind == LoanKind.Auto ? $"{VehicleYear} {VehicleMake} {VehicleModel}".Trim() : null;
    [NotMapped] public decimal TotalOwed => PrincipalRemaining + FeesOwed;
    /// <summary>Share of the amount borrowed that's been repaid, 0-100.</summary>
    [NotMapped] public decimal PercentPaid => Principal <= 0 ? 0m : Math.Clamp(Math.Round((Principal - PrincipalRemaining) / Principal * 100m, 1), 0m, 100m);

    /// <summary>The monthly period the next installment covers starts one month before its due date.</summary>
    public DateOnly? PeriodStart => NextDueDate?.AddMonths(-1);

    /// <summary>Past the due date (still in the grace days, no fee yet).</summary>
    public bool IsPastDue(DateOnly today) => Status == LoanStatus.Active && NextDueDate is DateOnly d && today > d;

    /// <summary>OVERDUE: more than <paramref name="graceDays"/> days past the due date (the late fee is charged then).</summary>
    public bool IsOverdue(DateOnly today, int graceDays) => Status == LoanStatus.Active && NextDueDate is DateOnly d && today > d.AddDays(graceDays);
}

/// <summary>One payment to a loan, with how it was split (fees first, then interest, then principal). Never deleted.</summary>
public class LoanPayment
{
    public int Id { get; set; }
    public int LoanId { get; set; }
    public Loan? Loan { get; set; }

    public LoanPaymentKind Kind { get; set; }
    public bool ByAutopay { get; set; }
    public int? InstallmentNumber { get; set; }        // scheduled payments: 1, 2, 3...
    public DateOnly? DueDate { get; set; }              // scheduled payments: the due date it paid
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Interest { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Principal { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Fees { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PrincipalAfter { get; set; }

    public int? FromAccountId { get; set; }             // null = seeded history from an external bank
    public int? TransactionId { get; set; }             // the LoanPayment row on the loan account
}

/// <summary>One row of an amortization schedule.</summary>
public record LoanScheduleRow(int Number, DateOnly DueDate, decimal Payment, decimal Interest, decimal Principal, decimal BalanceAfter);

/// <summary>
/// The loan math, in one place: the product page calculator's API, the offer, the account's schedule, payments, the
/// seed data and the tests all use it. decimal throughout (never double), rounded to the cent half away from zero.
/// </summary>
public static class LoanMath
{
    private static decimal Cents(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>Monthly interest rate as a fraction: 6.00% APR → 0.005.</summary>
    public static decimal MonthlyRate(decimal aprPercent) => aprPercent / 100m / 12m;

    /// <summary>The standard amortized payment M = P·r / (1 - (1+r)^-n), rounded to the cent (P / n at 0%).</summary>
    public static decimal MonthlyPayment(decimal principal, decimal aprPercent, int months)
    {
        if (principal <= 0 || months <= 0) return 0m;
        var r = MonthlyRate(aprPercent);
        if (r == 0) return Cents(principal / months);
        var growth = 1m;                                   // (1 + r)^n by repeated multiplication: exact enough in decimal
        for (var i = 0; i < months; i++) growth *= 1m + r;
        return Cents(principal * r * growth / (growth - 1m));
    }

    /// <summary>One month's interest on what's left: principal remaining × APR / 12.</summary>
    public static decimal InterestFor(decimal principalRemaining, decimal aprPercent) => Cents(principalRemaining * MonthlyRate(aprPercent));

    /// <summary>Interest built up day by day (APR / 365 per day) since the current period started: used for a payoff quote.</summary>
    public static decimal AccruedInterest(decimal principalRemaining, decimal aprPercent, int days) =>
        days <= 0 ? 0m : Cents(principalRemaining * aprPercent / 100m / 365m * days);

    /// <summary>
    /// The schedule from here: <paramref name="principal"/> left, paid <paramref name="payment"/> a month starting
    /// <paramref name="firstDue"/>, numbered from <paramref name="firstNumber"/>. Installment <paramref name="lastNumber"/>
    /// (the term) is the FINAL one: it pays whatever is left (principal + that month's interest), so rounding the monthly
    /// payment to the cent is absorbed there and the balance ends at exactly $0.00 on time, never with a tiny extra
    /// payment. Extra principal paid earlier makes the schedule end sooner (the payment stays the same).
    /// </summary>
    public static List<LoanScheduleRow> Schedule(decimal principal, decimal aprPercent, decimal payment, DateOnly firstDue,
                                                 int firstNumber = 1, int lastNumber = int.MaxValue)
    {
        var rows = new List<LoanScheduleRow>();
        var left = principal;
        for (var i = 0; left > 0 && i < 600; i++)
        {
            var (pay, interest, toPrincipal) = NextInstallment(left, aprPercent, payment, firstNumber + i >= lastNumber);
            left -= toPrincipal;
            rows.Add(new LoanScheduleRow(firstNumber + i, firstDue.AddMonths(i), pay, interest, toPrincipal, left));
        }
        return rows;
    }

    /// <summary>Total interest over a full schedule (the finance charge in the Truth-in-Lending box).</summary>
    public static decimal TotalInterest(decimal principal, decimal aprPercent, int months) =>
        Schedule(principal, aprPercent, MonthlyPayment(principal, aprPercent, months), new DateOnly(2000, 1, 1), 1, months).Sum(r => r.Interest);

    /// <summary>
    /// One installment: that month's interest, and the rest of the monthly payment toward principal. The last one
    /// (<paramref name="isFinal"/>, or when the payment would cover everything) is whatever is left.
    /// </summary>
    public static (decimal Payment, decimal Interest, decimal Principal) NextInstallment(decimal principalRemaining, decimal aprPercent, decimal monthlyPayment, bool isFinal = false)
    {
        var interest = InterestFor(principalRemaining, aprPercent);
        var toPrincipal = monthlyPayment - interest;
        // (A payment that doesn't cover the interest can't happen with real terms; it's treated as the end so loops finish.)
        if (isFinal || toPrincipal >= principalRemaining || toPrincipal <= 0) toPrincipal = principalRemaining;
        return (interest + toPrincipal, interest, toPrincipal);
    }

    public static (decimal Min, decimal Max, int MinMonths, int MaxMonths) Limits(LoanKind kind) =>
        kind == LoanKind.Auto ? (5_000m, 50_000m, 24, 72) : (1_000m, 25_000m, 12, 60);

    /// <summary>The term choices offered in the application (every 12 months; auto loans start at 24).</summary>
    public static int[] Terms(LoanKind kind) => kind == LoanKind.Auto ? new[] { 24, 36, 48, 60, 72 } : new[] { 12, 24, 36, 48, 60 };
}

/// <summary>
/// What the customer can pay on a loan TODAY (Make a payment, the payoff quote, autopay):
///   Scheduled — the next installment (its interest + principal) plus any unpaid late fees. One per monthly period: it
///               can be paid once the period has started (the previous due date), so it can't be paid twice by mistake.
///   Payoff    — principal + interest accrued day by day since the period started + unpaid fees = $0.00 afterwards.
/// </summary>
public record LoanQuote(
    decimal ScheduledAmount, decimal InstallmentInterest, decimal InstallmentPrincipal, decimal Fees,
    bool CanPayScheduled, DateOnly? OpensOn,
    int DaysAccrued, decimal AccruedInterest, decimal PayoffAmount,
    int PaymentsPastDue)
{
    public static LoanQuote For(Loan loan, DateOnly today)
    {
        if (loan.Status != LoanStatus.Active || loan.NextDueDate is not DateOnly due)
            return new LoanQuote(0, 0, 0, 0, false, null, 0, 0, 0, 0);
        var (payment, interest, principal) = LoanMath.NextInstallment(loan.PrincipalRemaining, loan.AprPercent, loan.MonthlyPayment, loan.PaymentsMade + 1 >= loan.TermMonths);
        var periodStart = due.AddMonths(-1);
        var days = Math.Max(0, today.DayNumber - periodStart.DayNumber);
        var accrued = LoanMath.AccruedInterest(loan.PrincipalRemaining, loan.AprPercent, days);
        var pastDue = 0;
        while (pastDue < 600 && due.AddMonths(pastDue) < today) pastDue++;   // due dates already behind us
        return new LoanQuote(payment + loan.FeesOwed, interest, principal, loan.FeesOwed,
            CanPayScheduled: periodStart <= today, OpensOn: periodStart,
            days, accrued, loan.PrincipalRemaining + accrued + loan.FeesOwed, pastDue);
    }
}

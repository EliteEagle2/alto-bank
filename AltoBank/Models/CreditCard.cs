using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>Card autopay: nothing, the minimum payment, or the whole statement balance, paid on the due date.</summary>
public enum CardAutopay { None, Minimum, StatementBalance }

/// <summary>What the customer chose on "Pay card".</summary>
public enum CardPaymentOption { Minimum, StatementBalance, CurrentBalance, Custom }

/// <summary>
/// ALTO PEAK credit card: one closed monthly statement. Created by the statement cycle (the background scheduler) on the
/// close date, in the bank's time zone. One per (account, close date), enforced by a unique index, so a restart can't
/// close the same month twice.
///
/// Amounts are what the customer OWES (positive), even though the card account's Balance is stored as a negative number
/// (see Account: a purchase is a debit like any other, so Insights, budgets and disputes treat it like spending).
/// </summary>
public class CreditCardStatement
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    // The bank days this statement covers: PeriodStart .. PeriodEnd (the close date), both included.
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    [Column(TypeName = "decimal(18,2)")] public decimal PreviousBalance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Purchases { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal PaymentsAndCredits { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal InterestCharged { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal FeesCharged { get; set; }

    /// <summary>What was owed at the close (after interest). Paying this by the due date avoids interest next month.</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal StatementBalance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal MinimumPayment { get; set; }
    public DateOnly DueDate { get; set; }

    [Column(TypeName = "decimal(4,2)")]
    public decimal AprPercent { get; set; }   // the APR used for this statement, e.g. 21.99

    // The last card transaction this statement includes. The next statement covers every transaction after it, so
    // the periods never overlap or leave gaps, even if a close runs late.
    public int LastTransactionId { get; set; }

    // ----- After the close -----
    // Set once the due date has passed and the payment was checked (paid on time, or the late fee charged).
    [ConcurrencyCheck] public bool DueDateProcessed { get; set; }
    public DateTime? AutopayAttemptedAt { get; set; }
    [StringLength(200)] public string? AutopayResult { get; set; }

    // Set (once) when the minimum wasn't paid by the due date. ConcurrencyCheck: two scheduler runs can't both charge it.
    [ConcurrencyCheck] public bool LateFeeCharged { get; set; }
    public int? LateFeeTransactionId { get; set; }
    public bool LateFeeWaived { get; set; }

    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The card math, in one place (statement close, the seed data and the tests all agree on it).
/// </summary>
public static class CardMath
{
    /// <summary>The greater of the floor ($25) or the percent (2%) of the statement balance, but never more than the balance.</summary>
    public static decimal MinimumPayment(decimal statementBalance, decimal floor, decimal percent)
    {
        if (statementBalance <= 0) return 0m;
        var pct = decimal.Round(statementBalance * percent, 2, MidpointRounding.AwayFromZero);
        return Math.Min(statementBalance, Math.Max(floor, pct));
    }

    /// <summary>
    /// DAILY BALANCE METHOD: the amount owed at the end of each day of the period, times the daily rate (APR / 365),
    /// added up, then rounded to the cent once.
    /// </summary>
    public static decimal DailyBalanceInterest(IEnumerable<decimal> endOfDayOwed, decimal aprPercent)
    {
        var dailyRate = aprPercent / 100m / 365m;
        var total = endOfDayOwed.Sum(owed => Math.Max(0m, owed) * dailyRate);
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The next close date on or after <paramref name="from"/> for a close day of the month (1-28).</summary>
    public static DateOnly NextClose(DateOnly from, int closeDay)
    {
        var candidate = new DateOnly(from.Year, from.Month, closeDay);
        return candidate >= from ? candidate : candidate.AddMonths(1);
    }

    /// <summary>Cash back on one purchase, rounded to the cent.</summary>
    public static decimal CashBack(decimal amount, decimal rate) => decimal.Round(amount * rate, 2, MidpointRounding.AwayFromZero);
}

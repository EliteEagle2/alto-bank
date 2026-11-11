using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

public enum PayeeCategory { Utilities, Phone, Rent, Insurance, CreditCard, Other }

/// <summary>Removed = soft-deleted: hidden from the customer and can't be paid, but kept for history.</summary>
public enum PayeeStatus { Active, Removed }

/// <summary>A company or person the customer pays bills to (BILL PAY). Never hard-deleted.</summary>
public class Payee
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(60)] public string Name { get; set; } = string.Empty;
    public PayeeCategory Category { get; set; } = PayeeCategory.Other;

    // The customer's account number AT the payee (e.g. their electric account). Only the last 4 are shown.
    [Required, StringLength(30)] public string AccountNumber { get; set; } = string.Empty;
    [StringLength(30)] public string? Nickname { get; set; }

    public PayeeStatus Status { get; set; } = PayeeStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAt { get; set; }

    [NotMapped] public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? Name : Nickname!;
    [NotMapped] public string Last4 => AccountNumber.Length <= 4 ? AccountNumber : AccountNumber[^4..];
    [NotMapped] public string CategoryLabel => LabelFor(Category);

    public static string LabelFor(PayeeCategory c) => c == PayeeCategory.CreditCard ? "Credit Card" : c.ToString();

    public static string IconFor(PayeeCategory c) => c switch
    {
        PayeeCategory.Utilities => "bi-lightning-charge",
        PayeeCategory.Phone => "bi-phone",
        PayeeCategory.Rent => "bi-house-door",
        PayeeCategory.Insurance => "bi-shield-check",
        PayeeCategory.CreditCard => "bi-credit-card",
        _ => "bi-receipt"
    };
}

/// <summary>BillPay = pay a payee. Transfer = move money between the customer's own accounts.</summary>
public enum ScheduleKind { BillPay, Transfer }

/// <summary>Once on a date, every week on a weekday, or every month on a day (31 = the last day of the month).</summary>
public enum ScheduleFrequency { Once, Weekly, Monthly }

/// <summary>
/// Active = will run on NextRunDate. Completed = a one-time item that ran, or a recurring one past its end date.
/// Cancelled = stopped by the customer. Failed = a ONE-TIME item whose run failed (recurring items keep going
/// and just record the failed run). Never deleted.
/// </summary>
public enum ScheduleStatus { Active, Completed, Cancelled, Failed }

/// <summary>
/// A scheduled or recurring bill payment / transfer ("Pay Keystone Electric $85 on the 15th of every month",
/// "Move $50 to savings every Friday"). The SchedulerService (a BackgroundService) pays it when NextRunDate
/// (a BANK-LOCAL date) arrives; every attempt is recorded as a ScheduledRun.
/// </summary>
public class ScheduledPayment
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public ScheduleKind Kind { get; set; }

    public int FromAccountId { get; set; }
    public Account? FromAccount { get; set; }

    public int? PayeeId { get; set; }          // BillPay
    public Payee? Payee { get; set; }

    public int? ToAccountId { get; set; }      // Transfer
    public Account? ToAccount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [StringLength(80)] public string? Memo { get; set; }

    public ScheduleFrequency Frequency { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }  // Weekly
    public int? DayOfMonth { get; set; }       // Monthly (1-31; past the month's end means its last day)

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? NextRunDate { get; set; } // null once nothing is left to run
    public bool SkipNext { get; set; }         // "Skip next": the next occurrence is recorded as Skipped, no money moves

    public ScheduleStatus Status { get; set; } = ScheduleStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }

    // Latest attempt, for quick display (the full history is in Runs).
    public DateTime? LastRunAt { get; set; }
    public ScheduledRunStatus? LastRunStatus { get; set; }
    [StringLength(200)] public string? LastFailureReason { get; set; }

    public ICollection<ScheduledRun> Runs { get; set; } = new List<ScheduledRun>();

    [NotMapped] public bool IsRecurring => Frequency != ScheduleFrequency.Once;

    [NotMapped]
    public string Target => Kind == ScheduleKind.BillPay
        ? Payee?.DisplayName ?? "Payee"
        : ToAccount == null ? "Account" : $"{ToAccount.DisplayName} ••{ToAccount.Last4}";

    [NotMapped]
    public string ScheduleText => Frequency switch
    {
        ScheduleFrequency.Weekly => $"Every {DayOfWeek}",
        ScheduleFrequency.Monthly => DayOfMonth >= 31 ? "Monthly on the last day" : $"Monthly on the {Ordinal(DayOfMonth ?? 1)}",
        _ => $"Once on {StartDate:MMM d, yyyy}"
    };

    public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}

public enum ScheduledRunStatus { Succeeded, Failed, Skipped }

/// <summary>
/// One attempt to pay a ScheduledPayment for one date. IDEMPOTENCY: there is a UNIQUE index on
/// (ScheduledPaymentId, RunDate), and the run row is inserted in the SAME database transaction as the money
/// movement. So a date can be paid at most once, even if the scheduler runs twice at the same moment or the
/// server restarts halfway through (an uncommitted transaction leaves nothing behind).
/// </summary>
public class ScheduledRun
{
    public int Id { get; set; }

    public int ScheduledPaymentId { get; set; }
    public ScheduledPayment? ScheduledPayment { get; set; }

    public DateOnly RunDate { get; set; }
    public ScheduledRunStatus Status { get; set; }
    [StringLength(200)] public string? FailureReason { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public int? TransactionId { get; set; }    // the debit it created (Succeeded only)
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One run of the background scheduler (timer or the admin's "Run scheduler now"), for the admin page.</summary>
public class SchedulerRunLog
{
    public int Id { get; set; }
    [Required, StringLength(10)] public string Trigger { get; set; } = "timer";   // timer | admin
    [StringLength(256)] public string? TriggeredBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public int Processed { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int RequestsExpired { get; set; }
}

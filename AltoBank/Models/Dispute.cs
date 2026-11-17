using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

public enum DisputeReason { NotAuthorized, WrongAmount, NotReceived, Duplicate, Other }

/// <summary>Submitted → UnderReview → Approved (refunded) / Denied.</summary>
public enum DisputeStatus { Submitted, UnderReview, Approved, Denied }

/// <summary>
/// DISPUTE A TRANSACTION. One dispute per transaction, ever (a unique index on TransactionId, so two submits at once can
/// only create one). Never deleted.
///  • RefundAmount is fixed at submit time: the whole charge, or for "Wrong amount" the difference between the charge
///    and what the customer expected.
///  • ProvisionalCreditEnabled captures the bank setting AT SUBMIT TIME, so changing the setting later doesn't change
///    what happens to disputes that are already open.
///  • Approval refunds through the reversal system (RefundTransactionId), or keeps the provisional credit as the refund.
///    Denial with a provisional credit posts a clawback (ClawbackTransactionId), which may overdraw (ClawbackOverdrew).
/// </summary>
public class Dispute
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public int TransactionId { get; set; }
    public Transaction? Transaction { get; set; }

    public DisputeReason Reason { get; set; }
    [Required, StringLength(1000)] public string Description { get; set; } = string.Empty;

    // "Wrong amount": what the customer expected to pay (less than the charge).
    [Column(TypeName = "decimal(18,2)")] public decimal? ExpectedAmount { get; set; }
    // "Duplicate charge": the earlier charge this one duplicates.
    public int? DuplicateOfTransactionId { get; set; }

    [Column(TypeName = "decimal(18,2)")] public decimal RefundAmount { get; set; }

    // Optional supporting document (IFileStore name; the customer's own file name is never kept).
    [StringLength(64)] public string? Attachment { get; set; }

    // Concurrency token: two admins deciding at once -> the second save fails, so a dispute is decided once.
    [ConcurrencyCheck]
    public DisputeStatus Status { get; set; } = DisputeStatus.Submitted;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewStartedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    [StringLength(256)] public string? ReviewerEmail { get; set; }   // names the admin, so not "...UserId"
    [StringLength(500)] public string? DecisionReason { get; set; }    // shown to the customer

    public bool ProvisionalCreditEnabled { get; set; }
    public int? ProvisionalCreditTransactionId { get; set; }
    public int? ClawbackTransactionId { get; set; }
    public bool ClawbackOverdrew { get; set; }
    public int? RefundTransactionId { get; set; }

    [NotMapped] public bool IsOpen => Status is DisputeStatus.Submitted or DisputeStatus.UnderReview;
    [NotMapped] public string Reference => $"DSP-{Id:D6}";

    public static string LabelFor(DisputeReason r) => r switch
    {
        DisputeReason.NotAuthorized => "I didn't authorize it",
        DisputeReason.WrongAmount => "Wrong amount",
        DisputeReason.NotReceived => "I didn't receive the item",
        DisputeReason.Duplicate => "Duplicate charge",
        _ => "Something else"
    };

    public static string LabelFor(DisputeStatus s) => s switch
    {
        DisputeStatus.UnderReview => "Under review",
        DisputeStatus.Approved => "Refunded",
        DisputeStatus.Denied => "Denied",
        _ => "Submitted"
    };
}

/// <summary>Which transactions can be disputed (decision 6), in one place for the page, the API and the server check.</summary>
public static class DisputeRules
{
    public const int MaxAgeDays = 60;

    /// <summary>Withdrawals, bill payments, P2P sends and credit card purchases. Not transfers between your own accounts.</summary>
    public static readonly TransactionType[] Types = { TransactionType.Withdrawal, TransactionType.BillPayment, TransactionType.PaymentSent, TransactionType.CardPurchase };

    /// <summary>Null if the transaction can be disputed, otherwise why not (in the customer's words).</summary>
    public static string? WhyNot(Transaction t, bool alreadyDisputed, bool hasRefund)
    {
        if (alreadyDisputed) return "This transaction already has a dispute.";
        if (!Types.Contains(t.Type))
            return t.Type is TransactionType.TransferOut or TransactionType.TransferIn
                ? "Transfers between your own accounts can't be disputed."
                : "Only withdrawals, bill payments, card purchases and payments you sent can be disputed.";
        if (t.Status != TransactionStatus.Posted || t.ReversedByTransactionId != null || hasRefund)
            return "This transaction was already reversed or refunded.";
        if (AltoBank.Services.BankTime.DateOf(DateTime.SpecifyKind(t.Date, DateTimeKind.Utc)) < AltoBank.Services.BankTime.Today.AddDays(-MaxAgeDays))
            return $"Transactions can only be disputed within {MaxAgeDays} days.";
        if (t.Account?.Status == AccountStatus.Closed) return "This account is closed. Please contact us about this charge.";
        return null;
    }
}

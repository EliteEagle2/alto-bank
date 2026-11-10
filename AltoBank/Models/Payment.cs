using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>Completed = money moved. Reversed = an admin reversed it with the existing reversal system.</summary>
public enum PaymentStatus { Completed, Reversed }

/// <summary>
/// SEND &amp; REQUEST: one payment from one Alto customer to another. The money itself moves as two linked
/// Transaction rows (PaymentSent on the sender's account, PaymentReceived on the recipient's primary checking);
/// this row ties them together for the activity list and the admin P2P page.
/// </summary>
public class Payment
{
    public int Id { get; set; }

    [Required] public string SenderUserId { get; set; } = string.Empty;
    public ApplicationUser? Sender { get; set; }

    [Required] public string RecipientUserId { get; set; } = string.Empty;
    public ApplicationUser? Recipient { get; set; }

    public int FromAccountId { get; set; }
    public Account? FromAccount { get; set; }

    public int ToAccountId { get; set; }
    public Account? ToAccount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [StringLength(80)] public string? Memo { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Completed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? SentTransactionId { get; set; }
    public int? ReceivedTransactionId { get; set; }

    // Set when this payment paid a money request.
    public int? MoneyRequestId { get; set; }
}

/// <summary>Pending until the payer pays or declines, the requester cancels, or 14 days pass (Expired).</summary>
public enum MoneyRequestStatus { Pending, Paid, Declined, Cancelled, Expired }

/// <summary>
/// A request for money from one customer to another. "Split a bill" creates one request per person,
/// all sharing a SplitGroupId (and the bill total in SplitTotal). Never deleted: status changes instead.
/// </summary>
public class MoneyRequest
{
    public const int ExpiryDays = 14;

    public int Id { get; set; }

    [Required] public string RequesterUserId { get; set; } = string.Empty;
    public ApplicationUser? Requester { get; set; }

    [Required] public string PayerUserId { get; set; } = string.Empty;
    public ApplicationUser? Payer { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [StringLength(80)] public string? Memo { get; set; }

    // Concurrency token: if two requests try to pay (or decline) the same request at once, the second save fails.
    [ConcurrencyCheck]
    public MoneyRequestStatus Status { get; set; } = MoneyRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(ExpiryDays);
    public DateTime? RespondedAt { get; set; }      // paid / declined / cancelled / expired

    // Split a bill: the requests made together share this id; SplitTotal is the whole bill.
    [StringLength(36)] public string? SplitGroupId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? SplitTotal { get; set; }

    public int? PaymentId { get; set; }             // the payment that paid it
    public Payment? Payment { get; set; }

    [NotMapped] public bool IsOpen => Status == MoneyRequestStatus.Pending && ExpiresAt > DateTime.UtcNow;
}

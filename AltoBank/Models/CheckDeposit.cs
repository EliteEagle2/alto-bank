using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>Submitted → (admin review) → Approved (money posted) or Rejected (reason shown to the customer).</summary>
public enum CheckDepositStatus { Pending, Approved, Rejected }

/// <summary>
/// MOBILE CHECK DEPOSIT (simulated): photos of the front and back of a paper check, waiting for an admin.
///  • A pending deposit does NOT change the posted balance; the account header shows it on a separate "Pending" line.
///  • Approval posts a CheckDeposit transaction through IBankService (TransactionId); rejection keeps the reason.
///  • The images live in App_Data/uploads under random names (IFileStore); FrontHash (SHA-256 of the front image)
///    stops the same check image being deposited twice. Nothing is ever deleted.
/// </summary>
public class CheckDeposit
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    // Stored file names chosen by the server ("3f2a….jpg"); the customer's own file names are never kept.
    [Required, StringLength(64)] public string FrontImage { get; set; } = string.Empty;
    [Required, StringLength(64)] public string BackImage { get; set; } = string.Empty;

    // SHA-256 of the front image, hex. Same image again = duplicate deposit.
    [Required, StringLength(64)] public string FrontHash { get; set; } = string.Empty;

    // Concurrency token: two admins approving the same check at once -> the second save fails (nothing posted twice).
    [ConcurrencyCheck]
    public CheckDepositStatus Status { get; set; } = CheckDepositStatus.Pending;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    [StringLength(256)] public string? ReviewerEmail { get; set; }   // not "...UserId": it names the admin, not the owner

    // Shown to the customer when rejected (e.g. "The back isn't endorsed").
    [StringLength(500)] public string? RejectReason { get; set; }

    // The posted CheckDeposit transaction once approved.
    public int? TransactionId { get; set; }

    [NotMapped] public bool IsPending => Status == CheckDepositStatus.Pending;
    [NotMapped] public string Reference => $"CHK-{Id:D6}";
}

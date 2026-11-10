using System.ComponentModel.DataAnnotations;

namespace AltoBank.Models;

/// <summary>What an alert is about (drives its icon). Stored as text.</summary>
public enum AlertType
{
    MoneyReceived,
    RequestReceived,
    RequestPaid,
    RequestDeclined,
    RequestCancelled,
    RequestExpired,
    BillPaid,
    BillFailed,
    TransferScheduled,
    BudgetNearLimit,
    CheckApproved,
    CheckRejected,
    DisputeUpdate,
    AccountFrozen,
    AccountUnfrozen,
    PaymentReversed
}

/// <summary>
/// ALERTS CENTER: one notification for one customer (the bell in the top bar).
/// Unread while ReadAt is null. Never deleted; "mark read" only sets ReadAt.
/// </summary>
public class Alert
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public AlertType Type { get; set; }

    [Required, StringLength(100)] public string Title { get; set; } = string.Empty;
    [Required, StringLength(300)] public string Message { get; set; } = string.Empty;

    // Where "View" goes (a local path such as /payments#inbox). Always a site-relative path.
    [StringLength(200)] public string? Link { get; set; }

    // Optional key so an alert is only created once (e.g. "budget:12:2026-11:75"). Unique per user.
    [StringLength(80)] public string? DedupeKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    public bool IsRead => ReadAt != null;

    public string Icon => Type switch
    {
        AlertType.MoneyReceived => "bi-cash-coin",
        AlertType.RequestReceived => "bi-envelope-paper",
        AlertType.RequestPaid => "bi-check2-circle",
        AlertType.RequestDeclined or AlertType.RequestCancelled or AlertType.RequestExpired => "bi-x-circle",
        AlertType.BillPaid or AlertType.TransferScheduled => "bi-calendar-check",
        AlertType.BillFailed => "bi-exclamation-triangle",
        AlertType.BudgetNearLimit => "bi-pie-chart",
        AlertType.CheckApproved => "bi-check2-square",
        AlertType.CheckRejected => "bi-x-square",
        AlertType.DisputeUpdate => "bi-shield-exclamation",
        AlertType.AccountFrozen => "bi-snow",
        AlertType.AccountUnfrozen => "bi-sun",
        AlertType.PaymentReversed => "bi-arrow-counterclockwise",
        _ => "bi-bell"
    };
}

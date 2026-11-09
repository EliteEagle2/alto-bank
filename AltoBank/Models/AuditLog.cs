using System.ComponentModel.DataAnnotations;

namespace AltoBank.Models;

/// <summary>
/// Append-only record of who did what, to which record, why, and what changed.
/// Written for every admin action (by IAdminService) and for key customer events
/// (login, failed login, account opened, transfers, card changes). Rows are never edited or deleted.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Who did it. Null for anonymous events like a failed login with an unknown email.
    [StringLength(450)] public string? ActorUserId { get; set; }
    [StringLength(256)] public string? ActorEmail { get; set; }
    [Required, StringLength(20)] public string ActorRole { get; set; } = "System";   // Admin / Customer / System / Anonymous

    // What happened, e.g. "Account.Freeze", "Transaction.Reverse", "Auth.Login".
    [Required, StringLength(60)] public string Action { get; set; } = string.Empty;

    // Which record it happened to, e.g. TargetType "Account", TargetId "12".
    [StringLength(40)] public string? TargetType { get; set; }
    [StringLength(60)] public string? TargetId { get; set; }

    // One readable sentence for the log list.
    [Required, StringLength(400)] public string Summary { get; set; } = string.Empty;

    // JSON snapshots of the important fields before and after the change.
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    // The reason an admin typed in the confirmation modal (required for destructive / money-moving actions).
    [StringLength(500)] public string? Reason { get; set; }

    [StringLength(45)] public string? IpAddress { get; set; }
}

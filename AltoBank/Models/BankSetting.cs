using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>
/// Bank-wide settings that admins can change on /admin/settings.
/// There is exactly ONE row (Id = 1), created by the AdminPortal migration.
/// </summary>
public class BankSetting
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    // Annual percentage yields as fractions: 0.0410 = 4.10%.
    [Column(TypeName = "decimal(9,6)")] public decimal SavingsApy { get; set; } = 0.0410m;
    [Column(TypeName = "decimal(9,6)")] public decimal CheckingApy { get; set; } = 0.0000m;

    // Default max outgoing transfers per account per day (an account can override it).
    [Column(TypeName = "decimal(18,2)")] public decimal DefaultDailyTransferLimit { get; set; } = 5000m;

    // Transactions at or above this amount show up in the admin "Needs attention" panel.
    [Column(TypeName = "decimal(18,2)")] public decimal LargeTransactionThreshold { get; set; } = 2500m;

    // Mobile check deposit limits: per check, and per customer per BANK day (pending + approved deposits count).
    [Column(TypeName = "decimal(18,2)")] public decimal MobileDepositPerCheckLimit { get; set; } = 5000m;
    [Column(TypeName = "decimal(18,2)")] public decimal MobileDepositDailyLimit { get; set; } = 10000m;

    // ON: applications are approved instantly. OFF: they wait as "Pending" for an admin.
    public bool AutoApproveApplications { get; set; } = true;

    // The background scheduler writes these after EVERY run (shown on /admin/scheduled).
    public DateTime? SchedulerLastRunAt { get; set; }
    [StringLength(200)] public string? SchedulerLastRunSummary { get; set; }

    public DateTime? UpdatedAt { get; set; }
    [StringLength(256)] public string? UpdatedBy { get; set; }
}

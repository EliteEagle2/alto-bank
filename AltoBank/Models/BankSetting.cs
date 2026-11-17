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

    // ----- Alto Peak credit card -----
    [Column(TypeName = "decimal(5,2)")] public decimal CardPurchaseAprPercent { get; set; } = 21.99m;
    [Column(TypeName = "decimal(18,2)")] public decimal CardLateFee { get; set; } = 29m;
    [Column(TypeName = "decimal(6,4)")] public decimal CardCashBackRate { get; set; } = 0.015m;   // 1.5% = 0.015
    [Column(TypeName = "decimal(18,2)")] public decimal CardMinimumPaymentFloor { get; set; } = 25m;
    [Column(TypeName = "decimal(6,4)")] public decimal CardMinimumPaymentPercent { get; set; } = 0.02m;   // 2%

    // ----- Loans (personal and auto) -----
    // APR (percent) by rate tier; auto loans get AutoLoanRateDiscount points off (secured by the vehicle).
    [Column(TypeName = "decimal(5,2)")] public decimal LoanRateTierA { get; set; } = 8.99m;
    [Column(TypeName = "decimal(5,2)")] public decimal LoanRateTierB { get; set; } = 11.99m;
    [Column(TypeName = "decimal(5,2)")] public decimal LoanRateTierC { get; set; } = 15.99m;
    [Column(TypeName = "decimal(5,2)")] public decimal LoanRateTierD { get; set; } = 19.99m;
    [Column(TypeName = "decimal(5,2)")] public decimal AutoLoanRateDiscount { get; set; } = 2.50m;
    // Loans at or above this amount always go to staff review.
    [Column(TypeName = "decimal(18,2)")] public decimal LoanReviewAmount { get; set; } = 20000m;
    [Column(TypeName = "decimal(18,2)")] public decimal LoanLateFee { get; set; } = 35m;
    // A payment is OVERDUE (and the late fee is charged) once it's more than this many days past its due date.
    public int LoanGraceDays { get; set; } = 10;

    // Disputes: ON = credit the customer right away when they file a dispute (taken back if it's denied).
    // Captured on each dispute when it's filed, so changing this doesn't affect open disputes.
    public bool ProvisionalCreditOnDisputes { get; set; }

    // ON: applications are approved instantly. OFF: they wait as "Pending" for an admin.
    public bool AutoApproveApplications { get; set; } = true;

    // The background scheduler writes these after EVERY run (shown on /admin/scheduled).
    public DateTime? SchedulerLastRunAt { get; set; }
    [StringLength(200)] public string? SchedulerLastRunSummary { get; set; }

    public DateTime? UpdatedAt { get; set; }
    [StringLength(256)] public string? UpdatedBy { get; set; }
}

using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using AltoBank.Validation;

namespace AltoBank.Areas.Admin.ViewModels;

// ============================================================================
// Shared
// ============================================================================

/// <summary>Result of an admin action (shown as a TempData flash message after the redirect).</summary>
public record AdminResult(bool Success, string Message, string? Extra = null);

/// <summary>One page of rows plus paging info, used by every admin list.</summary>
public class PagedList<T>
{
    public List<T> Items { get; init; } = new();
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
    public int FirstRow => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastRow => Math.Min(Page * PageSize, TotalCount);

    /// <summary>The non-generic paging info the _Pager partial needs.</summary>
    public PagerInfo Pager => new(Page, TotalPages, TotalCount, FirstRow, LastRow);
}

public record PagerInfo(int Page, int TotalPages, int TotalCount, int FirstRow, int LastRow);

/// <summary>One KPI tile (Areas/Admin/Views/Shared/_KpiTile.cshtml).</summary>
/// <param name="Key">Hook for JavaScript (data-kpi) when the value is loaded from the API.</param>
/// <param name="Tone">Optional color accent: "warn", "danger", "good".</param>
public record KpiTileViewModel(
    string Key, string Label, string Icon,
    string? Value = null, string? Hint = null, string? Tone = null,
    string? LinkController = null, Dictionary<string, string>? LinkQuery = null);

/// <summary>An extra input inside a confirmation modal (amount, target account, ...).</summary>
public record ConfirmField(
    string Name, string Label, string Type = "text", bool Required = true,
    string? Placeholder = null, string? Min = null, string? Step = null, string? Value = null,
    IEnumerable<KeyValuePair<string, string>>? Options = null);

/// <summary>
/// A confirmation modal (_ConfirmModal partial) for destructive or money-moving admin actions.
/// Description must say EXACTLY what will happen. Every modal includes a REQUIRED reason box.
/// </summary>
public record ConfirmModalViewModel(
    string Id, string Title, string Description, string FormUrl, string ButtonText,
    string Tone = "danger", IEnumerable<ConfirmField>? Fields = null);

// ============================================================================
// Overview
// ============================================================================

public class OverviewViewModel
{
    public int Customers { get; set; }
    public decimal TotalDeposits { get; set; }
    public Dictionary<string, int> AccountsByType { get; set; } = new();
    public int TransactionsToday { get; set; }
    public int PendingApplications { get; set; }
    public int FrozenAccounts { get; set; }
    public decimal LargeThreshold { get; set; }
    public List<AccountApplication> PendingList { get; set; } = new();
    public List<Account> FrozenList { get; set; } = new();
    public List<Transaction> LargeTransactions { get; set; } = new();
    public List<AccountApplication> RecentApplications { get; set; } = new();
    // Operations console additions
    public List<ScheduledPayment> FailedScheduled { get; set; } = new();   // scheduled items whose last run failed (7 days)
    public List<AuditLog> RecentAdminActivity { get; set; } = new();       // latest actions by staff
    public DateTime? SchedulerLastRunAt { get; set; }
    public string? SchedulerLastRunSummary { get; set; }
    public List<CheckDeposit> PendingChecks { get; set; } = new();          // mobile check deposits waiting for review
    public int PendingCheckCount { get; set; }
    public List<Dispute> OpenDisputes { get; set; } = new();                // submitted / under review, oldest first
    public int OpenDisputeCount { get; set; }
    public List<Dispute> ClawbackOverdrafts { get; set; } = new();          // denied disputes whose clawback left the account below $0
}

// ============================================================================
// Customers
// ============================================================================

public class CustomerListQuery
{
    public string? Search { get; set; }
    public string Sort { get; set; } = "joined";   // name | email | accounts | balance | status | joined
    public bool Desc { get; set; } = true;
    public int Page { get; set; } = 1;
}

public record CustomerRow(string Id, string Name, string Email, int AccountCount, decimal TotalBalance, bool IsLocked, DateTime? Joined);

/// <summary>Editable customer profile (admin Profile tab). Also the base for "Create customer".</summary>
public class CustomerProfileInput
{
    [Required, StringLength(50), Display(Name = "First name")] public string FirstName { get; set; } = "";
    [StringLength(1), Display(Name = "M.I.")] public string? MiddleInitial { get; set; }
    [Required, StringLength(50), Display(Name = "Last name")] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Phone, StringLength(20), Display(Name = "Phone")] public string? Phone { get; set; }
    [MinimumAge(18), Display(Name = "Date of birth")] public DateOnly? DateOfBirth { get; set; }
    [StringLength(100)] public string? Street { get; set; }
    [StringLength(50)] public string? Apt { get; set; }
    [StringLength(60)] public string? City { get; set; }
    [StringLength(2)] public string? State { get; set; }
    [RegularExpression(@"^\d{5}$", ErrorMessage = "ZIP code must be 5 digits.")] public string? Zip { get; set; }
}

public class CustomerDetailViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public List<Account> Accounts { get; set; } = new();
    public List<Transaction> Activity { get; set; } = new();
    public List<CustomerNote> Notes { get; set; } = new();
    public List<AccountApplication> Applications { get; set; } = new();
    public bool IsLocked { get; set; }
    public decimal TotalBalance => Accounts.Where(a => a.Status != AccountStatus.Closed).Sum(a => a.Balance);
    public CustomerProfileInput Profile { get; set; } = new();
}

/// <summary>Read-only "View as customer" preview data.</summary>
public class ViewAsViewModel
{
    public ApplicationUser User { get; set; } = null!;
    public List<Account> Accounts { get; set; } = new();
    public List<Transaction> Recent { get; set; } = new();
    public decimal TotalBalance { get; set; }
}

// ============================================================================
// Accounts
// ============================================================================

public class AccountListQuery
{
    public AccountType? Type { get; set; }
    public AccountStatus? Status { get; set; }
    public decimal? MinBalance { get; set; }
    public decimal? MaxBalance { get; set; }
    public string? Owner { get; set; }
    public int Page { get; set; } = 1;
}

public class AccountDetailViewModel
{
    public Account Account { get; set; } = null!;
    public List<Transaction> Transactions { get; set; } = new();
    public List<Account> OtherAccounts { get; set; } = new();   // same owner, not closed (close → move balance)
    public decimal DailyLimit { get; set; }
    public decimal UsedToday { get; set; }
    public bool UsesDefaultLimit { get; set; }
    public decimal Apy { get; set; }
    public decimal NextInterest => Math.Round(Account.Balance * Apy / 12m, 2);
}

// ============================================================================
// Transactions
// ============================================================================

public class TransactionListQuery
{
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public TransactionType? Type { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? Customer { get; set; }
    public int? AccountId { get; set; }
    public int Page { get; set; } = 1;
}

public class TransactionDetailViewModel
{
    public Transaction Transaction { get; set; } = null!;
    public Transaction? Linked { get; set; }          // other leg of a transfer
    public Transaction? ReversalOf { get; set; }      // if this row IS a reversal
    public Transaction? ReversedBy { get; set; }      // if this row WAS reversed
    public string? CannotReverseReason { get; set; }  // null = can be reversed
}

// ============================================================================
// Applications
// ============================================================================

public class ApplicationListQuery
{
    public ApplicationStatus? Status { get; set; }
    public int Page { get; set; } = 1;
}

// ============================================================================
// Settings + audit log
// ============================================================================

/// <summary>Settings form. APYs are entered as percentages (4.10 = 4.10%).</summary>
public class SettingsInput
{
    [Range(0, 20), Display(Name = "Savings APY (%)")] public decimal SavingsApyPercent { get; set; }
    [Range(0, 20), Display(Name = "Checking APY (%)")] public decimal CheckingApyPercent { get; set; }
    [Range(0, 1_000_000), Display(Name = "Default daily transfer limit")] public decimal DefaultDailyTransferLimit { get; set; }
    [Range(1, 1_000_000), Display(Name = "Large transaction alert threshold")] public decimal LargeTransactionThreshold { get; set; }
    [Display(Name = "Auto-approve applications")] public bool AutoApproveApplications { get; set; }
    [Range(1, 1_000_000), Display(Name = "Mobile deposit: max per check")] public decimal MobileDepositPerCheckLimit { get; set; }
    [Range(1, 1_000_000), Display(Name = "Mobile deposit: max per day")] public decimal MobileDepositDailyLimit { get; set; }
    [Display(Name = "Provisional credit on new disputes")] public bool ProvisionalCreditOnDisputes { get; set; }

    // Alto Peak card. Optional: a field that isn't posted keeps its current value.
    [Range(0, 36), Display(Name = "Card purchase APR (%)")] public decimal? CardPurchaseAprPercent { get; set; }
    [Range(0, 100), Display(Name = "Card late fee")] public decimal? CardLateFee { get; set; }
    [Range(0, 5), Display(Name = "Card cash back (%)")] public decimal? CardCashBackPercent { get; set; }
    [Range(0, 100), Display(Name = "Minimum payment: at least")] public decimal? CardMinimumPaymentFloor { get; set; }
    [Range(0, 10), Display(Name = "Minimum payment: % of balance")] public decimal? CardMinimumPaymentPercent { get; set; }

    // Loans. Optional too: a field that isn't posted keeps its current value.
    [Range(1, 36), Display(Name = "Tier A APR (%)")] public decimal? LoanRateTierA { get; set; }
    [Range(1, 36), Display(Name = "Tier B APR (%)")] public decimal? LoanRateTierB { get; set; }
    [Range(1, 36), Display(Name = "Tier C APR (%)")] public decimal? LoanRateTierC { get; set; }
    [Range(1, 36), Display(Name = "Tier D APR (%)")] public decimal? LoanRateTierD { get; set; }
    [Range(0, 10), Display(Name = "Auto loan discount (points)")] public decimal? AutoLoanRateDiscount { get; set; }
    [Range(1000, 1_000_000), Display(Name = "Staff review at or above")] public decimal? LoanReviewAmount { get; set; }
    [Range(0, 100), Display(Name = "Loan late fee")] public decimal? LoanLateFee { get; set; }
    [Range(0, 30), Display(Name = "Grace days before a loan is overdue")] public int? LoanGraceDays { get; set; }

    [Required(ErrorMessage = "Enter a reason for this change."), StringLength(500, MinimumLength = 5, ErrorMessage = "Reason must be at least 5 characters.")]
    public string Reason { get; set; } = "";

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public record InterestRunSummary(int AccountsCredited, decimal TotalPaid, List<(string Account, decimal Amount)> Lines);

public class AuditListQuery
{
    public string? User { get; set; }
    public string? Action { get; set; }
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
}

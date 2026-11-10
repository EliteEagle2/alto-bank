using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Dtos;
using AltoBank.Models;

namespace AltoBank.Areas.Admin.Services;

/// <summary>
/// Everything the admin portal can read or change. Registered with dependency injection in Program.cs:
///     builder.Services.AddScoped&lt;IAdminService, AdminService&gt;();
///
/// RULES for every method that changes data:
///   1. it runs inside ONE database transaction (all-or-nothing);
///   2. it writes an AuditLog row (admin, action, target, before/after JSON, reason, time);
///   3. destructive or money-moving actions REQUIRE a reason (5+ characters);
///   4. nothing is ever hard-deleted — status fields (Closed, Locked, Reversed, Cancelled, Denied) are used.
/// </summary>
public partial interface IAdminService
{
    // ----- Overview -----
    Task<OverviewViewModel> GetOverviewAsync();
    Task<AdminStatsDto> GetStatsAsync(int days);

    // ----- Customers -----
    Task<PagedList<CustomerRow>> SearchCustomersAsync(CustomerListQuery query, int pageSize = 20);
    Task<CustomerDetailViewModel?> GetCustomerAsync(string userId);
    Task<ViewAsViewModel?> GetViewAsAsync(string userId);
    Task<(AdminResult Result, string? UserId, string? TempPassword)> CreateCustomerAsync(CustomerProfileInput input);
    Task<AdminResult> UpdateCustomerProfileAsync(string userId, CustomerProfileInput input);
    Task<AdminResult> OpenAccountForCustomerAsync(string userId, AccountType type, string? nickname, decimal openingDeposit, string reason);
    Task<AdminResult> AddNoteAsync(string userId, string text);
    Task<AdminResult> LockCustomerAsync(string userId, string reason);
    Task<AdminResult> UnlockCustomerAsync(string userId, string reason);
    Task<(AdminResult Result, string? TempPassword)> ResetPasswordAsync(string userId, string reason);
    Task<AdminResult> SetForcePasswordChangeAsync(string userId, bool required, string reason);

    // ----- Accounts -----
    Task<PagedList<Account>> SearchAccountsAsync(AccountListQuery query, int pageSize = 20);
    Task<AccountDetailViewModel?> GetAccountAsync(int accountId);
    Task<AdminResult> FreezeAccountAsync(int accountId, string reason);
    Task<AdminResult> UnfreezeAccountAsync(int accountId, string reason);
    Task<AdminResult> CloseAccountAsync(int accountId, int? moveBalanceToAccountId, string reason);
    Task<AdminResult> ReopenAccountAsync(int accountId, string reason);
    Task<AdminResult> AdjustBalanceAsync(int accountId, bool isCredit, decimal amount, string reason);
    Task<AdminResult> UpdateAccountSettingsAsync(int accountId, string? nickname, decimal? dailyLimit, string reason);
    Task<AdminResult> PostInterestAsync(int accountId, string reason);

    // ----- Transactions -----
    Task<PagedList<Transaction>> SearchTransactionsAsync(TransactionListQuery query, int pageSize = 25);
    Task<List<Transaction>> ExportTransactionsAsync(TransactionListQuery query);
    Task<TransactionDetailViewModel?> GetTransactionAsync(int transactionId);
    Task<AdminResult> ReverseTransactionAsync(int transactionId, string reason);

    // ----- Applications -----
    Task<PagedList<AccountApplication>> SearchApplicationsAsync(ApplicationListQuery query, int pageSize = 20);
    Task<Dictionary<ApplicationStatus, int>> CountApplicationsAsync();
    Task<AccountApplication?> GetApplicationAsync(int applicationId);
    Task<AdminResult> ApproveApplicationAsync(int applicationId, string reason);
    Task<AdminResult> DenyApplicationAsync(int applicationId, string reason);

    // ----- Settings -----
    Task<SettingsInput> GetSettingsAsync();
    Task<AdminResult> UpdateSettingsAsync(SettingsInput input);
    Task<(AdminResult Result, InterestRunSummary? Summary)> RunMonthlyInterestAsync(string reason);

    // ----- Audit log -----
    Task<PagedList<AuditLog>> SearchAuditAsync(AuditListQuery query, int pageSize = 25);
    Task<List<AuditLog>> ExportAuditAsync(AuditListQuery query);
    Task<List<string>> GetAuditActionsAsync();
}

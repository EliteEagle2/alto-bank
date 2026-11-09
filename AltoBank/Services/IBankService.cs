using AltoBank.Dtos;
using AltoBank.Models;

namespace AltoBank.Services;

/// <summary>Simple result object: did the operation succeed, and a message to show the user.</summary>
public record BankResult(bool Success, string Message);

/// <summary>
/// Everything needed to open an account. FundingAccountId == null means an external deposit
/// (ExternalSource is then a label like "First Keystone ••1234" for the transaction description).
/// </summary>
public record OpenAccountRequest(
    AccountType Type, string? Nickname, string? CardDesign,
    decimal? GoalAmount, string? GoalName,
    decimal InitialDeposit, int? FundingAccountId, string? ExternalSource = null);

/// <summary>
/// The CUSTOMER banking operations. Controllers depend on this INTERFACE, not on the
/// BankService class, and Program.cs tells dependency injection which class to use:
///     builder.Services.AddScoped&lt;IBankService, BankService&gt;();
/// That makes the controllers easy to test with a fake implementation.
/// (Admin operations live in Areas/Admin/Services/IAdminService.)
/// </summary>
public interface IBankService
{
    // ----- Reading accounts (always filtered by the owner's userId) -----
    Task<List<Account>> GetAccountsAsync(string userId);
    Task<Account?> GetAccountAsync(int accountId, string userId);
    Task<Account?> GetAccountWithHistoryAsync(int accountId, string userId);       // explicit loading
    Task<ApplicationUser?> GetDashboardDataAsync(string userId, int recentPerAccount); // eager loading
    Task<List<DailyFlowDto>> GetDailyFlowsAsync(string userId, int days);              // dashboard chart
    Task<(List<Transaction> Items, int TotalCount)> GetTransactionsPageAsync(string userId, int accountId, int page, int pageSize);
    Task<(decimal Limit, decimal UsedToday)> GetDailyTransferUsageAsync(Account account);

    // ----- Moving money (status, limits, and balance rules enforced inside) -----
    Task<BankResult> DepositAsync(string userId, int accountId, decimal amount, string? description);
    Task<BankResult> WithdrawAsync(string userId, int accountId, decimal amount, string? description);
    Task<BankResult> TransferAsync(string userId, int fromId, int toId, decimal amount, string? memo);

    // ----- Opening accounts -----
    Task<(BankResult Result, Account? Account)> OpenAccountAsync(string userId, OpenAccountRequest request);
    Task<(BankResult Result, Account? Account)> OpenAccountInCurrentTransactionAsync(string userId, OpenAccountRequest request);
    Task<(BankResult Result, AccountApplication? Application)> SubmitApplicationAsync(
        string userId, AccountApplication application, OpenAccountRequest request);

    // ----- Applications -----
    Task<List<AccountApplication>> GetApplicationsAsync(string userId);
    Task<AccountApplication?> GetApplicationAsync(int applicationId, string userId);
    Task<AccountApplication?> GetLatestApplicationAsync(string userId);
}

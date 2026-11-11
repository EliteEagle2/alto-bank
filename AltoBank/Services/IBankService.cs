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

/// <summary>Filters for the Account Details activity list. Kind: in, out, deposit, withdrawal, transfer, interest, adjustment, reversal.</summary>
public record ActivityFilter(string? Search, string? Kind, DateTime? From, DateTime? To);

/// <summary>A page of activity, the total matching rows, and money in/out for ALL matching rows.</summary>
public record ActivityResult(List<Transaction> Items, int TotalCount, decimal MoneyIn, decimal MoneyOut);

/// <summary>Result of looking up a payment recipient by email or phone. Found => UserId + "Maya C."; otherwise Error.</summary>
public record RecipientLookup(string? UserId, string? ShortName, string? Error)
{
    public bool Found => UserId != null;
    public static RecipientLookup Fail(string error) => new(null, null, error);
}

/// <summary>Everything a monthly statement shows.</summary>
public record StatementData(Account Account, ApplicationUser Owner, int Year, int Month,
    decimal OpeningBalance, decimal ClosingBalance, decimal TotalIn, decimal TotalOut, List<Transaction> Transactions);

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
    Task<Account?> GetAccountDetailsAsync(int accountId, string userId);            // eager loading (owner + cards)
    Task<ApplicationUser?> GetDashboardDataAsync(string userId, int recentPerAccount); // eager loading
    Task<ActivityResult> GetActivityAsync(string userId, int accountId, ActivityFilter filter, int page, int pageSize);
    Task<StatementData?> GetStatementAsync(string userId, int accountId, int year, int month); // explicit loading
    Task<decimal> GetInterestThisYearAsync(string userId, int accountId);
    Task<Dictionary<int, string>> GetAccountLabelsAsync(string userId);
    Task<BankResult> UpdateNicknameAsync(string userId, int accountId, string? nickname);
    Task<BankSetting> GetSettingsAsync();                                              // read-only (APYs, limits)
    Task<List<DailyFlowDto>> GetDailyFlowsAsync(string userId, int days);              // dashboard chart
    Task<(decimal Limit, decimal UsedToday)> GetDailyTransferUsageAsync(Account account);

    // ----- Moving money (status, limits, and balance rules enforced inside) -----
    Task<BankResult> DepositAsync(string userId, int accountId, decimal amount, string? description);
    Task<BankResult> WithdrawAsync(string userId, int accountId, decimal amount, string? description);
    Task<BankResult> TransferAsync(string userId, int fromId, int toId, decimal amount, string? memo);

    // ----- Send & Request (money side; BankService.Payments.cs) -----
    Task<RecipientLookup> FindRecipientAsync(string senderUserId, string? query);
    Task<(BankResult Result, Payment? Payment)> SendPaymentAsync(string userId, int fromAccountId, string? recipient, decimal amount, string? memo);
    Task<(BankResult Result, Payment? Payment)> PayRequestAsync(string userId, int requestId, int fromAccountId);

    // ----- Bill Pay + scheduled items (money side; BankService.BillPay.cs) -----
    Task<BankResult> PayBillAsync(string userId, int fromAccountId, int payeeId, decimal amount, string? memo);
    /// <summary>Called by the background scheduler for one due item. Idempotent per (item, date).</summary>
    Task<ScheduledOutcome> ExecuteScheduledAsync(int scheduledPaymentId, DateOnly today);

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

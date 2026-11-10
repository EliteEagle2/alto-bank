using System.ComponentModel.DataAnnotations;

namespace AltoBank.Dtos;

// DTOs ("Data Transfer Objects") are the shapes the JSON API sends and receives.
// We never return EF entities directly: entities have navigation properties (which can
// loop forever when serialized) and fields we don't want to expose (like UserId).

public record AccountDto(
    int Id, string Type, string Name, string? Nickname,
    string MaskedNumber, decimal Balance, DateTime OpenedAt, string Status);

/// <summary>Body for POST /api/transfers. Validated automatically because of [ApiController].</summary>
public class TransferRequestDto
{
    [Required] public int FromAccountId { get; set; }
    [Required] public int ToAccountId { get; set; }

    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Amount must be between $0.01 and $1,000,000.")]
    public decimal Amount { get; set; }

    [StringLength(40)] public string? Memo { get; set; }
}

public record TransferResultDto(string Message, AccountDto From, AccountDto To);

/// <summary>One row of the Account Details "Activity" list (GET /api/accounts/{id}/activity).</summary>
/// <remarks>Type = readable label ("Transfer In"); Kind = the enum name ("TransferIn").</remarks>
public record ActivityItemDto(
    int Id, DateTime Date, string Description, string Type, string Kind, bool IsCredit,
    decimal Amount, decimal SignedAmount, decimal BalanceAfter, string Status,
    string? Memo, string? Counterparty, int? ReversalOfTransactionId, int? ReversedByTransactionId,
    DateOnly BankDate);   // the day this falls on in the bank's time zone (for "Today" / "Yesterday" grouping)

/// <summary>A page of activity plus the money in/out totals for the WHOLE filtered range.</summary>
public record ActivityPageDto(
    IReadOnlyList<ActivityItemDto> Items, int Page, int PageSize, int TotalCount, bool HasMore,
    decimal MoneyIn, decimal MoneyOut, decimal Net);

/// <summary>Full account number — only returned to the owner, on request (the "Show" button).</summary>
public record AccountNumberDto(string AccountNumber, string RoutingNumber);

/// <summary>Full card details — only returned to the owner, on request; CVV is computed, never stored.</summary>
public record CardDetailsDto(string CardNumber, string Expiry, string Cvv, string NameOnCard);

/// <summary>Body for POST /api/accounts/{id}/nickname (inline edit on Account Info).</summary>
public class NicknameDto
{
    [StringLength(30, ErrorMessage = "Nickname can be at most 30 characters.")]
    public string? Nickname { get; set; }
}

public record DailyActivityDto(DateOnly Date, int Count, decimal Volume);

/// <summary>Money in vs. money out for one day (customer dashboard chart).</summary>
public record DailyFlowDto(DateOnly Date, decimal In, decimal Out);

public record WeeklyCountDto(DateOnly WeekStart, int Count);

/// <summary>GET /api/admin/stats — KPIs and chart series for the admin overview.</summary>
public record AdminStatsDto(
    int TotalCustomers,
    int TotalAccounts,
    decimal TotalDeposits,
    Dictionary<string, int> AccountsByType,
    Dictionary<string, decimal> DepositsByType,
    int TransactionsToday,
    int PendingApplications,
    int FrozenAccounts,
    List<DailyActivityDto> TransactionsPerDay,
    List<WeeklyCountDto> NewAccountsPerWeek);

/// <summary>GET /api/admin/customers?search= — one row per customer.</summary>
public record AdminCustomerDto(string Id, string Name, string Email, int AccountCount, decimal TotalBalance, bool IsLocked, DateTime? Joined);

/// <summary>GET /api/admin/accounts/{id} — full admin view of one account.</summary>
public record AdminAccountDto(
    int Id, string AccountNumber, string Type, string? Nickname, string Status, string? StatusReason,
    decimal Balance, decimal DailyTransferLimit, DateTime OpenedAt, DateTime? ClosedAt,
    string OwnerId, string OwnerName, string OwnerEmail, int TransactionCount);

/// <summary>Body for POST /api/admin/accounts/{id}/freeze and /unfreeze.</summary>
public class AdminReasonDto
{
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
}

public record AdminActionResultDto(bool Success, string Message, AdminAccountDto? Account);

// ===================== Send & Request (/api/payments) =====================

/// <summary>GET /api/payments/recipient?q= — who a payment would go to ("Maya C."), before sending.</summary>
public record RecipientDto(string DisplayName);

/// <summary>Body for POST /api/payments/send.</summary>
public class SendPaymentDto
{
    [Required] public int FromAccountId { get; set; }
    [Required, StringLength(100)] public string To { get; set; } = "";        // email or mobile number
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Amount must be between $0.01 and $1,000,000.")]
    public decimal Amount { get; set; }
    [StringLength(80)] public string? Memo { get; set; }
}

/// <summary>A payment as the signed-in customer sees it: Direction is "sent" or "received"; Counterparty is "Maya C.".</summary>
public record PaymentDto(int Id, string Direction, string Counterparty, decimal Amount, string? Memo, string Status, DateTime CreatedAt, int? RequestId);

public class RequestPersonDto
{
    [Required, StringLength(100)] public string To { get; set; } = "";
    [Range(typeof(decimal), "0", "1000000")] public decimal Amount { get; set; }
}

/// <summary>
/// Body for POST /api/payments/request. Plain request: give each person an amount.
/// Split a bill: set SplitTotal, and SplitEvenly=true to have the server divide it (IncludeMe = you pay a share too).
/// </summary>
public class RequestMoneyDto
{
    [Required, MinLength(1), MaxLength(5)] public List<RequestPersonDto> Recipients { get; set; } = new();
    [StringLength(80)] public string? Memo { get; set; }
    [Range(typeof(decimal), "0.01", "1000000")] public decimal? SplitTotal { get; set; }
    public bool SplitEvenly { get; set; }
    public bool IncludeMe { get; set; } = true;
}

/// <summary>A money request as the signed-in customer sees it. Direction: "incoming" (asked of you) or "outgoing" (you asked).</summary>
public record MoneyRequestDto(int Id, string Direction, string Counterparty, decimal Amount, string? Memo, string Status,
    DateTime CreatedAt, DateTime ExpiresAt, decimal? SplitTotal, int? PaymentId);

public record MoneyRequestListDto(List<MoneyRequestDto> Incoming, List<MoneyRequestDto> Outgoing);

/// <summary>Body for POST /api/payments/requests/{id}/pay.</summary>
public class PayRequestDto
{
    [Required] public int FromAccountId { get; set; }
}

public record ActionResultDto(bool Success, string Message);

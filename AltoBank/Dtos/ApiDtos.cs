using System.ComponentModel.DataAnnotations;

namespace AltoBank.Dtos;

// DTOs ("Data Transfer Objects") are the shapes the JSON API sends and receives.
// We never return EF entities directly: entities have navigation properties (which can
// loop forever when serialized) and fields we don't want to expose (like UserId).

public record AccountDto(
    int Id, string Type, string Name, string? Nickname,
    string MaskedNumber, decimal Balance, DateTime OpenedAt);

public record TransactionDto(
    int Id, DateTime Date, string Description, string Type,
    decimal Amount, decimal SignedAmount, decimal BalanceAfter);

public record PagedResult<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

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

public record DailyActivityDto(DateOnly Date, int Count, decimal Volume);

public record StatsDto(
    int TotalCustomers,
    int TotalAccounts,
    decimal TotalDeposits,
    Dictionary<string, int> AccountsByType,
    Dictionary<string, decimal> BalancesByType,
    int ApplicationsLast30Days,
    List<DailyActivityDto> TransactionsPerDay);

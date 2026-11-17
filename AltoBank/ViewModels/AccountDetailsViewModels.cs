using AltoBank.Models;

namespace AltoBank.ViewModels;

/// <summary>Everything the customer Account Details page (/accounts/{id}) shows.</summary>
public class AccountDetailsViewModel
{
    public Account Account { get; set; } = null!;
    public List<Card> Cards { get; set; } = new();               // newest first
    public Card? CurrentCard => Cards.FirstOrDefault(c => c.Status != CardStatus.Cancelled);

    // Activity tab: the first page is rendered on the server; "Load more" uses the JSON API.
    public List<Transaction> FirstPage { get; set; } = new();
    public int TotalCount { get; set; }
    public decimal MoneyIn { get; set; }
    public decimal MoneyOut { get; set; }
    public const int PageSize = 20;
    public const int DefaultDays = 90;
    public Dictionary<int, string> AccountLabels { get; set; } = new();   // counterparty names

    // Mobile check deposits waiting for review. NOT in the posted or available balance (shown on their own line).
    public List<CheckDeposit> PendingChecks { get; set; } = new();

    // Disputes: which first-page rows can be disputed, and the status of any dispute (for the badge).
    public HashSet<int> Disputable { get; set; } = new();
    public Dictionary<int, DisputeStatus> DisputeStatuses { get; set; } = new();
    public HashSet<int> PartlyRefunded { get; set; } = new();
    public decimal PendingTotal => PendingChecks.Sum(c => c.Amount);

    // Header + Account Info
    public decimal AvailableBalance => Account.Status == AccountStatus.Active ? Account.Balance : 0m;
    public decimal Apy { get; set; }
    public decimal InterestThisYear { get; set; }
    public decimal DailyLimit { get; set; }
    public decimal UsedToday { get; set; }
    public int OpenAccountCount { get; set; }                     // transfer needs 2+ open accounts
    public List<DateOnly> StatementMonths { get; set; } = new();  // newest first

    // Direct Deposit
    public string RoutingNumber { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankAddress { get; set; } = string.Empty;
    public string OwnerName => $"{Account.User?.FirstName} {Account.User?.LastName}".Trim();
    public string OwnerAddress => Account.User?.Street == null ? ""
        : $"{Account.User.Street}{(Account.User.Apt == null ? "" : ", " + Account.User.Apt)}, {Account.User.City}, {Account.User.State} {Account.User.Zip}";

    public bool IsChecking => Account.AccountType == AccountType.Checking;
    public bool CanSend => Account.Status == AccountStatus.Active;
    public bool CanReceive => Account.Status != AccountStatus.Closed;
}

/// <summary>One activity row (_TransactionRow partial). Counterparty = the other account in a transfer.</summary>
public record TransactionRowViewModel(Transaction Transaction, string? Counterparty, bool CanDispute = false, DisputeStatus? Dispute = null,
                                      bool PartlyRefunded = false);

/// <summary>
/// A value with optional Show + Copy buttons (_CopyField partial).
/// If RevealUrl is set the real value is fetched from that (owner-only) endpoint when "Show"/"Copy" is
/// clicked, read from the JSON property RevealField, and re-masked after 30 seconds.
/// </summary>
public record CopyFieldViewModel(
    string Id, string Label, string Display,
    string? CopyValue = null, string? RevealUrl = null, string? RevealField = null, bool Monospace = true);

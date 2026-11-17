using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>
/// The kinds of accounts. Checking and Savings hold the customer's money ("deposit accounts").
/// CreditCard (Alto Peak) is money the customer OWES: its Balance is stored as a NEGATIVE number, so a purchase is a debit
/// like any other (Insights, budgets and disputes treat it as spending) and a payment is a credit. It can't be used for
/// deposits, withdrawals or transfers; it has its own purchase / pay / rewards actions (BankService.CreditCards.cs).
/// Loan (personal or auto) is also money OWED, stored negative the same way: the principal still to repay plus any unpaid
/// late fees. Its terms and schedule live in the Loan table (Models/Loan.cs, BankService.Loans.cs).
/// </summary>
public enum AccountType
{
    Checking,
    Savings,
    CreditCard,
    Loan
}

/// <summary>
/// Lifecycle of an account. Accounts are NEVER deleted; they're closed instead, so history is kept.
///   Active — normal use.
///   Frozen — set by an admin: deposits still allowed, withdrawals/outgoing transfers blocked.
///   Closed — no money movement at all (balance must be $0 first). An admin can reopen it.
/// </summary>
public enum AccountStatus
{
    Active,
    Frozen,
    Closed
}

/// <summary>
/// A bank account (checking or savings) belonging to one user.
/// </summary>
public class Account
{
    public int Id { get; set; }

    // 10-digit account number generated when the account is opened.
    [Required, StringLength(10)]
    public string AccountNumber { get; set; } = string.Empty;

    public AccountType AccountType { get; set; }

    [StringLength(40)]
    public string? Nickname { get; set; }

    // Money is ALWAYS stored as decimal (never float/double) to avoid rounding errors.
    // [ConcurrencyCheck]: EF adds "AND Balance = <the value we read>" to every UPDATE. If another
    // request changed the balance in the meantime, the update matches 0 rows and EF throws
    // DbUpdateConcurrencyException, so two simultaneous withdrawals can't both succeed (no lost updates).
    [Column(TypeName = "decimal(18,2)")]
    [ConcurrencyCheck]
    public decimal Balance { get; set; }

    // When the account was opened (UTC).
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    // Checking only: which debit card design the customer picked (see CardDesigns).
    [StringLength(20)]
    public string? CardDesign { get; set; }

    // Savings only: optional goal, e.g. "New Car" / $15,000. Shown as a progress bar.
    [Column(TypeName = "decimal(18,2)")]
    public decimal? GoalAmount { get; set; }

    [StringLength(40)]
    public string? GoalName { get; set; }

    // ----- Status (enforced by BankService, changed only by admins through IAdminService) -----
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    // Why the account is frozen/closed — shown to the customer in a banner.
    [StringLength(300)]
    public string? StatusReason { get; set; }
    public DateTime? StatusChangedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // ----- Alto Peak credit card only -----
    [Column(TypeName = "decimal(18,2)")] public decimal? CreditLimit { get; set; }
    // Cash back earned and not yet redeemed (whole cents).
    [Column(TypeName = "decimal(18,2)")] public decimal RewardsBalance { get; set; }
    // The statement closes on this day of every month (1-28) and the next close is NextStatementDate (bank date).
    public int? StatementCloseDay { get; set; }
    public DateOnly? NextStatementDate { get; set; }
    public CardAutopay Autopay { get; set; } = CardAutopay.None;
    public int? AutopayFromAccountId { get; set; }

    // Max total of outgoing transfers per day. null = use the bank-wide default (BankSetting).
    [Column(TypeName = "decimal(18,2)")]
    public decimal? DailyTransferLimit { get; set; }

    // Foreign key to the owner (Identity user ids are strings).
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Card> Cards { get; set; } = new List<Card>();

    // Computed helpers (not stored in the database).
    [NotMapped]
    public string Last4 => AccountNumber.Length >= 4 ? AccountNumber[^4..] : AccountNumber;

    [NotMapped]
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname)
        ? (AccountType switch { AccountType.CreditCard => "Alto Peak Card", AccountType.Loan => "Alto Loan", _ => $"Alto {AccountType}" })
        : Nickname!;

    /// <summary>Checking or savings: holds the customer's money and can send / receive it.</summary>
    [NotMapped] public bool IsDepositAccount => AccountType is AccountType.Checking or AccountType.Savings;
    [NotMapped] public bool IsCreditCard => AccountType == AccountType.CreditCard;
    [NotMapped] public bool IsLoan => AccountType == AccountType.Loan;
    /// <summary>Card or loan: what the customer owes now (0 if they're paid up or have a credit balance).</summary>
    [NotMapped] public decimal AmountOwed => IsCreditCard || IsLoan ? Math.Max(0m, -Balance) : 0m;
    /// <summary>Credit card: how much more can be spent (limit minus what's owed; a credit balance adds to it).</summary>
    [NotMapped] public decimal AvailableCredit => IsCreditCard ? Math.Max(0m, (CreditLimit ?? 0m) + Balance) : 0m;
    /// <summary>Credit card: share of the limit in use, 0-100.</summary>
    [NotMapped] public decimal Utilization => IsCreditCard && CreditLimit > 0 ? Math.Min(100m, Math.Round(AmountOwed / CreditLimit.Value * 100m, 1)) : 0m;
}

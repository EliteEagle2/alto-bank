using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>The two kinds of accounts a customer can open.</summary>
public enum AccountType
{
    Checking,
    Savings
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
    [Column(TypeName = "decimal(18,2)")]
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

    // Foreign key to the owner (Identity user ids are strings).
    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

    // Computed helpers (not stored in the database).
    [NotMapped]
    public string Last4 => AccountNumber.Length >= 4 ? AccountNumber[^4..] : AccountNumber;

    [NotMapped]
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? $"Alto {AccountType}" : Nickname!;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>The kinds of money movements we record.</summary>
public enum TransactionType
{
    Deposit,
    Withdrawal,
    TransferIn,
    TransferOut
}

/// <summary>
/// One line in an account's history. Amount is always positive;
/// Type tells us whether it added to (credit) or removed from (debit) the balance.
/// </summary>
public class Transaction
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    [Required, StringLength(100)]
    public string Description { get; set; } = string.Empty;

    public TransactionType Type { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    // Snapshot of the account balance right after this transaction = the "running balance".
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }

    // True when money came INTO the account (shown in green).
    [NotMapped]
    public bool IsCredit => Type is TransactionType.Deposit or TransactionType.TransferIn;

    // Amount with a sign: +50.00 for credits, -50.00 for debits.
    [NotMapped]
    public decimal SignedAmount => IsCredit ? Amount : -Amount;

    // Dates are saved in UTC; SQLite forgets that, so we mark it UTC and convert for display.
    [NotMapped]
    public DateTime LocalDate => DateTime.SpecifyKind(Date, DateTimeKind.Utc).ToLocalTime();

    [NotMapped]
    public string TypeLabel => Type switch
    {
        TransactionType.TransferIn => "Transfer In",
        TransactionType.TransferOut => "Transfer Out",
        _ => Type.ToString()
    };
}

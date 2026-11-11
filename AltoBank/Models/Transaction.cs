using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>The kinds of money movements we record.</summary>
public enum TransactionType
{
    Deposit,
    Withdrawal,
    TransferIn,
    TransferOut,
    Interest,           // interest paid on an account (credit)
    AdjustmentCredit,   // "Admin adjustment" that adds money
    AdjustmentDebit,    // "Admin adjustment" that removes money
    ReversalCredit,     // offsets (undoes) an earlier debit
    ReversalDebit,      // offsets (undoes) an earlier credit
    PaymentSent,        // Send & Request: money sent to another Alto customer (debit)
    PaymentReceived,    // Send & Request: money received from another Alto customer (credit)
    BillPayment         // Bill Pay: money paid to a payee, now or on a schedule (debit)
}

/// <summary>
/// Posted = normal. Reversed = an admin undid it with an offsetting transaction.
/// Transactions are NEVER deleted or edited in place; a reversal is a new row, so history stays complete.
/// </summary>
public enum TransactionStatus
{
    Posted,
    Reversed
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

    public TransactionStatus Status { get; set; } = TransactionStatus.Posted;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    // Snapshot of the account balance right after this transaction = the "running balance".
    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }

    // Optional note, e.g. the customer's transfer memo or the admin's reason for an adjustment.
    [StringLength(200)]
    public string? Memo { get; set; }

    // Transfers and payments: the OTHER account involved, and the matching leg on that account.
    public int? CounterpartyAccountId { get; set; }

    // Payments between customers: the other person's short name ("Maya C."), saved when the payment is made.
    // Shown instead of looking up the other customer's account, so nothing else about them is exposed.
    [StringLength(60)]
    public string? CounterpartyName { get; set; }
    public int? LinkedTransactionId { get; set; }

    // Reversals: this row undoes ReversalOfTransactionId; the original points back via ReversedByTransactionId.
    public int? ReversalOfTransactionId { get; set; }
    public int? ReversedByTransactionId { get; set; }

    // ----- Computed helpers (not stored) -----

    // True when money came INTO the account (shown in green).
    [NotMapped]
    public bool IsCredit => IsCreditType(Type);

    /// <summary>Does this kind of transaction ADD money to the account?</summary>
    public static bool IsCreditType(TransactionType type) => CreditTypes.Contains(type);

    /// <summary>Every type that adds money (also used in LINQ-to-SQL filters).</summary>
    public static readonly TransactionType[] CreditTypes =
    {
        TransactionType.Deposit, TransactionType.TransferIn, TransactionType.Interest,
        TransactionType.AdjustmentCredit, TransactionType.ReversalCredit, TransactionType.PaymentReceived
    };

    /// <summary>Money sent to another account. These count toward the daily transfer limit (withdrawals don't).</summary>
    public static readonly TransactionType[] LimitedTypes =
    {
        TransactionType.TransferOut, TransactionType.PaymentSent, TransactionType.BillPayment
    };

    [NotMapped]
    public bool IsReversal => Type is TransactionType.ReversalCredit or TransactionType.ReversalDebit;

    // Amount with a sign: +50.00 for credits, -50.00 for debits.
    [NotMapped]
    public decimal SignedAmount => IsCredit ? Amount : -Amount;

    // Dates are saved in UTC; shown in the bank's time zone (Services/BankTime.cs).
    [NotMapped]
    public DateTime LocalDate => AltoBank.Services.BankTime.ToLocal(Date);

    [NotMapped]
    public string TypeLabel => LabelFor(Type);

    public static string LabelFor(TransactionType type) => type switch
    {
        TransactionType.TransferIn => "Transfer In",
        TransactionType.TransferOut => "Transfer Out",
        TransactionType.AdjustmentCredit or TransactionType.AdjustmentDebit => "Admin adjustment",
        TransactionType.ReversalCredit or TransactionType.ReversalDebit => "Reversal",
        TransactionType.PaymentSent => "Payment sent",
        TransactionType.PaymentReceived => "Payment received",
        TransactionType.BillPayment => "Bill payment",
        _ => type.ToString()
    };
}

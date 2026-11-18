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
    BillPayment,        // Bill Pay: money paid to a payee, now or on a schedule (debit)
    CheckDeposit,       // Mobile check deposit, posted when an admin approves it (credit)
    ProvisionalCredit,  // Dispute: money returned right away while the dispute is reviewed (credit)
    ProvisionalClawback,// Dispute denied: the provisional credit taken back; may overdraw (debit)
    CardPurchase,       // Alto Peak: a purchase on the credit card (debit: adds to what's owed)
    CardPayment,        // Alto Peak: a payment to the card (credit: lowers what's owed)
    InterestCharge,     // Alto Peak: interest at statement close (debit)
    LateFee,            // Alto Peak: minimum not paid by the due date (debit)
    FeeWaiver,          // Alto Peak: a fee taken back by staff (credit)
    CashBackCredit,     // Alto Peak: cash back redeemed as a statement credit (credit)
    RewardRedemption,   // Alto Peak: cash back redeemed into checking (credit on the checking account)
    LoanDisbursement,   // Loan: the amount borrowed, on the loan account (debit: adds to what's owed)
    LoanProceeds,       // Loan: the borrowed money arriving in checking (credit)
    LoanPayment         // Loan: a payment to the loan (credit: lowers what's owed)
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

    // Spending Insights: what this was for. Set automatically by CategoryRules when the row is created;
    // CategoryOverridden = the customer picked it themselves, so the rules never change it again.
    public TransactionCategory Category { get; set; } = TransactionCategory.Other;
    public bool CategoryOverridden { get; set; }

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
        TransactionType.AdjustmentCredit, TransactionType.ReversalCredit, TransactionType.PaymentReceived,
        TransactionType.CheckDeposit, TransactionType.ProvisionalCredit,
        TransactionType.CardPayment, TransactionType.FeeWaiver, TransactionType.CashBackCredit, TransactionType.RewardRedemption,
        TransactionType.LoanProceeds, TransactionType.LoanPayment
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

    /// <summary>
    /// The activity-list icon for each kind of transaction (Bootstrap Icons). account-details.js has the same map for
    /// rows added by "Load more"; keep the two in step.
    /// </summary>
    public static string IconFor(TransactionType type) => type switch
    {
        TransactionType.Deposit => "bi-arrow-down-left",
        TransactionType.Withdrawal => "bi-arrow-up-right",
        TransactionType.TransferIn or TransactionType.TransferOut => "bi-arrow-left-right",
        TransactionType.Interest => "bi-graph-up-arrow",
        TransactionType.AdjustmentCredit or TransactionType.AdjustmentDebit => "bi-sliders",
        TransactionType.PaymentSent => "bi-send",
        TransactionType.PaymentReceived => "bi-person-down",
        TransactionType.BillPayment => "bi-receipt",
        TransactionType.CheckDeposit => "bi-camera",
        TransactionType.ProvisionalCredit => "bi-shield-check",
        TransactionType.ProvisionalClawback => "bi-shield-x",
        TransactionType.CardPurchase => "bi-bag",
        TransactionType.CardPayment => "bi-credit-card-2-back",
        TransactionType.InterestCharge => "bi-percent",
        TransactionType.LateFee => "bi-exclamation-diamond",
        TransactionType.FeeWaiver => "bi-patch-check",
        TransactionType.CashBackCredit or TransactionType.RewardRedemption => "bi-gift",
        TransactionType.LoanPayment => "bi-bank",
        TransactionType.LoanDisbursement or TransactionType.LoanProceeds => "bi-cash-coin",
        _ => "bi-arrow-counterclockwise"   // reversals
    };

    public static string LabelFor(TransactionType type) => type switch
    {
        TransactionType.TransferIn => "Transfer In",
        TransactionType.TransferOut => "Transfer Out",
        TransactionType.AdjustmentCredit or TransactionType.AdjustmentDebit => "Admin adjustment",
        TransactionType.ReversalCredit or TransactionType.ReversalDebit => "Reversal",
        TransactionType.PaymentSent => "Payment sent",
        TransactionType.PaymentReceived => "Payment received",
        TransactionType.BillPayment => "Bill payment",
        TransactionType.CheckDeposit => "Mobile check deposit",
        TransactionType.ProvisionalCredit => "Provisional credit",
        TransactionType.ProvisionalClawback => "Provisional credit reversed",
        TransactionType.CardPurchase => "Card purchase",
        TransactionType.CardPayment => "Payment",
        TransactionType.InterestCharge => "Interest charged",
        TransactionType.LateFee => "Late fee",
        TransactionType.FeeWaiver => "Fee waived",
        TransactionType.CashBackCredit => "Cash back (statement credit)",
        TransactionType.RewardRedemption => "Cash back",
        TransactionType.LoanDisbursement => "Loan funds",
        TransactionType.LoanProceeds => "Loan deposit",
        TransactionType.LoanPayment => "Loan payment",
        _ => type.ToString()
    };
}

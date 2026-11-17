using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using AltoBank.Services;

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
    public bool IsCreditCard => Account.IsCreditCard;
    public bool IsLoan => Account.IsLoan;
    /// <summary>Card or loan: the balance is money owed, so rows show "Owed" instead of a running balance.</summary>
    public bool ShowsOwed => Account.IsCreditCard || Account.IsLoan;

    // ----- Loan only -----
    public LoanView? Loan { get; set; }

    // ----- Alto Peak card only -----
    public CreditCardStatement? LatestStatement { get; set; }
    public List<CreditCardStatement> Statements { get; set; } = new();
    public decimal MinimumLeft { get; set; }       // minimum due on the latest statement, after payments since it closed
    public decimal StatementLeft { get; set; }     // statement balance still to pay
    public decimal PaidSinceClose { get; set; }
    public List<Account> PayFromAccounts { get; set; } = new();
    public decimal CardAprPercent { get; set; }
    public decimal CashBackPercent { get; set; }
    public Account? AutopayFrom => PayFromAccounts.FirstOrDefault(a => a.Id == Account.AutopayFromAccountId);
    public bool CanSend => Account.Status == AccountStatus.Active;
    public bool CanReceive => Account.Status != AccountStatus.Closed;
}

/// <summary>One activity row (_TransactionRow partial). Counterparty = the other account in a transfer.</summary>
public record TransactionRowViewModel(Transaction Transaction, string? Counterparty, bool CanDispute = false, DisputeStatus? Dispute = null,
                                      bool PartlyRefunded = false, bool OnCreditCard = false);

/// <summary>
/// A value with optional Show + Copy buttons (_CopyField partial).
/// If RevealUrl is set the real value is fetched from that (owner-only) endpoint when "Show"/"Copy" is
/// clicked, read from the JSON property RevealField, and re-masked after 30 seconds.
/// </summary>
public record CopyFieldViewModel(
    string Id, string Label, string Display,
    string? CopyValue = null, string? RevealUrl = null, string? RevealField = null, bool Monospace = true);

/// <summary>Make a loan payment form (/loans/{id}/pay).</summary>
public class PayLoanForm
{
    public LoanPaymentKind Kind { get; set; } = LoanPaymentKind.Scheduled;
    [Range(0.01, 1_000_000)] public decimal? ExtraAmount { get; set; }
    public int? FromAccountId { get; set; }
}

public class PayLoanViewModel
{
    public LoanView Loan { get; set; } = null!;
    public List<Account> FromAccounts { get; set; } = new();
    public PayLoanForm Form { get; set; } = new();
}

/// <summary>The loan offer page (/loans/offer/{applicationId}): the terms, the Truth-in-Lending box, where the money goes.</summary>
public class LoanOfferViewModel
{
    public AccountApplication Application { get; set; } = null!;
    public List<Account> CheckingAccounts { get; set; } = new();
    public decimal FinanceCharge { get; set; }          // total interest over the full schedule
    public decimal TotalOfPayments { get; set; }
    public decimal FinalPayment { get; set; }           // the last payment (adjusted so the loan ends at $0.00)
    public DateOnly FirstDueDate { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public bool Expired { get; set; }
    public decimal LateFee { get; set; }
    public int GraceDays { get; set; }
    public int? ToAccountId { get; set; }
    public bool Agreed { get; set; }
}

/// <summary>Pay card form (/credit-card/{id}/pay).</summary>
public class PayCardForm
{
    public CardPaymentOption Option { get; set; } = CardPaymentOption.StatementBalance;
    [Range(0.01, 1_000_000)] public decimal? CustomAmount { get; set; }
    public int? FromAccountId { get; set; }
}

public class PayCardViewModel
{
    public Account Card { get; set; } = null!;
    public CreditCardStatement? Statement { get; set; }
    public decimal PaidSinceClose { get; set; }
    public decimal MinimumLeft { get; set; }
    public decimal StatementLeft { get; set; }
    public List<Account> FromAccounts { get; set; } = new();
    public PayCardForm Form { get; set; } = new();
}

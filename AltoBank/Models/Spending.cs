using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>SPENDING INSIGHTS: what a transaction was for. Stored as text ("FoodAndDining").</summary>
public enum TransactionCategory
{
    FoodAndDining,
    Groceries,
    Gas,
    Shopping,
    BillsAndUtilities,
    Entertainment,
    FeesAndInterest,   // interest charged and late fees (card and loan); a fee waiver is a refund here
    Transfers,
    DebtPayments,      // paying your own card or loan: money moving, not spending (like Transfers)
    Income,
    Other
}

/// <summary>Removed = soft-deleted: hidden and not checked, kept for history. Adding the same category again reactivates it.</summary>
public enum BudgetStatus { Active, Removed }

/// <summary>
/// A monthly spending limit for one category. One row per (user, category); "remove" only sets Status.
/// Progress = this bank month's spending in that category across all of the customer's accounts.
/// </summary>
public class Budget
{
    public int Id { get; set; }

    [Required] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public TransactionCategory Category { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal MonthlyLimit { get; set; }

    public BudgetStatus Status { get; set; } = BudgetStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Labels, icons and chart colors for each category, in one place.</summary>
public static class Categories
{
    /// <summary>Categories that count as SPENDING (insights, budgets). Income and Transfers are money moving, not spent.</summary>
    public static readonly TransactionCategory[] Spending =
    {
        TransactionCategory.FoodAndDining, TransactionCategory.Groceries, TransactionCategory.Gas, TransactionCategory.Shopping,
        TransactionCategory.BillsAndUtilities, TransactionCategory.Entertainment, TransactionCategory.FeesAndInterest, TransactionCategory.Other
    };

    public static readonly TransactionCategory[] All = Enum.GetValues<TransactionCategory>();

    public static bool IsSpending(TransactionCategory c) => c is not (TransactionCategory.Income or TransactionCategory.Transfers or TransactionCategory.DebtPayments);

    public static string Label(TransactionCategory c) => c switch
    {
        TransactionCategory.FoodAndDining => "Food & Dining",
        TransactionCategory.BillsAndUtilities => "Bills & Utilities",
        TransactionCategory.Gas => "Gas & Transport",
        TransactionCategory.FeesAndInterest => "Fees & Interest",
        TransactionCategory.DebtPayments => "Debt payments",
        _ => c.ToString()
    };

    public static string Icon(TransactionCategory c) => c switch
    {
        TransactionCategory.FoodAndDining => "bi-cup-hot",
        TransactionCategory.Groceries => "bi-basket",
        TransactionCategory.Gas => "bi-fuel-pump",
        TransactionCategory.Shopping => "bi-bag",
        TransactionCategory.BillsAndUtilities => "bi-lightning-charge",
        TransactionCategory.Entertainment => "bi-film",
        TransactionCategory.FeesAndInterest => "bi-percent",
        TransactionCategory.Transfers => "bi-arrow-left-right",
        TransactionCategory.DebtPayments => "bi-credit-card-2-back",
        TransactionCategory.Income => "bi-cash-stack",
        _ => "bi-three-dots"
    };

    /// <summary>
    /// Chart colors. The color follows the CATEGORY (never its rank), so Groceries is the same orange in every chart
    /// and month. The six hues were checked as a set on the dark surface (#18233A) for colorblind separation and
    /// 3:1 contrast; Other is a neutral gray, and the two non-spending categories reuse quiet tones.
    /// </summary>
    public static string Color(TransactionCategory c) => c switch
    {
        TransactionCategory.FoodAndDining => "#3987e5",
        TransactionCategory.Groceries => "#d95926",
        TransactionCategory.Gas => "#199e70",
        TransactionCategory.Shopping => "#c98500",
        TransactionCategory.BillsAndUtilities => "#d55181",
        TransactionCategory.Entertainment => "#9085e9",
        TransactionCategory.FeesAndInterest => "#2f9fb3",   // 7th hue, checked in palette order (after Entertainment)
        TransactionCategory.Income => "#4CC38A",
        TransactionCategory.Transfers => "#8FB8E8",
        TransactionCategory.DebtPayments => "#A7B3C9",
        _ => "#7f8aa0"
    };

    /// <summary>Parses a category KEY such as "FoodAndDining" (case-insensitive). Only exact names count, so numbers
    /// ("99") and combinations ("Groceries,Gas") are refused.</summary>
    public static bool TryParse(string? value, out TransactionCategory category)
    {
        var match = All.Where(c => string.Equals(c.ToString(), value?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        category = match.FirstOrDefault();
        return match.Count == 1;
    }
}

/// <summary>
/// AUTO-CATEGORIZATION: keyword rules on the description (merchant name). The first rule whose keyword appears in
/// the description wins, so more specific rules go first. Transfers between accounts and payments between people
/// are always "Transfers"; a credit nothing matches is "Income", a debit nothing matches is "Other".
/// A customer's own choice (Transaction.CategoryOverridden) is never replaced by these rules.
/// </summary>
public static class CategoryRules
{
    private static readonly (string Keyword, TransactionCategory Category)[] Rules =
    {
        // Income first: "Payroll - Keystone Logistics" must not match the Gas/Shopping words in employer names.
        ("payroll", TransactionCategory.Income), ("salary", TransactionCategory.Income), ("direct deposit", TransactionCategory.Income),
        ("interest", TransactionCategory.Income),

        ("coffee", TransactionCategory.FoodAndDining), ("café", TransactionCategory.FoodAndDining), ("cafe", TransactionCategory.FoodAndDining),
        ("bistro", TransactionCategory.FoodAndDining), ("diner", TransactionCategory.FoodAndDining), ("noodle", TransactionCategory.FoodAndDining),
        ("pizza", TransactionCategory.FoodAndDining), ("restaurant", TransactionCategory.FoodAndDining), ("grill", TransactionCategory.FoodAndDining),
        ("bakery", TransactionCategory.FoodAndDining), ("taco", TransactionCategory.FoodAndDining), ("burger", TransactionCategory.FoodAndDining),

        ("grocer", TransactionCategory.Groceries), ("market", TransactionCategory.Groceries), ("foods", TransactionCategory.Groceries),
        ("supermarket", TransactionCategory.Groceries),

        ("fuel", TransactionCategory.Gas),
        (" gas", TransactionCategory.Gas),

        (" rent", TransactionCategory.BillsAndUtilities), ("electric", TransactionCategory.BillsAndUtilities), ("water", TransactionCategory.BillsAndUtilities),
        ("internet", TransactionCategory.BillsAndUtilities), ("phone bill", TransactionCategory.BillsAndUtilities), ("wireless", TransactionCategory.BillsAndUtilities),
        ("insurance", TransactionCategory.BillsAndUtilities), ("utilit", TransactionCategory.BillsAndUtilities),

        ("streamly", TransactionCategory.Entertainment), ("cinema", TransactionCategory.Entertainment), ("movie", TransactionCategory.Entertainment),
        ("concert", TransactionCategory.Entertainment), ("theater", TransactionCategory.Entertainment), ("theatre", TransactionCategory.Entertainment),
        ("tickets", TransactionCategory.Entertainment), ("games", TransactionCategory.Entertainment),

        ("shopnest", TransactionCategory.Shopping), ("online order", TransactionCategory.Shopping), ("outfitters", TransactionCategory.Shopping),
        ("pharmacy", TransactionCategory.Shopping), ("store", TransactionCategory.Shopping), ("bookshop", TransactionCategory.Shopping),
        ("books", TransactionCategory.Shopping), ("hardware", TransactionCategory.Shopping), ("dry clean", TransactionCategory.Shopping),
        ("cleaners", TransactionCategory.Shopping), ("laundry", TransactionCategory.Shopping),

        ("fitness", TransactionCategory.Entertainment), ("gym", TransactionCategory.Entertainment), ("yoga", TransactionCategory.Entertainment),

        ("parking", TransactionCategory.Gas), ("transit", TransactionCategory.Gas), ("toll", TransactionCategory.Gas), ("rideshare", TransactionCategory.Gas),
    };

    public static TransactionCategory For(TransactionType type, string? description)
    {
        // Money moving between accounts or people isn't spending or income.
        if (type is TransactionType.TransferIn or TransactionType.TransferOut
                 or TransactionType.PaymentSent or TransactionType.PaymentReceived)
            return TransactionCategory.Transfers;
        if (type is TransactionType.Interest or TransactionType.CheckDeposit or TransactionType.CashBackCredit or TransactionType.RewardRedemption)
            return TransactionCategory.Income;
        // Paying your own card or loan: its own non-spending category, so Insights can show it without counting it twice.
        if (type is TransactionType.CardPayment or TransactionType.LoanPayment) return TransactionCategory.DebtPayments;
        // Borrowing and repaying a loan moves money between the customer's own accounts: not income, not spending.
        if (type is TransactionType.LoanDisbursement or TransactionType.LoanProceeds) return TransactionCategory.Transfers;
        if (type is TransactionType.InterestCharge or TransactionType.LateFee or TransactionType.FeeWaiver) return TransactionCategory.FeesAndInterest;
        // Every Bill Pay payee is a bill (utilities, phone, rent, insurance, credit card). "Test Gas Co" is a gas
        // UTILITY bill, not fuel, so the merchant keywords below don't apply.
        if (type == TransactionType.BillPayment) return TransactionCategory.BillsAndUtilities;

        var text = " " + (description ?? "").ToLowerInvariant();
        foreach (var (keyword, category) in Rules)
            if (text.Contains(keyword)) return category;

        return Transaction.IsCreditType(type) ? TransactionCategory.Income : TransactionCategory.Other;
    }
}

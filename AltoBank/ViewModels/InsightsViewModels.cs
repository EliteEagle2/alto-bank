using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AltoBank.Models;
using AltoBank.Services;

namespace AltoBank.ViewModels;

/// <summary>/insights: one month of spending, plus the transactions of one category when a slice is picked.</summary>
public class InsightsPageViewModel
{
    public required InsightsMonth Data { get; init; }
    public TransactionCategory? Category { get; init; }
    public List<Transaction> CategoryTransactions { get; init; } = new();

    public string MonthKey => MonthParam.Format(Data.Month);
}

/// <summary>/budgets: progress this month, what was spent last month (to help pick a limit), and the add form.</summary>
public class BudgetsPageViewModel
{
    public List<BudgetProgress> Budgets { get; init; } = new();
    /// <summary>Last full month's spending per category (a hint next to the form).</summary>
    public Dictionary<TransactionCategory, decimal> LastMonth { get; init; } = new();
    public DateOnly Month { get; init; }
    public BudgetFormModel Form { get; init; } = new();

    public IEnumerable<TransactionCategory> Unbudgeted =>
        Categories.Spending.Where(c => Budgets.All(b => b.Category != c));
}

public class BudgetFormModel
{
    [Required(ErrorMessage = "Choose a category.")]
    [Display(Name = "Category")]
    public TransactionCategory? Category { get; set; }

    [Required(ErrorMessage = "Enter a monthly limit.")]
    [Range(typeof(decimal), "1", "100000", ErrorMessage = "Enter a monthly limit between $1.00 and $100,000.")]
    [Display(Name = "Monthly limit")]
    public decimal? MonthlyLimit { get; set; }
}

/// <summary>Dashboard widget: budgets at 75% or more of their limit this month.</summary>
public record BudgetWidgetViewModel(bool HasBudgets, List<BudgetProgress> NearLimit);

/// <summary>Months in URLs and the API are "yyyy-MM" (e.g. 2026-11).</summary>
public static class MonthParam
{
    public static string Format(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out DateOnly month)
    {
        month = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateOnly.TryParseExact(value.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        month = d;
        return true;
    }
}

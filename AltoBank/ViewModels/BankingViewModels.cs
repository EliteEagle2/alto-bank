using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AltoBank.ViewModels;

/// <summary>Everything the dashboard page shows.</summary>
public class DashboardViewModel
{
    public string FirstName { get; set; } = string.Empty;
    public string Greeting { get; set; } = "Hello";
    public List<Account> Accounts { get; set; } = new();
    public decimal TotalBalance { get; set; }
    public List<Transaction> RecentTransactions { get; set; } = new();
    public List<AltoBank.Dtos.DailyFlowDto> Flows { get; set; } = new();   // 30-day in/out chart
    public BudgetWidgetViewModel Budgets { get; set; } = new(false, new());   // budgets at 75%+ this month
}

/// <summary>Shared form for Deposit and Withdraw (same fields, different action).</summary>
public class MoneyMovementViewModel
{
    [Required, Display(Name = "Account")]
    public int AccountId { get; set; }

    // Range starts at 0.01, so zero and negative amounts are rejected.
    [Required, Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Enter an amount between $0.01 and $1,000,000.")]
    public decimal Amount { get; set; }

    [StringLength(60)]
    public string? Description { get; set; }

    // Dropdown items — filled in by the controller, not posted back by the form.
    [ValidateNever]
    public List<SelectListItem> AccountOptions { get; set; } = new();
}

/// <summary>Form for moving money between two of the user's accounts.</summary>
public class TransferViewModel
{
    [Required, Display(Name = "From account")]
    public int FromAccountId { get; set; }

    [Required, Display(Name = "To account")]
    public int ToAccountId { get; set; }

    [Required, Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Enter an amount between $0.01 and $1,000,000.")]
    public decimal Amount { get; set; }

    [StringLength(40)]
    public string? Memo { get; set; }

    [ValidateNever]
    public List<SelectListItem> AccountOptions { get; set; } = new();
}

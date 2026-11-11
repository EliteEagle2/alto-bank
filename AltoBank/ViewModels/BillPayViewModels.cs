using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AltoBank.ViewModels;

/// <summary>Add / edit payee form.</summary>
public class PayeeInput
{
    [Required(ErrorMessage = "Enter the payee's name."), StringLength(60)]
    [Display(Name = "Payee name")]
    public string Name { get; set; } = string.Empty;

    public PayeeCategory Category { get; set; } = PayeeCategory.Utilities;

    [Required(ErrorMessage = "Enter your account number at this payee."), StringLength(30, MinimumLength = 4)]
    [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "Use letters, digits and dashes only.")]
    [Display(Name = "Your account number with them")]
    public string AccountNumber { get; set; } = string.Empty;

    [StringLength(30)]
    [Display(Name = "Nickname (optional)")]
    public string? Nickname { get; set; }

    public PayeeData ToData() => new(Name, Category, AccountNumber, Nickname);
}

/// <summary>
/// "Pay a bill" and "Schedule a transfer" form. When: "now" (bill only), "once" (on Date),
/// "weekly" (every DayOfWeek from Date), "monthly" (on DayOfMonth from Date). The server re-checks everything.
/// </summary>
public class SchedulePaymentInput
{
    [Display(Name = "From")]
    public int FromAccountId { get; set; }

    [Display(Name = "Pay to")]
    public int? PayeeId { get; set; }

    [Display(Name = "To")]
    public int? ToAccountId { get; set; }

    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Amount must be between $0.01 and $1,000,000.")]
    public decimal Amount { get; set; }

    [StringLength(80)]
    [Display(Name = "Memo (optional)")]
    public string? Memo { get; set; }

    public string When { get; set; } = "now";

    [Display(Name = "Date")]
    public DateOnly? Date { get; set; }

    [Display(Name = "Every")]
    public DayOfWeek? DayOfWeek { get; set; }

    [Range(1, 31)]
    [Display(Name = "On day")]
    public int? DayOfMonth { get; set; }

    [Display(Name = "End date (optional)")]
    public DateOnly? EndDate { get; set; }

    public ScheduleFrequency Frequency => When switch
    {
        "weekly" => ScheduleFrequency.Weekly,
        "monthly" => ScheduleFrequency.Monthly,
        _ => ScheduleFrequency.Once
    };

    public ScheduleData ToData(ScheduleKind kind) => new(kind, FromAccountId, PayeeId, ToAccountId, Amount, Memo,
        Frequency, Date ?? BankTime.Today, DayOfWeek, DayOfMonth, EndDate);
}

/// <summary>/bills — payees, the pay form, and what's coming up for bills.</summary>
public class BillPayPageViewModel
{
    public List<Payee> Payees { get; set; } = new();
    public List<SelectListItem> FromAccounts { get; set; } = new();
    public SchedulePaymentInput Pay { get; set; } = new();
    public PayeeInput NewPayee { get; set; } = new();
    public List<UpcomingItem> UpcomingBills { get; set; } = new();
    public Dictionary<int, int> ActiveSchedulesByPayee { get; set; } = new();
}

/// <summary>/scheduled — calendar month, upcoming list, every scheduled item, recent runs, and the transfer form.</summary>
public class UpcomingPageViewModel
{
    public DateOnly Month { get; set; }                       // first day of the month shown
    public List<UpcomingItem> MonthItems { get; set; } = new();
    public List<UpcomingItem> Next45Days { get; set; } = new();
    public List<ScheduledPayment> Items { get; set; } = new();
    public List<ScheduledRun> RecentRuns { get; set; } = new();
    public List<SelectListItem> Accounts { get; set; } = new();
    public SchedulePaymentInput Transfer { get; set; } = new() { When = "weekly" };
    public DateOnly Today { get; set; }
}

/// <summary>Edit one scheduled item (amount, account, next date, end date, memo).</summary>
public class ScheduleEditInput
{
    public int Id { get; set; }
    [Display(Name = "From")] public int FromAccountId { get; set; }
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Amount must be between $0.01 and $1,000,000.")]
    public decimal Amount { get; set; }
    [StringLength(80)][Display(Name = "Memo (optional)")] public string? Memo { get; set; }
    [Required][Display(Name = "Next payment date")] public DateOnly NextRunDate { get; set; }
    [Display(Name = "End date (optional)")] public DateOnly? EndDate { get; set; }

    // Display only
    public ScheduledPayment? Item { get; set; }
    public List<SelectListItem> Accounts { get; set; } = new();
}

/// <summary>The "when" controls shared by the bill and transfer forms (_ScheduleWhen partial).</summary>
public record ScheduleWhenViewModel(string Prefix, SchedulePaymentInput Input, bool AllowNow);

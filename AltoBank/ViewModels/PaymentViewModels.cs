using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AltoBank.ViewModels;

/// <summary>The "Send" form. The same rules are checked again in BankService (so the API can't skip them).</summary>
public class SendPaymentInput
{
    [Required(ErrorMessage = "Choose the account to send from.")]
    [Display(Name = "From")]
    public int FromAccountId { get; set; }

    [Required(ErrorMessage = "Enter the recipient's email or mobile number.")]
    [StringLength(100)]
    [Display(Name = "To (email or mobile number)")]
    public string To { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Amount must be between $0.01 and $1,000,000.")]
    public decimal Amount { get; set; }

    [StringLength(80)]
    [Display(Name = "Memo (optional)")]
    public string? Memo { get; set; }
}

/// <summary>One person on the "Request" form.</summary>
public class RequestPersonInput
{
    [StringLength(100)] public string? To { get; set; }
    [Range(typeof(decimal), "0", "1000000")] public decimal Amount { get; set; }
}

/// <summary>
/// The "Request" form. Mode: "single" (amount per person as typed), "even" (split SplitTotal evenly,
/// calculated on the server) or "custom" (typed amounts, which can't add up to more than SplitTotal).
/// </summary>
public class RequestMoneyInput
{
    public List<RequestPersonInput> People { get; set; } = new() { new() };

    [StringLength(80)]
    [Display(Name = "What's it for?")]
    public string? Memo { get; set; }

    public string Mode { get; set; } = "single";

    [Range(typeof(decimal), "0", "1000000")]
    [Display(Name = "Bill total")]
    public decimal? SplitTotal { get; set; }

    [Display(Name = "Include me in the split")]
    public bool IncludeMe { get; set; } = true;

    public NewRequest ToNewRequest() => new(
        People.Select(p => new RequestLine(p.To, p.Amount)).ToList(), Memo,
        Mode is "even" or "custom" ? SplitTotal : null, Mode == "even", IncludeMe);
}

/// <summary>Everything on /payments: the forms, the inbox, and the activity lists.</summary>
public class PaymentsPageViewModel
{
    public PaymentsOverview Overview { get; set; } = null!;
    public List<SelectListItem> SourceAccounts { get; set; } = new();   // active accounts that can send
    public SendPaymentInput Send { get; set; } = new();
    public RequestMoneyInput Request { get; set; } = new();
    public decimal DailyLimit { get; set; }
    public bool HasCheckingToReceive { get; set; }
}

/// <summary>The confirm step: "Sending $45.00 to Maya C." before any money moves.</summary>
public class SendReviewViewModel
{
    public SendPaymentInput Input { get; set; } = new();
    public string RecipientName { get; set; } = string.Empty;
    public string FromLabel { get; set; } = string.Empty;
}

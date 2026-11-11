using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AltoBank.ViewModels;

/// <summary>Everything the Profile &amp; settings page shows (each section posts its own small form).</summary>
public class ProfilePageViewModel
{
    public ProfileViewModel Personal { get; set; } = new();
    public UserPreferences Prefs { get; set; } = new();
    public DateOnly? DateOfBirth { get; set; }
    public DateTime? MemberSince { get; set; }
    public List<SelectListItem> AccountOptions { get; set; } = new();
    public List<SelectListItem> States { get; set; } = new();
    public List<SignInEntry> RecentSignIns { get; set; } = new();

    /// <summary>A light "profile strength" meter: how many optional details are filled in.</summary>
    public int CompletionPercent { get; set; }
    public List<string> Missing { get; set; } = new();
}

/// <summary>One recent sign-in (from the audit log).</summary>
public record SignInEntry(DateTime At, string? IpAddress, string Method);

/// <summary>Profile → Alerts. Unchecked categories are muted.</summary>
public class AlertSettingsInput
{
    public List<AlertCategory> Enabled { get; set; } = new();

    [Display(Name = "Low balance alert")]
    [Range(0.01, 100000, ErrorMessage = "Low balance amount must be between $0.01 and $100,000.")]
    public decimal? LowBalanceAlert { get; set; }

    [Display(Name = "Large purchase alert")]
    [Range(0.01, 100000, ErrorMessage = "Large purchase amount must be between $0.01 and $100,000.")]
    public decimal? LargeDebitAlert { get; set; }
}

/// <summary>Profile → Preferences.</summary>
public class PreferencesInput
{
    public int? DefaultAccountId { get; set; }
    public bool HideBalances { get; set; }
    [Required] public string Theme { get; set; } = "aurora";
    public bool Paperless { get; set; }
    public bool DiscoverableByPhone { get; set; }
}

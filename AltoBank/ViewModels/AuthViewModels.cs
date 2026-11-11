using System.ComponentModel.DataAnnotations;

namespace AltoBank.ViewModels;

// ViewModels hold ONLY the data a form needs, plus validation rules.
// This keeps database models (and things like PasswordHash) out of the views.

public class RegisterViewModel
{
    [Required, StringLength(50), Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(50), Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current (temporary) password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8), Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ProfileViewModel
{
    [Required, StringLength(50), Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(50), Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(30), Display(Name = "Preferred name")]
    public string? PreferredName { get; set; }

    [Display(Name = "Mobile phone")]
    public string? Phone { get; set; }

    [StringLength(100), Display(Name = "Street address")] public string? Street { get; set; }
    [StringLength(50), Display(Name = "Apt, suite, unit")] public string? Apt { get; set; }
    [StringLength(60)] public string? City { get; set; }
    [StringLength(2)] public string? State { get; set; }
    [RegularExpression(@"^\d{5}$", ErrorMessage = "ZIP code must be exactly 5 digits."), Display(Name = "ZIP code")]
    public string? Zip { get; set; }

    // Past account applications, listed under the form (not posted back).
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
    public List<AltoBank.Models.AccountApplication> Applications { get; set; } = new();
}

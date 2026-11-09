using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AltoBank.Models;

/// <summary>
/// The logged-in customer. Extends ASP.NET Core Identity's IdentityUser
/// (which already has Email, PhoneNumber, PasswordHash, etc.) with name, birth date, and address.
/// The extra fields are filled in by the first account application so later applications can prefill them.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    [StringLength(1)] public string? MiddleInitial { get; set; }
    public DateOnly? DateOfBirth { get; set; }

    // Home address
    [StringLength(100)] public string? Street { get; set; }
    [StringLength(50)] public string? Apt { get; set; }
    [StringLength(60)] public string? City { get; set; }
    [StringLength(2)] public string? State { get; set; }
    [StringLength(5)] public string? Zip { get; set; }

    // True once we have enough saved info to offer the "Is this still correct?" shortcut.
    public bool HasApplicationProfile => DateOfBirth != null && !string.IsNullOrEmpty(Street) && !string.IsNullOrEmpty(PhoneNumber);

    // Navigation properties
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<AccountApplication> Applications { get; set; } = new List<AccountApplication>();
}

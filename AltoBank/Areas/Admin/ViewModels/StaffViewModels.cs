using System.ComponentModel.DataAnnotations;

namespace AltoBank.Areas.Admin.ViewModels;

/// <summary>One person with admin access (Staff page).</summary>
public record StaffMember(string UserId, string Name, string Email, bool IsOwner, bool IsCustomer, bool IsLocked,
                          DateTime? CreatedAt, bool MustChangePassword);

public class StaffPageViewModel
{
    public List<StaffMember> Staff { get; set; } = new();
    /// <summary>True only for the owner: shows the add / remove controls.</summary>
    public bool CanManage { get; set; }
    public AddStaffInput Add { get; set; } = new();
}

/// <summary>"Add an admin": promote an existing login, or create a new staff login.</summary>
public class AddStaffInput
{
    public const string Promote = "promote";
    public const string Create = "create";

    public string Mode { get; set; } = Promote;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [StringLength(50), Display(Name = "First name")] public string? FirstName { get; set; }
    [StringLength(50), Display(Name = "Last name")] public string? LastName { get; set; }

    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}

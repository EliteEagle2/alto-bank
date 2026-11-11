using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Data;
using AltoBank.Models;
using Microsoft.AspNetCore.Identity;

namespace AltoBank.Areas.Admin.Services;

public partial interface IAdminService
{
    // ----- Staff (who can use the admin console) -----
    Task<StaffPageViewModel> GetStaffPageAsync();
    /// <summary>OWNER ONLY. Gives an existing user admin access, or creates a new staff login (temporary password).</summary>
    Task<(AdminResult Result, string? TempPassword)> AddStaffAsync(AddStaffInput input);
    /// <summary>OWNER ONLY. Removes admin access (the login itself is kept; nothing is deleted).</summary>
    Task<AdminResult> RevokeStaffAsync(string userId, string reason);
}

/// <summary>
/// STAFF MANAGEMENT. Three levels:
///   Customer → online banking only.
///   Admin    → the admin console (customers, accounts, transactions, settings...).
///   Owner    → everything an admin can do, plus adding/removing admins. Exactly one owner, set in configuration
///              (Bank:OwnerEmail), never from the website, so no admin can promote themselves to owner.
/// Every change needs a reason and is audited; role changes take effect on the person's next page load
/// (the security stamp is checked on every request).
/// </summary>
public partial class AdminService
{
    private bool CallerIsOwner => _http.HttpContext?.User.IsInRole(SeedData.OwnerRole) == true;

    public async Task<StaffPageViewModel> GetStaffPageAsync()
    {
        var admins = await _users.GetUsersInRoleAsync(SeedData.AdminRole);
        var owners = (await _users.GetUsersInRoleAsync(SeedData.OwnerRole)).Select(u => u.Id).ToHashSet();
        var customers = (await _users.GetUsersInRoleAsync(SeedData.CustomerRole)).Select(u => u.Id).ToHashSet();
        var staff = admins
            .Select(u => new StaffMember(u.Id, $"{u.FirstName} {u.LastName}".Trim(), u.Email ?? "", owners.Contains(u.Id),
                                         customers.Contains(u.Id), u.LockoutEnd > DateTimeOffset.UtcNow, u.CreatedAt, u.MustChangePassword))
            .OrderByDescending(s => s.IsOwner).ThenBy(s => s.Name).ToList();
        return new StaffPageViewModel { Staff = staff, CanManage = CallerIsOwner };
    }

    public async Task<(AdminResult Result, string? TempPassword)> AddStaffAsync(AddStaffInput input)
    {
        if (!CallerIsOwner) return (Fail("Only the owner can add admins."), null);
        string? temp = null;
        var result = await ExecuteAsync(input.Reason, true, async () =>
        {
            var email = (input.Email ?? "").Trim();
            if (email.Length == 0) return Fail("Enter an email address.");
            var user = await _users.FindByEmailAsync(email);

            if (input.Mode == AddStaffInput.Promote)
            {
                if (user == null) return Fail("No one has a login with that email. To add someone new, choose \"Create a new staff login\".");
                if (await _users.IsInRoleAsync(user, SeedData.AdminRole)) return Fail($"{user.FirstName} {user.LastName} is already an admin.");
                if (IdentityError(await _users.AddToRoleAsync(user, SeedData.AdminRole)) is { } err) return Fail(err);
                _audit.Add("Staff.Grant", $"Gave admin access to {user.FirstName} {user.LastName} ({user.Email})", "Staff", user.Id, reason: input.Reason);
                return Ok($"{user.FirstName} {user.LastName} is now an admin. They'll see the admin console on their next page load.");
            }

            if (user != null) return Fail("Someone already has a login with that email. Choose \"Give an existing user admin access\" instead.");
            var first = (input.FirstName ?? "").Trim();
            var last = (input.LastName ?? "").Trim();
            if (first.Length == 0 || last.Length == 0) return Fail("Enter the new admin's first and last name.");
            if (first.Length > 50 || last.Length > 50) return Fail("Names can be at most 50 characters.");

            user = new ApplicationUser
            {
                UserName = email, Email = email, EmailConfirmed = true, FirstName = first, LastName = last,
                CreatedAt = DateTime.UtcNow, MustChangePassword = true
            };
            temp = GenerateTempPassword();
            var created = await _users.CreateAsync(user, temp);
            if (!created.Succeeded) return Fail(string.Join(" ", created.Errors.Select(e => e.Description)));
            if (IdentityError(await _users.AddToRoleAsync(user, SeedData.AdminRole)) is { } roleError) return Fail(roleError);
            _audit.Add("Staff.Create", $"Created staff login for {first} {last} ({email}) with admin access", "Staff", user.Id, reason: input.Reason);
            return Ok($"{first} {last} was added as an admin. Give them the temporary password below; they must change it at first login.");
        });
        return (result, result.Success ? temp : null);
    }

    public async Task<AdminResult> RevokeStaffAsync(string userId, string reason)
    {
        if (!CallerIsOwner) return Fail("Only the owner can remove admins.");
        return await ExecuteAsync(reason, true, async () =>
        {
            var user = await _users.FindByIdAsync(userId);
            if (user == null || !await _users.IsInRoleAsync(user, SeedData.AdminRole)) return Fail("That person isn't an admin.");
            if (await _users.IsInRoleAsync(user, SeedData.OwnerRole))
                return Fail("The owner's access comes from the server configuration (Bank:OwnerEmail) and can't be removed here.");
            if (IdentityError(await _users.RemoveFromRoleAsync(user, SeedData.AdminRole)) is { } err) return Fail(err);
            var stillCustomer = await _users.IsInRoleAsync(user, SeedData.CustomerRole);
            _audit.Add("Staff.Revoke", $"Removed admin access from {user.FirstName} {user.LastName} ({user.Email})", "Staff", user.Id, reason: reason);
            return Ok($"{user.FirstName} {user.LastName} no longer has admin access." +
                      (stillCustomer ? " Their own bank accounts are unaffected." : " Their login is kept but can't open the admin console."));
        });
    }
}

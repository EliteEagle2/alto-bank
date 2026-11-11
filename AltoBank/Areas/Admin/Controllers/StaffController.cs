using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin/staff — who can use the admin console. Every admin can SEE the list; only the OWNER (set in
/// configuration, Bank:OwnerEmail) can add or remove admins. The owner check is enforced twice: by
/// [Authorize(Roles = Owner)] here and again inside IAdminService.
/// </summary>
public class StaffController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public StaffController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Staff";
        ViewBag.TempPassword = TempData["TempPassword"] as string;   // shown ONCE after creating a staff login
        return View(await _admin.GetStaffPageAsync());
    }

    [HttpPost, Authorize(Roles = SeedData.OwnerRole)]
    public async Task<IActionResult> Add([Bind(Prefix = "Add")] AddStaffInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Check the form.";
            return RedirectToAction(nameof(Index));
        }
        var (result, temp) = await _admin.AddStaffAsync(input);
        Flash(result);
        if (temp != null) TempData["TempPassword"] = temp;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = SeedData.OwnerRole)]
    public async Task<IActionResult> Revoke(string id, string reason)
    {
        Flash(await _admin.RevokeStaffAsync(id, reason));
        return RedirectToAction(nameof(Index));
    }
}

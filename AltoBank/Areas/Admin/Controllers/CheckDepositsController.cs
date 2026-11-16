using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin/checkdeposits — the mobile check deposit review queue (Pending / Approved / Rejected, paged), a detail page
/// with both images, and Approve / Reject through IAdminService (transaction + audit row + required reason).
/// Images are served by Image(), which is Admin-only (AdminControllerBase) and never cached.
/// </summary>
public class CheckDepositsController : AdminControllerBase
{
    private readonly IAdminService _admin;
    private readonly ICheckDepositService _checks;

    public CheckDepositsController(IAdminService admin, ICheckDepositService checks)
    {
        _admin = admin;
        _checks = checks;
    }

    public async Task<IActionResult> Index([FromQuery] CheckDepositListQuery query)
    {
        ViewData["Title"] = "Check Deposits";
        // It's a work queue: with no status in the URL, open the Pending checks. "All" sends ?status=all.
        if (!Request.Query.ContainsKey("status")) query.Status = CheckDepositStatus.Pending;
        return View(await _admin.GetCheckDepositsPageAsync(query));
    }

    public async Task<IActionResult> Details(int id)
    {
        var vm = await _admin.GetCheckDepositAsync(id);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"Check {vm.Deposit.Reference}";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Approve(int id, string reason)
    {
        Flash(await _admin.ApproveCheckDepositAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Reject(int id, string reason)
    {
        Flash(await _admin.RejectCheckDepositAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>GET /admin/checkdeposits/image/5?side=front — the stored image, for staff only, never cached.</summary>
    public async Task<IActionResult> Image(int id, string side)
    {
        var file = await _checks.OpenImageForStaffAsync(id, side);
        if (file == null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Value.Stream, file.Value.ContentType);
    }
}

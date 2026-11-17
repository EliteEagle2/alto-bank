using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin/disputes — the dispute queue (Submitted / Under review / Approved / Denied, oldest first), a detail page, and
/// Start review / Approve / Deny through IAdminService (transaction + audit row; Deny needs a reason the customer sees).
/// The attachment is served by Attachment(), Admin-only (AdminControllerBase) and never cached.
/// </summary>
public class DisputesController : AdminControllerBase
{
    private readonly IAdminService _admin;
    private readonly IDisputeService _disputes;

    public DisputesController(IAdminService admin, IDisputeService disputes)
    {
        _admin = admin;
        _disputes = disputes;
    }

    public async Task<IActionResult> Index([FromQuery] DisputeListQuery query)
    {
        ViewData["Title"] = "Disputes";
        // A work queue: with no status in the URL, show the ones waiting (Submitted). "All" sends ?status=all.
        if (!Request.Query.ContainsKey("status")) query.Status = DisputeStatus.Submitted;
        return View(await _admin.GetDisputesPageAsync(query));
    }

    public async Task<IActionResult> Details(int id)
    {
        var vm = await _admin.GetDisputeAsync(id);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"Dispute {vm.Dispute.Reference}";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> StartReview(int id, string? reason)
    {
        Flash(await _admin.StartDisputeReviewAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Approve(int id, string reason)
    {
        Flash(await _admin.ApproveDisputeAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Deny(int id, string reason)
    {
        Flash(await _admin.DenyDisputeAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>GET /admin/disputes/attachment/5 — the customer's attachment, staff only, never cached.</summary>
    public async Task<IActionResult> Attachment(int id)
    {
        var file = await _disputes.OpenAttachmentForStaffAsync(id);
        if (file == null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Value.Stream, file.Value.ContentType);
    }
}

using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>/admin/applications — review queue: list by status, full detail, approve or deny (with reason).</summary>
public class ApplicationsController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public ApplicationsController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] ApplicationListQuery query)
    {
        ViewData["Title"] = "Applications";
        ViewBag.Query = query;
        ViewBag.Counts = await _admin.CountApplicationsAsync();
        return View(await _admin.SearchApplicationsAsync(query));
    }

    public async Task<IActionResult> Details(int id)
    {
        var app = await _admin.GetApplicationAsync(id);
        if (app == null) return NotFound();
        ViewData["Title"] = $"Application ALTO-{app.Id:D6}";
        return View(app);
    }

    [HttpPost]
    public async Task<IActionResult> Approve(int id, string reason)
    {
        Flash(await _admin.ApproveApplicationAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Deny(int id, string reason)
    {
        Flash(await _admin.DenyApplicationAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }
}

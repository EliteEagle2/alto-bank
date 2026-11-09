using AltoBank.Areas.Admin.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>/admin — OVERVIEW: KPI tiles, charts (from /api/admin/stats) and the "Needs attention" panel.</summary>
public class DashboardController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public DashboardController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Overview";
        return View(await _admin.GetOverviewAsync());
    }
}

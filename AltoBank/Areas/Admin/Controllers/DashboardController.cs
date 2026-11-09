using AltoBank.Data;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin — the admin OVERVIEW page.
/// [Area("Admin")] puts this controller in the Admin area (URL prefix /admin, views in Areas/Admin/Views).
/// [Authorize(Roles = "Admin")] means only admins get in; customers see the Access Denied page.
/// </summary>
[Area("Admin")]
[Authorize(Roles = SeedData.AdminRole)]
public class DashboardController : Controller
{
    private readonly IBankService _bank;

    public DashboardController(IBankService bank) => _bank = bank;

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Overview";
        return View(await _bank.GetRecentApplicationsAsync(count: 8));
    }
}

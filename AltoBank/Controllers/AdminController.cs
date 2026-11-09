using AltoBank.Data;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// Admin dashboard at /Admin. Only users in the "Admin" role (seeded in SeedData) can open it.
/// The KPI tiles and charts load their numbers from the JSON API (/api/stats) with JavaScript;
/// the recent applications table is rendered on the server with Razor.
/// </summary>
[Authorize(Roles = SeedData.AdminRole)]
public class AdminController : Controller
{
    private readonly IBankService _bank;

    public AdminController(IBankService bank) => _bank = bank;

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Admin Dashboard";
        return View(await _bank.GetRecentApplicationsAsync(count: 10));
    }
}

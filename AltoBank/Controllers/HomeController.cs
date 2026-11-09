using System.Diagnostics;
using AltoBank.Models;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>Public landing page — no login required.</summary>
public class HomeController : Controller
{
    // GET /  — hero, card graphic, and feature tiles.
    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

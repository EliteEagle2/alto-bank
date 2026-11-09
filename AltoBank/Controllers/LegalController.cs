using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>Public legal pages linked from the footer.</summary>
public class LegalController : Controller
{
    public IActionResult Privacy() => View();
    public IActionResult Terms() => View();
    public IActionResult Accessibility() => View();
}

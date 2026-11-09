using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>Public product pages — the Checking / Savings / Credit Cards / Loans links in the nav.</summary>
public class ProductsController : Controller
{
    public IActionResult Checking() => View();     // /Products/Checking
    public IActionResult Savings() => View();      // /Products/Savings (includes a growth calculator)
    public IActionResult CreditCards() => View();  // /Products/CreditCards
    public IActionResult Loans() => View();        // /Products/Loans (includes a payment calculator)
}

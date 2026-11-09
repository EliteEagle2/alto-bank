using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Models;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>/admin/customers — search, create, view and manage customers. Every change goes through IAdminService.</summary>
public class CustomersController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public CustomersController(IAdminService admin) => _admin = admin;

    // GET /admin/customers?search=&sort=&desc=&page=
    public async Task<IActionResult> Index([FromQuery] CustomerListQuery query)
    {
        ViewData["Title"] = "Customers";
        ViewBag.Query = query;
        return View(await _admin.SearchCustomersAsync(query));
    }

    // GET /admin/customers/details/{id}?tab=profile
    public async Task<IActionResult> Details(string id, string tab = "profile")
    {
        var vm = await _admin.GetCustomerAsync(id);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"{vm.User.FirstName} {vm.User.LastName}";
        ViewBag.Tab = tab;
        ViewBag.TempPassword = TempData["TempPassword"] as string;   // shown ONCE after create / reset
        return View(vm);
    }

    [HttpGet]
    public IActionResult Create()
    {
        ViewData["Title"] = "Create customer";
        return View(new CustomerProfileInput());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CustomerProfileInput input)
    {
        ViewData["Title"] = "Create customer";
        if (!ModelState.IsValid) return View(input);
        var (result, userId, temp) = await _admin.CreateCustomerAsync(input);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return View(input);
        }
        Flash(result);
        TempData["TempPassword"] = temp;
        return RedirectToAction(nameof(Details), new { id = userId, tab = "security" });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProfile(string id, [Bind(Prefix = "Profile")] CustomerProfileInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please fix the profile: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id, tab = "profile" });
        }
        Flash(await _admin.UpdateCustomerProfileAsync(id, input));
        return RedirectToAction(nameof(Details), new { id, tab = "profile" });
    }

    [HttpPost]
    public async Task<IActionResult> OpenAccount(string id, AccountType type, string? nickname, decimal? openingDeposit, string reason)
    {
        Flash(await _admin.OpenAccountForCustomerAsync(id, type, nickname, openingDeposit ?? 0m, reason));
        return RedirectToAction(nameof(Details), new { id, tab = "accounts" });
    }

    [HttpPost]
    public async Task<IActionResult> AddNote(string id, string text)
    {
        Flash(await _admin.AddNoteAsync(id, text));
        return RedirectToAction(nameof(Details), new { id, tab = "notes" });
    }

    [HttpPost]
    public async Task<IActionResult> Lock(string id, string reason)
    {
        Flash(await _admin.LockCustomerAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id, tab = "security" });
    }

    [HttpPost]
    public async Task<IActionResult> Unlock(string id, string reason)
    {
        Flash(await _admin.UnlockCustomerAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id, tab = "security" });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(string id, string reason)
    {
        var (result, temp) = await _admin.ResetPasswordAsync(id, reason);
        Flash(result);
        if (temp != null) TempData["TempPassword"] = temp;
        return RedirectToAction(nameof(Details), new { id, tab = "security" });
    }

    [HttpPost]
    public async Task<IActionResult> ForcePasswordChange(string id, bool required, string reason)
    {
        Flash(await _admin.SetForcePasswordChangeAsync(id, required, reason));
        return RedirectToAction(nameof(Details), new { id, tab = "security" });
    }

    // GET /admin/customers/viewas/{id} — READ-ONLY preview of the customer's dashboard.
    // The admin stays logged in as themselves (no impersonation); nothing on this page can change data.
    public async Task<IActionResult> ViewAs(string id)
    {
        var vm = await _admin.GetViewAsAsync(id);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"Viewing as {vm.User.FirstName} {vm.User.LastName}";
        return View(vm);
    }
}

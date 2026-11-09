using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>/admin/settings — bank-wide settings (BankSetting row) and the monthly interest run.</summary>
public class SettingsController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public SettingsController(IAdminService admin) => _admin = admin;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Settings";
        var input = await _admin.GetSettingsAsync();
        input.Reason = "";
        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Index(SettingsInput input)
    {
        ViewData["Title"] = "Settings";
        if (!ModelState.IsValid)
        {
            var current = await _admin.GetSettingsAsync();
            input.UpdatedAt = current.UpdatedAt;
            input.UpdatedBy = current.UpdatedBy;
            return View(input);
        }
        Flash(await _admin.UpdateSettingsAsync(input));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RunInterest(string reason)
    {
        var (result, summary) = await _admin.RunMonthlyInterestAsync(reason);
        Flash(result);
        if (summary != null)
        {
            // One line per account for the summary panel on the next page (TempData holds simple values).
            TempData["InterestLines"] = string.Join("\n", summary.Lines.Select(l => $"{l.Account}|{l.Amount:C}"));
        }
        return RedirectToAction(nameof(Index));
    }
}

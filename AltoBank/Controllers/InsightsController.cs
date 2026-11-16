using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// SPENDING INSIGHTS — /insights
///   GET  /insights?month=2026-11&amp;category=Groceries   one month: donut, month-over-month bars, top merchants,
///                                                     money in vs out, callouts; optional category drill-down
///   POST /transactions/{id}/category                   recategorize one transaction (the expanded row's form)
/// The charts read their numbers from /api/insights (Controllers/Api/InsightsApiController.cs).
/// Every lookup is filtered by the signed-in customer, so another customer's transaction id is a 404.
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
public class InsightsController : Controller
{
    private readonly IInsightsService _insights;
    private readonly IBudgetService _budgets;
    private readonly UserManager<ApplicationUser> _userManager;

    public InsightsController(IInsightsService insights, IBudgetService budgets, UserManager<ApplicationUser> userManager)
    {
        _insights = insights;
        _budgets = budgets;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("insights")]
    public async Task<IActionResult> Index(string? month, string? category)
    {
        var picked = MonthParam.TryParse(month, out var m) ? m : BankTime.FirstOfThisMonth;
        var data = await _insights.GetMonthAsync(UserId, picked);   // clamps to the months this customer has banked
        TransactionCategory? cat = Categories.TryParse(category, out var c) ? c : null;
        ViewData["Title"] = "Insights";
        return View(new InsightsPageViewModel
        {
            Data = data,
            Category = cat,
            CategoryTransactions = cat is TransactionCategory pick ? await _insights.GetCategoryTransactionsAsync(UserId, data.Month, pick) : new()
        });
    }

    /// <summary>
    /// The category picker in an expanded transaction row (Account Details, Insights drill-down).
    /// category = "auto" puts it back to the automatic category. Post/Redirect/Get back to where it came from.
    /// </summary>
    [HttpPost("transactions/{id:int}/category")]
    public async Task<IActionResult> Recategorize(int id, string? category, string? returnUrl)
    {
        TransactionCategory? chosen = null;
        if (category != "auto")
        {
            if (!Categories.TryParse(category, out var c)) { TempData["Error"] = "Choose a category."; return Back(returnUrl); }
            chosen = c;
        }
        var outcome = await _budgets.RecategorizeAsync(UserId, id, chosen);
        if (outcome == null) return NotFound();   // not this customer's transaction
        TempData[outcome.Value.Result.Success ? "Success" : "Error"] = outcome.Value.Result.Message;
        return Back(returnUrl);
    }

    private IActionResult Back(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
}

/// <summary>
/// BUDGETS — /budgets
///   GET  /budgets                 progress for this month + add / change / remove
///   POST /budgets                 add a budget, or change the limit of an existing one
///   POST /budgets/{id}/remove     soft delete (Status = Removed)
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("budgets")]
public class BudgetsController : Controller
{
    private readonly IBudgetService _budgets;
    private readonly IInsightsService _insights;
    private readonly UserManager<ApplicationUser> _userManager;

    public BudgetsController(IBudgetService budgets, IInsightsService insights, UserManager<ApplicationUser> userManager)
    {
        _budgets = budgets;
        _insights = insights;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await BuildAsync(new BudgetFormModel()));

    [HttpPost("")]
    public async Task<IActionResult> Save([Bind(Prefix = "Form")] BudgetFormModel form)
    {
        var result = ModelState.IsValid
            ? await _budgets.SaveAsync(UserId, form.Category!.Value, form.MonthlyLimit!.Value)
            : new BankResult(false, ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Check the form.");
        if (!result.Success)
        {
            // "Change limit" on an existing budget: that form lives inside the budget's row, so show the problem at the
            // top of the page instead (Post/Redirect/Get). The "Add a budget" form shows it next to the field.
            var page = await BuildAsync(form);
            if (form.Category is TransactionCategory c && page.Budgets.Any(b => b.Category == c))
            {
                TempData["Error"] = $"{Categories.Label(c)} budget not changed: {result.Message}";
                return RedirectToAction(nameof(Index));
            }
            if (ModelState.IsValid) ModelState.AddModelError("Form.MonthlyLimit", result.Message);
            return View(nameof(Index), page);
        }
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/remove")]
    public async Task<IActionResult> Remove(int id)
    {
        var result = await _budgets.RemoveAsync(UserId, id);
        if (result == null) return NotFound();
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    private async Task<BudgetsPageViewModel> BuildAsync(BudgetFormModel form)
    {
        ViewData["Title"] = "Budgets";
        var month = BankTime.FirstOfThisMonth;

        return new BudgetsPageViewModel
        {
            Month = month,
            Budgets = await _budgets.GetProgressAsync(UserId, month),
            LastMonth = await _insights.GetSpendingByCategoryAsync(UserId, month.AddMonths(-1)),
            Form = form
        };
    }
}

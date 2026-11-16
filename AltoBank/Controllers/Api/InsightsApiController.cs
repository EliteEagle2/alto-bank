using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers.Api;

/// <summary>
/// SPENDING INSIGHTS JSON API (the Insights page's charts read from here).
///   GET  /api/insights/categories                    every category with its label and chart color
///   GET  /api/insights/summary?month=2026-11         donut data, top merchants, money in/out, callouts, budgets
///   GET  /api/insights/trend?month=2026-11           spending per month (up to 6 months ending at month)
///   GET  /api/insights/budgets                       this month's budget progress
///   POST /api/insights/transactions/{id}/category    recategorize one transaction ({ "category": "Groceries" } or "auto")
/// Months are bank months ("yyyy-MM"); a month before the customer's first account or after this month is clamped.
/// Another customer's transaction id returns 404. Responses are DTOs, never entities.
/// CSRF: like the other JSON APIs, the POST accepts JSON only (see AccountsApiController).
/// </summary>
[ApiController]
[Route("api/insights")]
[Authorize(Roles = SeedData.CustomerRole)]
[Produces("application/json")]
public class InsightsApiController : ControllerBase
{
    private readonly IInsightsService _insights;
    private readonly IBudgetService _budgets;
    private readonly UserManager<ApplicationUser> _userManager;

    public InsightsApiController(IInsightsService insights, IBudgetService budgets, UserManager<ApplicationUser> userManager)
    {
        _insights = insights;
        _budgets = budgets;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("categories")]
    public ActionResult<List<CategoryDto>> GetCategories() =>
        Categories.All.Select(c => new CategoryDto(c.ToString(), Categories.Label(c), Categories.Color(c), Categories.Icon(c), Categories.IsSpending(c))).ToList();

    [HttpGet("summary")]
    [ProducesResponseType(typeof(InsightsSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InsightsSummaryDto>> GetSummary(string? month)
    {
        if (ParseMonth(month) is not DateOnly m) return BadMonth();
        var d = await _insights.GetMonthAsync(UserId, m);
        return new InsightsSummaryDto(MonthParam.Format(d.Month), d.IsCurrentMonth, d.ComparedDays,
            d.Spending, d.PreviousSpending, d.MoneyIn, d.MoneyOut,
            d.Categories.Select(c => new CategorySpendDto(c.Category.ToString(), c.Label, c.Color, c.Amount, c.PreviousAmount, c.Count)).ToList(),
            d.TopMerchants.Select(x => new MerchantSpendDto(x.Name, Categories.Label(x.Category), x.Amount, x.Count)).ToList(),
            d.Callouts.Select(x => new CalloutDto(x.Tone, x.Text)).ToList(),
            d.Budgets.Select(ToDto).ToList());
    }

    [HttpGet("trend")]
    [ProducesResponseType(typeof(List<MonthSpendDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<MonthSpendDto>>> GetTrend(string? month)
    {
        if (ParseMonth(month) is not DateOnly m) return BadMonth();
        var d = await _insights.GetMonthAsync(UserId, m);
        return d.Trend.Select(t => new MonthSpendDto(MonthParam.Format(t.Month), t.Month.ToString("MMM yyyy"), t.Spending)).ToList();
    }

    [HttpGet("budgets")]
    public async Task<ActionResult<List<BudgetProgressDto>>> GetBudgets() =>
        (await _budgets.GetProgressAsync(UserId, BankTime.FirstOfThisMonth)).Select(ToDto).ToList();

    [HttpPost("transactions/{id:int}/category")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(RecategorizeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecategorizeResultDto>> Recategorize(int id, RecategorizeDto body)
    {
        TransactionCategory? chosen = null;
        if (!string.Equals(body.Category, "auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!Categories.TryParse(body.Category, out var c))
                return BadRequest(new ProblemDetails { Title = "Unknown category.", Status = 400 });
            chosen = c;
        }
        var outcome = await _budgets.RecategorizeAsync(UserId, id, chosen);
        if (outcome == null) return NotFound();
        var (result, t) = outcome.Value;
        if (!result.Success) return BadRequest(new ProblemDetails { Title = result.Message, Status = 400 });
        return new RecategorizeResultDto(true, result.Message, t.Category.ToString(), Categories.Label(t.Category), t.CategoryOverridden);
    }

    // No month = this month; a malformed one is a 400.
    private static DateOnly? ParseMonth(string? month) =>
        string.IsNullOrWhiteSpace(month) ? BankTime.FirstOfThisMonth : MonthParam.TryParse(month, out var m) ? m : null;

    private ActionResult BadMonth() => BadRequest(new ProblemDetails { Title = "Month must look like 2026-11.", Status = 400 });

    private static BudgetProgressDto ToDto(BudgetProgress b) =>
        new(b.BudgetId, b.Category.ToString(), b.Label, b.Limit, b.Spent, b.Remaining, b.Percent, b.State);
}

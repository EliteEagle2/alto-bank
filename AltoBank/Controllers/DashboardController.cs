using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>The signed-in home screen. [Authorize] means you must be logged in.</summary>
[Authorize(Roles = SeedData.CustomerRole)] // customers only; admins use /admin
public class DashboardController : Controller
{
    private readonly IBankService _bank;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPreferencesService _prefs;
    private readonly IBudgetService _budgets;

    public DashboardController(IBankService bank, UserManager<ApplicationUser> userManager, IPreferencesService prefs, IBudgetService budgets)
    {
        _budgets = budgets;
        _prefs = prefs;
        _bank = bank;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        // EAGER LOADING: one query loads the user + accounts + each account's recent transactions.
        var user = await _bank.GetDashboardDataAsync(_userManager.GetUserId(User)!, recentPerAccount: 8);
        if (user == null) return Challenge();

        var accounts = user.Accounts
            .OrderBy(a => a.AccountType).ThenBy(a => a.OpenedAt)
            .ToList();

        // Combine every account's recent transactions and keep the 8 newest overall (LINQ to Objects).
        var recent = accounts
            .SelectMany(a => a.Transactions)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Take(8)
            .ToList();

        var hour = BankTime.ToLocal(DateTime.UtcNow).Hour;   // bank time, not the server clock
        var prefs = await _prefs.GetAsync(user.Id);
        var budgets = await _budgets.GetProgressAsync(user.Id, BankTime.FirstOfThisMonth);   // already sorted: fullest first
        return View(new DashboardViewModel
        {
            FirstName = prefs.PreferredName ?? user.FirstName,   // Profile → preferred name
            Greeting = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening",
            Accounts = accounts,
            TotalBalance = accounts.Sum(a => a.Balance), // summed in memory (SQLite can't SUM decimals)
            RecentTransactions = recent,
            Flows = await _bank.GetDailyFlowsAsync(user.Id, days: 30),
            Budgets = new BudgetWidgetViewModel(budgets.Any(), budgets.Where(b => b.State != "ok").ToList())
        });
    }
}

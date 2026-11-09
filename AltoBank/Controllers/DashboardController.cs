using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>The signed-in home screen. [Authorize] means you must be logged in.</summary>
[Authorize]
public class DashboardController : Controller
{
    private readonly IBankService _bank;
    private readonly UserManager<ApplicationUser> _userManager;

    public DashboardController(IBankService bank, UserManager<ApplicationUser> userManager)
    {
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

        var hour = DateTime.Now.Hour;
        return View(new DashboardViewModel
        {
            FirstName = user.FirstName,
            Greeting = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening",
            Accounts = accounts,
            TotalBalance = accounts.Sum(a => a.Balance), // summed in memory (SQLite can't SUM decimals)
            RecentTransactions = recent
        });
    }
}

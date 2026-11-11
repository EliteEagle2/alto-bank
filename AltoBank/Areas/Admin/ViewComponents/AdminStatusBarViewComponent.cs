using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.ViewComponents;

/// <summary>What the admin status strip shows (under the console bar on every /admin page).</summary>
public record AdminStatusBarModel(DateTime BankNow, DateTime? SchedulerLastRunAt, int PendingApplications, int FrozenAccounts, int FailedPayments)
{
    /// <summary>The scheduler runs every minute; no run in the last 5 minutes means it's stopped or stuck.</summary>
    public bool SchedulerHealthy => SchedulerLastRunAt is DateTime t && DateTime.UtcNow - t < TimeSpan.FromMinutes(5);
}

/// <summary>
/// VIEW COMPONENT: the admin console's status strip (bank time, scheduler health, queue counts).
/// A view component (not a partial) because it loads its own data, so every admin controller doesn't have to.
/// Read-only: three COUNT queries and the settings row.
/// </summary>
public class AdminStatusBarViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;

    public AdminStatusBarViewComponent(ApplicationDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var settings = await _db.GetSettingsAsync();
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var model = new AdminStatusBarModel(
            BankTime.ToLocal(DateTime.UtcNow),
            settings.SchedulerLastRunAt,
            await _db.AccountApplications.CountAsync(a => a.Status == ApplicationStatus.Pending),
            await _db.Accounts.CountAsync(a => a.Status == AccountStatus.Frozen),
            await _db.ScheduledPayments.CountAsync(s => s.LastRunStatus == ScheduledRunStatus.Failed && s.LastRunAt >= weekAgo));
        return View("~/Areas/Admin/Views/Shared/Components/AdminStatusBar/Default.cshtml", model);
    }
}

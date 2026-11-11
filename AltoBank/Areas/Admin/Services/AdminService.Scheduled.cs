using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.Services;

public partial interface IAdminService
{
    // ----- Scheduled payments (Bill Pay + scheduled transfers) -----
    Task<ScheduledAdminViewModel> GetScheduledPageAsync(ScheduledListQuery query, int pageSize = 25);
    /// <summary>Runs the background scheduler right now (reason required, audited).</summary>
    Task<(AdminResult Result, SchedulerRunSummary? Summary)> RunSchedulerNowAsync(string reason);
}

public partial class AdminService
{
    public async Task<ScheduledAdminViewModel> GetScheduledPageAsync(ScheduledListQuery query, int pageSize = 25)
    {
        var today = BankTime.Today;
        var like = string.IsNullOrWhiteSpace(query.Customer) ? null : $"%{query.Customer.Trim()}%";
        var vm = new ScheduledAdminViewModel { Query = query };

        if (query.Failures)
        {
            var q = _db.ScheduledRuns.AsNoTracking()
                .Include(r => r.ScheduledPayment!).ThenInclude(s => s.User)
                .Include(r => r.ScheduledPayment!).ThenInclude(s => s.Payee)
                .Include(r => r.ScheduledPayment!).ThenInclude(s => s.ToAccount)
                .Include(r => r.ScheduledPayment!).ThenInclude(s => s.FromAccount)
                .Where(r => r.Status == ScheduledRunStatus.Failed);
            if (query.Kind is ScheduleKind k) q = q.Where(r => r.ScheduledPayment!.Kind == k);
            if (like != null) q = q.Where(r => EF.Functions.Like(r.ScheduledPayment!.User!.FirstName + " " + r.ScheduledPayment.User.LastName, like)
                                            || EF.Functions.Like(r.ScheduledPayment!.User!.Email!, like));
            vm.FailedRuns = await PageAsync(q.OrderByDescending(r => r.ProcessedAt), query.Page, pageSize);
        }
        else
        {
            var q = _db.ScheduledPayments.AsNoTracking().Include(s => s.User).Include(s => s.Payee)
                .Include(s => s.FromAccount).Include(s => s.ToAccount).AsQueryable();
            if (query.Status is ScheduleStatus st) q = q.Where(s => s.Status == st);
            if (query.Kind is ScheduleKind k) q = q.Where(s => s.Kind == k);
            if (like != null) q = q.Where(s => EF.Functions.Like(s.User!.FirstName + " " + s.User.LastName, like) || EF.Functions.Like(s.User!.Email!, like));
            vm.Items = await PageAsync(q.OrderBy(s => s.Status != ScheduleStatus.Active).ThenBy(s => s.NextRunDate).ThenByDescending(s => s.Id), query.Page, pageSize);
        }

        var weekAgo = DateTime.UtcNow.AddDays(-7);
        vm.ActiveCount = await _db.ScheduledPayments.CountAsync(s => s.Status == ScheduleStatus.Active);
        vm.DueToday = await _db.ScheduledPayments.CountAsync(s => s.Status == ScheduleStatus.Active && s.NextRunDate != null && s.NextRunDate <= today);
        vm.FailedLast7Days = await _db.ScheduledRuns.CountAsync(r => r.Status == ScheduledRunStatus.Failed && r.ProcessedAt >= weekAgo);
        var settings = await _db.GetSettingsAsync();
        vm.LastRunAt = settings.SchedulerLastRunAt;
        vm.LastRunSummary = settings.SchedulerLastRunSummary;
        vm.RecentRuns = await _db.SchedulerRunLogs.AsNoTracking().OrderByDescending(l => l.StartedAt).Take(8).ToListAsync();
        return vm;
    }

    /// <summary>
    /// "Run scheduler now". The run itself is many small transactions ON PURPOSE: each due payment is paid (or
    /// fails) in its own transaction, so one customer's failure can never roll back another's payment. The admin
    /// action is recorded in two audited transactions through ExecuteAsync: "Scheduler.RunRequested" (with the
    /// reason) BEFORE anything moves, so no money ever moves without the admin's request on record, then
    /// "Scheduler.Run" with what the run did. The payments themselves are logged as "System" (the scheduler did
    /// them), not as the admin.
    /// </summary>
    public async Task<(AdminResult Result, SchedulerRunSummary? Summary)> RunSchedulerNowAsync(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinReasonLength)
            return (Fail($"A reason of at least {MinReasonLength} characters is required for this action."), null);

        var requested = await ExecuteAsync(reason, true, () =>
        {
            _audit.Add("Scheduler.RunRequested", "Asked the scheduler to run now", "Scheduler", null, reason: reason);
            return Task.FromResult(Ok("Requested."));
        });
        if (!requested.Success) return (requested, null);

        var summary = await _scheduler.RunAsync("admin", default, AdminEmail);
        var result = await ExecuteAsync(reason, true, () =>
        {
            _audit.Add("Scheduler.Run", $"Ran the scheduler: {summary.Processed} processed, {summary.Succeeded} paid, {summary.Failed} failed, {summary.Skipped} skipped, {summary.RequestsExpired} request(s) expired",
                       "Scheduler", null, after: new { summary.Processed, summary.Succeeded, summary.Failed, summary.Skipped, summary.RequestsExpired }, reason: reason);
            return Task.FromResult(Ok($"Scheduler ran: {summary.Processed} processed ({summary.Succeeded} paid, {summary.Failed} failed, {summary.Skipped} skipped), {summary.RequestsExpired} request(s) expired."));
        });
        return (result, summary);
    }
}

using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services.Scheduling;

/// <summary>
/// ONE scheduler run (every minute from SchedulerService, or the admin's "Run scheduler now"):
///   1. every Active scheduled item whose NextRunDate is today or earlier (bank time) is run through
///      IBankService.ExecuteScheduledAsync, one date at a time, so missed dates after downtime are caught up
///      (each date still at most once);
///   2. money requests older than 14 days are expired (with an alert to the requester);
///   3. a SchedulerRunLog row is saved (shown on the admin Scheduled Payments page).
/// Each item runs in its OWN transaction, so one failure never undoes another payment.
/// </summary>
public class SchedulerRunner : ISchedulerRunner
{
    private const int MaxCatchUpPerItem = 12;

    private readonly ApplicationDbContext _db;
    private readonly IBankService _bank;
    private readonly IPaymentRequestService _requests;

    public SchedulerRunner(ApplicationDbContext db, IBankService bank, IPaymentRequestService requests)
    {
        _db = db;
        _bank = bank;
        _requests = requests;
    }

    public async Task<SchedulerRunSummary> RunAsync(string trigger, CancellationToken cancellationToken = default, string? triggeredBy = null)
    {
        var started = DateTime.UtcNow;
        var today = BankTime.Today;
        int processed = 0, succeeded = 0, failed = 0, skipped = 0;

        var due = await _db.ScheduledPayments.AsNoTracking()
            .Where(s => s.Status == ScheduleStatus.Active && s.NextRunDate != null && s.NextRunDate <= today)
            .OrderBy(s => s.NextRunDate).ThenBy(s => s.Id)
            .Select(s => s.Id).ToListAsync(cancellationToken);

        foreach (var id in due)
        {
            for (int i = 0; i < MaxCatchUpPerItem && !cancellationToken.IsCancellationRequested; i++)
            {
                var outcome = await _bank.ExecuteScheduledAsync(id, today);
                if (!outcome.Ran) break;
                processed++;
                switch (outcome.Status)
                {
                    case ScheduledRunStatus.Succeeded: succeeded++; break;
                    case ScheduledRunStatus.Failed: failed++; break;
                    case ScheduledRunStatus.Skipped: skipped++; break;
                }
            }
        }

        var expired = await _requests.ExpireDueAsync();
        var summary = new SchedulerRunSummary(trigger, started, DateTime.UtcNow, processed, succeeded, failed, skipped, expired);

        _db.ChangeTracker.Clear();
        // "Last run" is updated every time; a log row is kept only when the run did something or an admin started it
        // (one row a minute forever would be ~1,400 rows a day of "nothing happened").
        var text = $"{processed} processed, {succeeded} succeeded, {failed} failed, {skipped} skipped, {expired} request(s) expired ({trigger})";
        await _db.BankSettings.Where(s => s.Id == BankSetting.SingletonId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.SchedulerLastRunAt, summary.FinishedAt).SetProperty(s => s.SchedulerLastRunSummary, text), cancellationToken);
        if (processed > 0 || expired > 0 || trigger == "admin")
        {
            _db.SchedulerRunLogs.Add(new SchedulerRunLog
            {
            Trigger = trigger, TriggeredBy = triggeredBy, StartedAt = summary.StartedAt, FinishedAt = summary.FinishedAt,
            Processed = processed, Succeeded = succeeded, Failed = failed, Skipped = skipped, RequestsExpired = expired
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        return summary;
    }
}

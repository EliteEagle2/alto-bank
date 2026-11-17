using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services.Scheduling;

/// <summary>
/// ONE scheduler run (every minute from SchedulerService, or the admin's "Run scheduler now"):
///   1. every Active scheduled item whose NextRunDate is today or earlier (bank time) is run through
///      IBankService.ExecuteScheduledAsync. After downtime, older missed dates are recorded as "missed" (no money
///      moves) and only the most recent due date is paid; each date is handled at most once;
///   2. every open Alto Peak card's cycle: statement close (with interest if the last one wasn't paid in full), autopay on
///      the due date, a late fee after it (BankService.RunCardCycleAsync);
///   3. money requests older than 14 days are expired (with an alert to the requester);
///   4. a SchedulerRunLog row is saved (shown on the admin Scheduled Payments page).
/// Each item runs in its OWN transaction, so one failure never undoes another payment.
/// </summary>
public class SchedulerRunner : ISchedulerRunner
{
    private const int MaxCatchUpPerItem = 12;

    private readonly ApplicationDbContext _db;
    private readonly IBankService _bank;
    private readonly IPaymentRequestService _requests;
    private readonly ILogger<SchedulerRunner> _log;

    public SchedulerRunner(ApplicationDbContext db, IBankService bank, IPaymentRequestService requests, ILogger<SchedulerRunner> log)
    {
        _db = db;
        _bank = bank;
        _requests = requests;
        _log = log;
    }

    public async Task<SchedulerRunSummary> RunAsync(string trigger, CancellationToken cancellationToken = default, string? triggeredBy = null)
    {
        // Everything the scheduler does is the BANK acting, even when an admin pressed "Run scheduler now"
        // (that request is audited separately, under the admin's name).
        using var _ = AuditLogger.ActAs(new AuditActor(null, null, "System"));
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
                ScheduledOutcome outcome;
                try { outcome = await _bank.ExecuteScheduledAsync(id, today); }
                catch (Exception ex)
                {
                    // One broken item must never stop the others (or the request expiry below).
                    _log.LogError(ex, "Scheduler: scheduled item {Id} threw; moving on to the next item", id);
                    _db.ChangeTracker.Clear();
                    failed++;
                    break;
                }
                if (outcome.Error != null)
                    _log.LogError(outcome.Error, "Scheduler: scheduled item {Id} ({Date}): {Message}", id, outcome.RunDate, outcome.Message);
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

        // Alto Peak cards: statement close + interest, autopay on the due date, late fee after it (each idempotent).
        var cards = await _db.Accounts.AsNoTracking()
            .Where(a => a.AccountType == AccountType.CreditCard && a.Status != AccountStatus.Closed)
            .Select(a => a.Id).ToListAsync(cancellationToken);
        foreach (var cardId in cards)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var c = await _bank.RunCardCycleAsync(cardId, today);
                processed += c.StatementsClosed + c.AutopaysMade + c.AutopaysFailed + c.LateFees;
                succeeded += c.StatementsClosed + c.AutopaysMade;
                failed += c.AutopaysFailed;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scheduler: credit card {Id} cycle threw; moving on", cardId);
                _db.ChangeTracker.Clear();
                failed++;
            }
        }

        var expired = 0;
        try { expired = await _requests.ExpireDueAsync(); }
        catch (Exception ex) { _log.LogError(ex, "Scheduler: expiring money requests failed"); _db.ChangeTracker.Clear(); }
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

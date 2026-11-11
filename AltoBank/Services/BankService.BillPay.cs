using AltoBank.Models;
using AltoBank.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>What happened when the scheduler tried one item. Ran = false means nothing was due (or it was already done).</summary>
public record ScheduledOutcome(bool Ran, ScheduledRunStatus? Status, DateOnly? RunDate, string Message);

/// <summary>
/// BILL PAY + SCHEDULED TRANSFERS: the money side. Paying a bill now, and running one due scheduled item.
/// Same rules as every money movement: the account must be the owner's, Active, within its daily limit
/// (bill payments and scheduled transfers count), with enough money, under the Balance concurrency check.
/// </summary>
public partial class BankService
{
    private const string NothingDue = "Nothing due.";

    public Task<BankResult> PayBillAsync(string userId, int fromAccountId, int payeeId, decimal amount, string? memo)
    {
        if (CheckAmount(amount) is { } invalid) return Task.FromResult(invalid);
        memo = Clean(memo);
        if (memo?.Length > 80) return Task.FromResult(new BankResult(false, "Memo can be at most 80 characters."));
        return InTransactionAsync(async () => (await PayBillCoreAsync(userId, fromAccountId, payeeId, amount, memo)).Result);
    }

    /// <summary>Pays one bill. Must run inside a DB transaction (PayBillAsync's own, or the scheduler's).</summary>
    private async Task<(BankResult Result, Transaction? Debit)> PayBillCoreAsync(string userId, int fromAccountId, int payeeId, decimal amount, string? memo)
    {
        var payee = await _db.Payees.FirstOrDefaultAsync(p => p.Id == payeeId && p.UserId == userId && p.Status == PayeeStatus.Active);
        if (payee == null) return (new(false, "Payee not found."), null);
        var from = await GetAccountAsync(fromAccountId, userId);                        // ownership check
        if (from == null) return (new(false, "Account not found."), null);
        if (CanSend(from) is { } blocked) return (blocked, null);                        // frozen / closed
        if (await CheckDailyLimitAsync(from, amount) is { } overLimit) return (overLimit, null);
        if (amount > from.Balance) return (new(false, $"Insufficient funds. {from.DisplayName} has {from.Balance:C} available."), null);

        from.Balance -= amount;
        var note = memo == null ? "" : $" - {memo}";
        var debit = AddTransaction(from, TransactionType.BillPayment, amount, $"Bill payment - {payee.Name}{note}", memo);
        debit.CounterpartyName = payee.Name.Length > 60 ? payee.Name[..60] : payee.Name;
        await _db.SaveChangesAsync();
        _audit.Add("BillPay.Pay", $"Paid {amount:C} to {payee.Name} from ••{from.Last4}", "Payee", payee.Id.ToString(),
                   after: new { payee = payee.Id, from = from.Id, amount, transactionId = debit.Id });
        await _db.SaveChangesAsync();
        return (new(true, $"Paid {amount:C} to {payee.DisplayName} from {from.DisplayName}."), debit);
    }

    /// <summary>
    /// Runs ONE due occurrence of a scheduled item (called by the background scheduler, once per due date).
    ///  • Success: the ScheduledRun row, the money movement, and moving NextRunDate forward are ONE transaction.
    ///    The unique index on (item, date) means the same date can never be paid twice, even across restarts.
    ///  • Skip next: records a Skipped run, moves no money.
    ///  • Failure (insufficient funds, frozen account, over the limit, payee removed...): the money transaction is
    ///    rolled back, then a Failed run + an alert are saved. No silent retry: that date is done. A one-time item
    ///    becomes Failed; a recurring one moves on to its next date.
    /// </summary>
    public async Task<ScheduledOutcome> ExecuteScheduledAsync(int scheduledPaymentId, DateOnly today)
    {
        // The scheduler reuses one DbContext for many items: start from what's in the database right now.
        _db.ChangeTracker.Clear();
        DateOnly runDate = default;
        string? failure = null;
        ScheduledRunStatus status = ScheduledRunStatus.Succeeded;
        BankResult result;
        try
        {
            result = await InTransactionAsync(async () =>
            {
                var s = await LoadScheduleAsync(scheduledPaymentId);
                if (s == null || s.Status != ScheduleStatus.Active || s.NextRunDate is not DateOnly due || due > today)
                    return new BankResult(false, NothingDue);
                runDate = due;

                // Already recorded (e.g. a run committed but this copy of the item is behind): just move on.
                if (await _db.ScheduledRuns.AnyAsync(r => r.ScheduledPaymentId == s.Id && r.RunDate == due))
                {
                    Advance(s, due);
                    await _db.SaveChangesAsync();
                    return new BankResult(true, NothingDue);   // success = commit the catch-up of NextRunDate
                }

                var run = new ScheduledRun { ScheduledPaymentId = s.Id, RunDate = due, Amount = s.Amount, ProcessedAt = DateTime.UtcNow };
                _db.ScheduledRuns.Add(run);

                if (s.SkipNext)
                {
                    status = run.Status = ScheduledRunStatus.Skipped;
                    s.SkipNext = false;
                    Finish(s, due, ScheduledRunStatus.Skipped, null);
                    await _db.SaveChangesAsync();
                    return new BankResult(true, $"Skipped {due:MMM d} as requested.");
                }

                run.Status = ScheduledRunStatus.Succeeded;
                var (moved, debit) = s.Kind == ScheduleKind.BillPay
                    ? await PayBillCoreAsync(s.UserId, s.FromAccountId, s.PayeeId ?? 0, s.Amount, s.Memo)
                    : await TransferCoreAsync(s.UserId, s.FromAccountId, s.ToAccountId ?? 0, s.Amount, s.Memo ?? "Scheduled transfer");
                if (!moved.Success) { failure = moved.Message; return moved; }   // rolls back everything, run row included

                run.TransactionId = debit?.Id;
                Finish(s, due, ScheduledRunStatus.Succeeded, null);
                var from = await _db.Accounts.FirstAsync(a => a.Id == s.FromAccountId);
                if (s.Kind == ScheduleKind.BillPay)
                    _alerts.Add(s.UserId, AlertType.BillPaid, "Bill paid",
                        $"Paid {s.Amount:C} to {s.Payee?.DisplayName} from {from.DisplayName} ••{from.Last4} ({s.ScheduleText.ToLowerInvariant()}).", "/scheduled");
                else
                    _alerts.Add(s.UserId, AlertType.TransferScheduled, "Scheduled transfer done",
                        $"Moved {s.Amount:C} from {from.DisplayName} ••{from.Last4} to {s.Target}.", "/scheduled");
                await _db.SaveChangesAsync();
                return new BankResult(true, moved.Message);
            });
        }
        catch (DbUpdateException)
        {
            // The unique (item, date) index refused a second run: another scheduler run already paid it.
            _db.ChangeTracker.Clear();
            return new ScheduledOutcome(false, null, runDate, "Already processed by another run.");
        }

        if (result.Message == NothingDue) return new ScheduledOutcome(false, null, null, NothingDue);
        if (result.Success) return new ScheduledOutcome(true, status, runDate, result.Message);
        return await RecordFailureAsync(scheduledPaymentId, runDate, failure ?? result.Message);
    }

    // Saves a Failed run + alert in its own transaction (the money transaction was rolled back).
    private async Task<ScheduledOutcome> RecordFailureAsync(int scheduledPaymentId, DateOnly runDate, string reason)
    {
        _db.ChangeTracker.Clear();
        reason = reason.Length > 200 ? reason[..200] : reason;
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var s = await LoadScheduleAsync(scheduledPaymentId);
            if (s == null || await _db.ScheduledRuns.AnyAsync(r => r.ScheduledPaymentId == s.Id && r.RunDate == runDate))
                return new ScheduledOutcome(false, null, runDate, "Already processed by another run.");

            _db.ScheduledRuns.Add(new ScheduledRun
            {
                ScheduledPaymentId = s.Id, RunDate = runDate, Amount = s.Amount, Status = ScheduledRunStatus.Failed,
                FailureReason = reason, ProcessedAt = DateTime.UtcNow
            });
            Finish(s, runDate, ScheduledRunStatus.Failed, reason);
            if (!s.IsRecurring) { s.Status = ScheduleStatus.Failed; s.NextRunDate = null; }
            var what = s.Kind == ScheduleKind.BillPay ? $"Your {s.Amount:C} payment to {s.Target}" : $"Your {s.Amount:C} transfer to {s.Target}";
            _alerts.Add(s.UserId, AlertType.BillFailed, s.Kind == ScheduleKind.BillPay ? "Bill payment failed" : "Scheduled transfer failed",
                $"{what} on {runDate:MMM d} didn't go through: {reason}" + (s.IsRecurring && s.NextRunDate != null ? $" The next one is scheduled for {s.NextRunDate:MMM d}." : " It won't be retried."),
                "/scheduled");
            _audit.Add("Schedule.RunFailed", $"Scheduled {s.Kind} #{s.Id} for {runDate:yyyy-MM-dd} failed: {reason}", "ScheduledPayment", s.Id.ToString(),
                       after: new { runDate, reason, next = s.NextRunDate }, actor: new AuditActor(null, null, "System"));
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return new ScheduledOutcome(true, ScheduledRunStatus.Failed, runDate, reason);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return new ScheduledOutcome(false, null, runDate, "Already processed by another run.");
        }
    }

    private Task<ScheduledPayment?> LoadScheduleAsync(int id) =>
        _db.ScheduledPayments.Include(s => s.Payee).Include(s => s.ToAccount).FirstOrDefaultAsync(s => s.Id == id);

    // Record the attempt on the item and move NextRunDate to the next occurrence (or finish the item).
    private static void Finish(ScheduledPayment s, DateOnly due, ScheduledRunStatus status, string? reason)
    {
        s.LastRunAt = DateTime.UtcNow;
        s.LastRunStatus = status;
        s.LastFailureReason = reason;
        Advance(s, due);
    }

    private static void Advance(ScheduledPayment s, DateOnly due)
    {
        s.NextRunDate = Recurrence.After(s, due);
        if (s.NextRunDate == null && s.Status == ScheduleStatus.Active) s.Status = ScheduleStatus.Completed;
    }
}

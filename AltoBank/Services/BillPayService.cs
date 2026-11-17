using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

public record PayeeData(string Name, PayeeCategory Category, string AccountNumber, string? Nickname);

/// <summary>A new scheduled item. When: "once" (on Date), "weekly" (DayOfWeek), "monthly" (DayOfMonth), each starting on Date.</summary>
public record ScheduleData(ScheduleKind Kind, int FromAccountId, int? PayeeId, int? ToAccountId, decimal Amount, string? Memo,
    ScheduleFrequency Frequency, DateOnly StartDate, DayOfWeek? DayOfWeek, int? DayOfMonth, DateOnly? EndDate);

/// <summary>What can change on an existing item (to change how often it repeats, cancel it and create a new one).</summary>
public record ScheduleEdit(int FromAccountId, decimal Amount, string? Memo, DateOnly NextRunDate, DateOnly? EndDate);

/// <summary>One upcoming payment/transfer on a date (calendar + list).</summary>
public record UpcomingItem(DateOnly Date, ScheduledPayment Item, bool WillBeSkipped);

/// <summary>
/// BILL PAY + SCHEDULED TRANSFERS: everything that does NOT move money (payees, creating/editing/skipping/
/// cancelling schedules, the calendar). Money moves only in IBankService (PayBillAsync / ExecuteScheduledAsync).
/// Every method takes the signed-in user's id; another customer's payee or schedule is simply "not found".
/// Nothing is deleted: payees are Removed, schedules Cancelled.
/// </summary>
public interface IBillPayService
{
    Task<List<Payee>> GetPayeesAsync(string userId);
    Task<Payee?> GetPayeeAsync(string userId, int payeeId);
    Task<(BankResult Result, Payee? Payee)> AddPayeeAsync(string userId, PayeeData data);
    Task<BankResult> UpdatePayeeAsync(string userId, int payeeId, PayeeData data);
    Task<BankResult> RemovePayeeAsync(string userId, int payeeId);

    Task<List<ScheduledPayment>> GetSchedulesAsync(string userId);
    Task<ScheduledPayment?> GetScheduleAsync(string userId, int id);
    Task<(BankResult Result, ScheduledPayment? Item)> CreateScheduleAsync(string userId, ScheduleData data);
    Task<BankResult> UpdateScheduleAsync(string userId, int id, ScheduleEdit edit);
    Task<BankResult> SetSkipNextAsync(string userId, int id, bool skip);
    Task<BankResult> CancelScheduleAsync(string userId, int id);

    Task<List<UpcomingItem>> GetUpcomingAsync(string userId, DateOnly from, DateOnly to);
    Task<List<ScheduledRun>> GetRecentRunsAsync(string userId, int take = 20);
}

public class BillPayService : IBillPayService
{
    // A credit card has its own Pay card page and autopay, so schedules use checking or savings only.
    private const string NotForSchedules = "Scheduled payments and transfers use checking or savings. To pay your Alto Peak card automatically, turn on autopay on the card.";

    private readonly ApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public BillPayService(ApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    // ===================== Payees =====================

    public Task<List<Payee>> GetPayeesAsync(string userId) =>
        _db.Payees.AsNoTracking().Where(p => p.UserId == userId && p.Status == PayeeStatus.Active)
            .OrderBy(p => p.Name).ToListAsync();

    public Task<Payee?> GetPayeeAsync(string userId, int payeeId) =>
        _db.Payees.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payeeId && p.UserId == userId && p.Status == PayeeStatus.Active);

    public async Task<(BankResult Result, Payee? Payee)> AddPayeeAsync(string userId, PayeeData data)
    {
        if (Validate(data) is { } bad) return (bad, null);
        var payee = new Payee
        {
            UserId = userId, Name = data.Name.Trim(), Category = data.Category, AccountNumber = data.AccountNumber.Trim(),
            Nickname = Blank(data.Nickname), Status = PayeeStatus.Active, CreatedAt = DateTime.UtcNow
        };
        _db.Payees.Add(payee);
        await _db.SaveChangesAsync();
        await _audit.WriteAsync("Payee.Add", $"Added payee {payee.Name} (••{payee.Last4})", "Payee", payee.Id.ToString(),
                                after: new { payee.Name, category = payee.Category.ToString(), payee.Nickname });
        return (new BankResult(true, $"{payee.DisplayName} was added to your payees."), payee);
    }

    public async Task<BankResult> UpdatePayeeAsync(string userId, int payeeId, PayeeData data)
    {
        if (Validate(data) is { } bad) return bad;
        var payee = await _db.Payees.FirstOrDefaultAsync(p => p.Id == payeeId && p.UserId == userId && p.Status == PayeeStatus.Active);
        if (payee == null) return Fail("Payee not found.");
        var before = new { payee.Name, category = payee.Category.ToString(), payee.Nickname, last4 = payee.Last4 };
        payee.Name = data.Name.Trim();
        payee.Category = data.Category;
        payee.AccountNumber = data.AccountNumber.Trim();
        payee.Nickname = Blank(data.Nickname);
        _audit.Add("Payee.Update", $"Updated payee {payee.Name}", "Payee", payee.Id.ToString(), before,
                   new { payee.Name, category = payee.Category.ToString(), payee.Nickname, last4 = payee.Last4 });
        await _db.SaveChangesAsync();
        return new BankResult(true, $"{payee.DisplayName} was updated.");
    }

    /// <summary>Soft delete: the payee is marked Removed (history keeps pointing at it) and its active schedules are cancelled.</summary>
    public async Task<BankResult> RemovePayeeAsync(string userId, int payeeId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var payee = await _db.Payees.FirstOrDefaultAsync(p => p.Id == payeeId && p.UserId == userId && p.Status == PayeeStatus.Active);
        if (payee == null) return Fail("Payee not found.");
        payee.Status = PayeeStatus.Removed;
        payee.RemovedAt = DateTime.UtcNow;
        var schedules = await _db.ScheduledPayments.Where(s => s.PayeeId == payee.Id && s.Status == ScheduleStatus.Active).ToListAsync();
        foreach (var s in schedules) { s.Status = ScheduleStatus.Cancelled; s.CancelledAt = DateTime.UtcNow; s.NextRunDate = null; }
        _audit.Add("Payee.Remove", $"Removed payee {payee.Name}" + (schedules.Count > 0 ? $" and cancelled {schedules.Count} scheduled payment(s)" : ""),
                   "Payee", payee.Id.ToString(), new { status = "Active" }, new { status = "Removed", cancelledSchedules = schedules.Select(s => s.Id) });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return new BankResult(true, $"{payee.DisplayName} was removed" + (schedules.Count > 0 ? $" and {schedules.Count} scheduled payment(s) cancelled." : "."));
    }

    private static BankResult? Validate(PayeeData d)
    {
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Trim().Length > 60) return Fail("Enter the payee's name (up to 60 characters).");
        var acct = d.AccountNumber?.Trim() ?? "";
        if (acct.Length is < 4 or > 30 || !acct.All(c => char.IsLetterOrDigit(c) || c == '-'))
            return Fail("Enter your account number at the payee (4-30 letters, digits or dashes).");
        if (d.Nickname?.Trim().Length > 30) return Fail("Nickname can be at most 30 characters.");
        if (!Enum.IsDefined(d.Category)) return Fail("Choose a category.");
        return null;
    }

    // ===================== Schedules =====================

    private IQueryable<ScheduledPayment> Schedules(string userId) =>
        _db.ScheduledPayments.Include(s => s.Payee).Include(s => s.FromAccount).Include(s => s.ToAccount)
            .Where(s => s.UserId == userId);

    public Task<List<ScheduledPayment>> GetSchedulesAsync(string userId) =>
        Schedules(userId).AsNoTracking()
            .OrderBy(s => s.Status != ScheduleStatus.Active).ThenBy(s => s.NextRunDate).ThenByDescending(s => s.CreatedAt)
            .ToListAsync();

    public Task<ScheduledPayment?> GetScheduleAsync(string userId, int id) =>
        Schedules(userId).AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

    public async Task<(BankResult Result, ScheduledPayment? Item)> CreateScheduleAsync(string userId, ScheduleData d)
    {
        if (BankService.CheckAmount(d.Amount) is { } badAmount) return (badAmount, null);
        var memo = Blank(d.Memo);
        if (memo?.Length > 80) return (Fail("Memo can be at most 80 characters."), null);
        var today = BankTime.Today;
        if (d.StartDate < today) return (Fail("The first date can't be in the past."), null);
        if (d.StartDate > today.AddYears(1)) return (Fail("Schedule payments up to a year ahead."), null);
        if (d.Frequency == ScheduleFrequency.Weekly && d.DayOfWeek == null) return (Fail("Choose the day of the week."), null);
        if (d.Frequency == ScheduleFrequency.Monthly && d.DayOfMonth is not (>= 1 and <= 31)) return (Fail("Choose a day of the month (1-31)."), null);

        var from = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == d.FromAccountId && a.UserId == userId);
        if (from == null) return (Fail("Account not found."), null);
        if (from.Status == AccountStatus.Closed) return (Fail($"{from.DisplayName} is closed."), null);
        if (!from.IsDepositAccount) return (Fail(NotForSchedules), null);

        Payee? payee = null; Account? to = null;
        if (d.Kind == ScheduleKind.BillPay)
        {
            payee = await _db.Payees.FirstOrDefaultAsync(p => p.Id == d.PayeeId && p.UserId == userId && p.Status == PayeeStatus.Active);
            if (payee == null) return (Fail("Payee not found."), null);
        }
        else
        {
            to = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == d.ToAccountId && a.UserId == userId);
            if (to == null) return (Fail("Account not found."), null);
            if (to.Id == from.Id) return (Fail("Choose two different accounts."), null);
            if (to.Status == AccountStatus.Closed) return (Fail($"{to.DisplayName} is closed."), null);
            if (!to.IsDepositAccount) return (Fail(NotForSchedules), null);
        }

        var first = Recurrence.First(d.Frequency, d.StartDate, d.DayOfWeek, d.DayOfMonth);
        if (d.EndDate is DateOnly end && end < first) return (Fail("The end date is before the first payment."), null);

        var item = new ScheduledPayment
        {
            UserId = userId, Kind = d.Kind, FromAccountId = from.Id, PayeeId = payee?.Id, ToAccountId = to?.Id,
            Amount = d.Amount, Memo = memo, Frequency = d.Frequency,
            DayOfWeek = d.Frequency == ScheduleFrequency.Weekly ? d.DayOfWeek : null,
            DayOfMonth = d.Frequency == ScheduleFrequency.Monthly ? d.DayOfMonth : null,
            StartDate = d.StartDate, EndDate = d.Frequency == ScheduleFrequency.Once ? null : d.EndDate,
            NextRunDate = first, Status = ScheduleStatus.Active, CreatedAt = DateTime.UtcNow,
            Payee = payee, ToAccount = to, FromAccount = from
        };
        _db.ScheduledPayments.Add(item);
        await _db.SaveChangesAsync();
        await _audit.WriteAsync("Schedule.Create", $"Scheduled {d.Kind} of {d.Amount:C} to {item.Target}: {item.ScheduleText}", "ScheduledPayment", item.Id.ToString(),
                                after: new { kind = d.Kind.ToString(), d.Amount, frequency = d.Frequency.ToString(), first, d.EndDate, from = from.Id, payee = payee?.Id, to = to?.Id });
        var when = first == today ? "today (within a minute)" : first.ToString("dddd, MMM d");
        return (new BankResult(true, $"Scheduled {d.Amount:C} to {item.Target}: {item.ScheduleText.ToLowerInvariant()}. First one: {when}."), item);
    }

    public async Task<BankResult> UpdateScheduleAsync(string userId, int id, ScheduleEdit e)
    {
        if (BankService.CheckAmount(e.Amount) is { } bad) return bad;
        var memo = Blank(e.Memo);
        if (memo?.Length > 80) return Fail("Memo can be at most 80 characters.");
        var s = await Schedules(userId).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return Fail("Scheduled payment not found.");
        if (s.Status != ScheduleStatus.Active) return Fail($"This item is {s.Status.ToString().ToLowerInvariant()} and can't be changed.");
        if (e.NextRunDate < BankTime.Today) return Fail("The next date can't be in the past.");
        if (e.NextRunDate > BankTime.Today.AddYears(1)) return Fail("Schedule payments up to a year ahead.");
        // The next date has to match the repeat pattern, or the schedule would quietly drift to a new day.
        if (s.Frequency == ScheduleFrequency.Weekly && s.DayOfWeek is DayOfWeek dow && e.NextRunDate.DayOfWeek != dow)
            return Fail($"This repeats every {dow}, so the next date must be a {dow}. To change the day, cancel it and schedule a new one.");
        if (s.Frequency == ScheduleFrequency.Monthly && s.DayOfMonth is int dom
            && e.NextRunDate != Recurrence.MonthDay(e.NextRunDate.Year, e.NextRunDate.Month, dom))
            return Fail($"This repeats on {s.ScheduleText.ToLowerInvariant()}, so the next date must be that day of a month. To change the day, cancel it and schedule a new one.");
        // Each date is processed once (that's what stops double payments), so a date that already ran can't be reused.
        if (await _db.ScheduledRuns.AnyAsync(r => r.ScheduledPaymentId == s.Id && r.RunDate == e.NextRunDate))
            return Fail($"This was already processed for {e.NextRunDate:MMM d}. Choose a later date, or use Pay now for a one-off payment.");
        if (e.EndDate is DateOnly end && end < e.NextRunDate) return Fail("The end date is before the next payment.");
        var from = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == e.FromAccountId && a.UserId == userId);
        if (from == null || from.Status == AccountStatus.Closed) return Fail("Account not found.");
        if (!from.IsDepositAccount) return Fail(NotForSchedules);
        if (s.Kind == ScheduleKind.Transfer && from.Id == s.ToAccountId) return Fail("Choose two different accounts.");

        var before = new { s.Amount, s.Memo, s.NextRunDate, s.EndDate, from = s.FromAccountId };
        s.Amount = e.Amount; s.Memo = memo; s.NextRunDate = e.NextRunDate; s.FromAccountId = from.Id;
        if (s.IsRecurring) s.EndDate = e.EndDate;
        else s.StartDate = e.NextRunDate;
        _audit.Add("Schedule.Update", $"Updated scheduled {s.Kind} #{s.Id} to {s.Target}", "ScheduledPayment", s.Id.ToString(),
                   before, new { s.Amount, s.Memo, s.NextRunDate, s.EndDate, from = s.FromAccountId });
        await _db.SaveChangesAsync();
        return new BankResult(true, $"Updated. Next: {s.Amount:C} on {e.NextRunDate:ddd, MMM d}.");
    }

    public async Task<BankResult> SetSkipNextAsync(string userId, int id, bool skip)
    {
        var s = await Schedules(userId).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return Fail("Scheduled payment not found.");
        if (s.Status != ScheduleStatus.Active || s.NextRunDate == null) return Fail("Only active items can be skipped.");
        if (!s.IsRecurring && skip) return Fail("This is a one-time payment. Cancel it instead.");
        s.SkipNext = skip;
        _audit.Add(skip ? "Schedule.SkipNext" : "Schedule.UndoSkip", $"{(skip ? "Skip" : "Un-skip")} {s.NextRunDate:yyyy-MM-dd} for #{s.Id}", "ScheduledPayment", s.Id.ToString(),
                   new { skipNext = !skip }, new { skipNext = skip });
        await _db.SaveChangesAsync();
        return new BankResult(true, skip ? $"The {s.NextRunDate:MMM d} payment to {s.Target} will be skipped." : $"The {s.NextRunDate:MMM d} payment is back on.");
    }

    public async Task<BankResult> CancelScheduleAsync(string userId, int id)
    {
        var s = await Schedules(userId).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return Fail("Scheduled payment not found.");
        if (s.Status != ScheduleStatus.Active) return Fail($"This item is already {s.Status.ToString().ToLowerInvariant()}.");
        s.Status = ScheduleStatus.Cancelled;
        s.CancelledAt = DateTime.UtcNow;
        s.NextRunDate = null;
        _audit.Add("Schedule.Cancel", $"Cancelled scheduled {s.Kind} #{s.Id} to {s.Target}", "ScheduledPayment", s.Id.ToString(),
                   new { status = "Active" }, new { status = "Cancelled" });
        await _db.SaveChangesAsync();
        return new BankResult(true, $"Cancelled the {s.ScheduleText.ToLowerInvariant()} payment to {s.Target}.");
    }

    public async Task<List<UpcomingItem>> GetUpcomingAsync(string userId, DateOnly from, DateOnly to)
    {
        var active = await Schedules(userId).AsNoTracking().Where(s => s.Status == ScheduleStatus.Active && s.NextRunDate != null).ToListAsync();
        return active
            .SelectMany(s => Recurrence.Between(s, from, to).Select(d => new UpcomingItem(d, s, s.SkipNext && d == s.NextRunDate)))
            .OrderBy(u => u.Date).ThenBy(u => u.Item.Id)
            .ToList();
    }

    public Task<List<ScheduledRun>> GetRecentRunsAsync(string userId, int take = 20) =>
        _db.ScheduledRuns.AsNoTracking()
            .Include(r => r.ScheduledPayment!).ThenInclude(s => s.Payee)
            .Include(r => r.ScheduledPayment!).ThenInclude(s => s.ToAccount)
            .Where(r => r.ScheduledPayment!.UserId == userId)
            .OrderByDescending(r => r.ProcessedAt).Take(take).ToListAsync();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static BankResult Fail(string m) => new(false, m);
}

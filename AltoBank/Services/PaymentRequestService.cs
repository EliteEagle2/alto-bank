using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>One person in a request: who (email or phone) and how much.</summary>
public record RequestLine(string? To, decimal Amount);

/// <summary>
/// A new request. With SplitTotal + SplitEvenly the amounts are worked out here (not trusted from the browser):
/// the total is divided by the number of people (+1 when IncludeMe, because the requester pays a share too).
/// </summary>
public record NewRequest(List<RequestLine> Lines, string? Memo, decimal? SplitTotal, bool SplitEvenly, bool IncludeMe);

/// <summary>Everything the Send &amp; Request page lists for one customer.</summary>
public record PaymentsOverview(
    List<MoneyRequest> Inbox,          // asked of me, still open
    List<MoneyRequest> MyRequests,     // I asked others (any status)
    List<MoneyRequest> ClosedInbox,    // asked of me, already paid/declined/cancelled/expired
    List<Payment> Sent,
    List<Payment> Received);

/// <summary>
/// SEND &amp; REQUEST: the parts that DON'T move money: creating, declining, cancelling and expiring requests,
/// and listing activity. (Paying a request moves money, so it's IBankService.PayRequestAsync.)
/// Every method takes the signed-in user's id and only touches requests that user is part of.
/// </summary>
public interface IPaymentRequestService
{
    Task<(BankResult Result, List<MoneyRequest> Created)> CreateAsync(string requesterId, NewRequest input);
    Task<BankResult> DeclineAsync(string payerId, int requestId);
    Task<BankResult> CancelAsync(string requesterId, int requestId);
    Task<MoneyRequest?> GetForUserAsync(string userId, int requestId);
    Task<PaymentsOverview> GetOverviewAsync(string userId);
    /// <summary>Marks every Pending request past its ExpiresAt as Expired and alerts the requester. Returns how many.</summary>
    Task<int> ExpireDueAsync();
}

public class PaymentRequestService : IPaymentRequestService
{
    public const int MaxPeople = 5;

    /// <summary>Works out an even split in cents (any leftover cent goes to the requester, or to the first people).</summary>
    public static List<decimal> SplitEvenly(decimal total, int people, bool includeMe)
    {
        var shares = people + (includeMe ? 1 : 0);
        var cents = (long)Math.Round(total * 100m);
        var each = cents / shares;
        var leftover = cents - each * shares;
        return Enumerable.Range(0, people)
            .Select(i => (each + (!includeMe && i < leftover ? 1 : 0)) / 100m)
            .ToList();
    }

    private readonly ApplicationDbContext _db;
    private readonly IBankService _bank;
    private readonly IAlertService _alerts;
    private readonly IAuditLogger _audit;

    public PaymentRequestService(ApplicationDbContext db, IBankService bank, IAlertService alerts, IAuditLogger audit)
    {
        _db = db;
        _bank = bank;
        _alerts = alerts;
        _audit = audit;
    }

    public async Task<(BankResult Result, List<MoneyRequest> Created)> CreateAsync(string requesterId, NewRequest input)
    {
        var lines = input.Lines.Where(l => !string.IsNullOrWhiteSpace(l.To)).ToList();
        if (lines.Count == 0) return (Fail("Add at least one person to request money from."), new());
        if (lines.Count > MaxPeople) return (Fail($"You can request from at most {MaxPeople} people at a time."), new());
        var memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
        if (memo?.Length > 80) return (Fail("Memo can be at most 80 characters."), new());

        // The requester needs an active checking account for the money to land in.
        if (!await _db.Accounts.AnyAsync(a => a.UserId == requesterId && a.AccountType == AccountType.Checking && a.Status == AccountStatus.Active))
            return (Fail("You need an active checking account to receive money."), new());

        // Split a bill: amounts are calculated on the server from the total.
        List<decimal> amounts;
        if (input.SplitTotal is decimal total)
        {
            if (BankService.CheckAmount(total) is { } bad) return (bad, new());
            if (input.SplitEvenly)
                amounts = SplitEvenly(total, lines.Count, input.IncludeMe);
            else
            {
                amounts = lines.Select(l => l.Amount).ToList();
                if (amounts.Sum() > total) return (Fail($"The amounts add up to {amounts.Sum():C}, which is more than the {total:C} bill."), new());
            }
        }
        else amounts = lines.Select(l => l.Amount).ToList();

        // Resolve every person first, so one bad entry means nothing is created.
        var people = new List<(RecipientLookup Who, decimal Amount)>();
        for (int i = 0; i < lines.Count; i++)
        {
            var who = await _bank.FindRecipientAsync(requesterId, lines[i].To);
            if (!who.Found) return (Fail($"{lines[i].To!.Trim()}: {who.Error}"), new());
            if (people.Any(p => p.Who.UserId == who.UserId)) return (Fail($"{who.ShortName} is on the list twice."), new());
            if (BankService.CheckAmount(amounts[i]) is { } bad) return (Fail($"{who.ShortName}: {bad.Message}"), new());
            people.Add((who, amounts[i]));
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        var requester = await _db.Users.FirstAsync(u => u.Id == requesterId);
        var group = people.Count > 1 || input.SplitTotal != null ? Guid.NewGuid().ToString() : null;
        var now = DateTime.UtcNow;
        var created = people.Select(p => new MoneyRequest
        {
            RequesterUserId = requesterId, PayerUserId = p.Who.UserId!, Amount = p.Amount, Memo = memo,
            Status = MoneyRequestStatus.Pending, CreatedAt = now, ExpiresAt = now.AddDays(MoneyRequest.ExpiryDays),
            SplitGroupId = group, SplitTotal = input.SplitTotal
        }).ToList();
        _db.MoneyRequests.AddRange(created);
        await _db.SaveChangesAsync();

        foreach (var r in created)
            await _alerts.AddAsync(r.PayerUserId, AlertType.RequestReceived, "Money request",
                $"{BankService.ShortName(requester)} requested {r.Amount:C}" + (memo == null ? "." : $" for {memo}.") +
                $" It expires in {MoneyRequest.ExpiryDays} days.", "/payments#inbox");
        _audit.Add("Request.Create", $"Requested {created.Sum(r => r.Amount):C} from {created.Count} " + (created.Count == 1 ? "person" : "people"),
                   "MoneyRequest", string.Join(",", created.Select(r => r.Id)),
                   after: created.Select(r => new { r.Id, payer = r.PayerUserId, r.Amount, r.SplitTotal }));
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var names = string.Join(", ", people.Select(p => $"{p.Who.ShortName} ({p.Amount:C})"));
        return (new BankResult(true, $"Request sent to {names}."), created);
    }

    public Task<BankResult> DeclineAsync(string payerId, int requestId) =>
        CloseAsync(requestId, r => r.PayerUserId == payerId, MoneyRequestStatus.Declined, async r =>
        {
            var payer = await _db.Users.FirstAsync(u => u.Id == payerId);
            await _alerts.AddAsync(r.RequesterUserId, AlertType.RequestDeclined, "Request declined",
                $"{BankService.ShortName(payer)} declined your request for {r.Amount:C}" + (r.Memo == null ? "." : $" ({r.Memo})."), "/payments#requests");
            return "Request declined.";
        });

    public Task<BankResult> CancelAsync(string requesterId, int requestId) =>
        CloseAsync(requestId, r => r.RequesterUserId == requesterId, MoneyRequestStatus.Cancelled, async r =>
        {
            var requester = await _db.Users.FirstAsync(u => u.Id == requesterId);
            await _alerts.AddAsync(r.PayerUserId, AlertType.RequestCancelled, "Request cancelled",
                $"{BankService.ShortName(requester)} cancelled their request for {r.Amount:C}. You don't need to pay it.", "/payments#inbox");
            return "Request cancelled.";
        });

    // Decline / cancel: only for the right person, only while Pending and not expired. Audited; alert in the same transaction.
    private async Task<BankResult> CloseAsync(int requestId, Func<MoneyRequest, bool> allowed, MoneyRequestStatus newStatus, Func<MoneyRequest, Task<string>> notify)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var r = await _db.MoneyRequests.FirstOrDefaultAsync(x => x.Id == requestId);
        if (r == null || !allowed(r)) return Fail("Request not found.");
        if (r.Status != MoneyRequestStatus.Pending) return Fail($"This request is already {r.Status.ToString().ToLowerInvariant()}.");
        if (r.ExpiresAt <= DateTime.UtcNow) return Fail("This request has expired.");

        r.Status = newStatus;
        r.RespondedAt = DateTime.UtcNow;
        var message = await notify(r);
        _audit.Add($"Request.{newStatus}", $"{newStatus} request #{r.Id} ({r.Amount:C})", "MoneyRequest", r.Id.ToString(),
                   new { status = "Pending" }, new { status = newStatus.ToString() });
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            return Fail("This request changed while we were processing it. Please reload.");
        }
        return new BankResult(true, message);
    }

    public Task<MoneyRequest?> GetForUserAsync(string userId, int requestId) =>
        _db.MoneyRequests.AsNoTracking().Include(r => r.Requester).Include(r => r.Payer)
            .FirstOrDefaultAsync(r => r.Id == requestId && (r.PayerUserId == userId || r.RequesterUserId == userId));

    public async Task<PaymentsOverview> GetOverviewAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var asked = await _db.MoneyRequests.AsNoTracking().Include(r => r.Requester)
            .Where(r => r.PayerUserId == userId).OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
        var mine = await _db.MoneyRequests.AsNoTracking().Include(r => r.Payer)
            .Where(r => r.RequesterUserId == userId).OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
        var sent = await _db.Payments.AsNoTracking().Include(p => p.Recipient).Include(p => p.FromAccount)
            .Where(p => p.SenderUserId == userId).OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync();
        var received = await _db.Payments.AsNoTracking().Include(p => p.Sender).Include(p => p.ToAccount)
            .Where(p => p.RecipientUserId == userId).OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync();
        return new PaymentsOverview(
            Inbox: asked.Where(r => r.Status == MoneyRequestStatus.Pending && r.ExpiresAt > now).ToList(),
            MyRequests: mine,
            ClosedInbox: asked.Where(r => !(r.Status == MoneyRequestStatus.Pending && r.ExpiresAt > now)).ToList(),
            Sent: sent, Received: received);
    }

    public async Task<int> ExpireDueAsync()
    {
        var now = DateTime.UtcNow;
        var due = await _db.MoneyRequests.Include(r => r.Payer)
            .Where(r => r.Status == MoneyRequestStatus.Pending && r.ExpiresAt <= now).ToListAsync();
        if (due.Count == 0) return 0;

        await using var tx = await _db.Database.BeginTransactionAsync();
        foreach (var r in due)
        {
            r.Status = MoneyRequestStatus.Expired;
            r.RespondedAt = now;
            await _alerts.AddAsync(r.RequesterUserId, AlertType.RequestExpired, "Request expired",
                $"Your request to {BankService.ShortName(r.Payer!)} for {r.Amount:C} expired after {MoneyRequest.ExpiryDays} days without a reply.",
                "/payments#requests", dedupeKey: $"request-expired:{r.Id}");
            _audit.Add("Request.Expired", $"Request #{r.Id} ({r.Amount:C}) expired", "MoneyRequest", r.Id.ToString(),
                       new { status = "Pending" }, new { status = "Expired" }, actor: new AuditActor(null, null, "System"));
        }
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone paid/declined one of them at the same moment; leave it for the next run.
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            return 0;
        }
        return due.Count;
    }

    private static BankResult Fail(string message) => new(false, message);
}

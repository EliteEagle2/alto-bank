using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>
/// SEND &amp; REQUEST: the money side. (Creating, declining and cancelling requests moves no money, so that
/// lives in PaymentRequestService; PAYING a request moves money, so it's here.)
/// Same rules as every other money movement in BankService:
///   • the sender's account must belong to the sender, be Active (not frozen/closed), stay within its
///     daily transfer limit (payments count toward it), and have enough money;
///   • the recipient must be an active Alto customer; the money lands in their PRIMARY checking
///     (their oldest Active checking account);
///   • one DB transaction for both sides, with the Balance concurrency check, so racing payments can't overdraw.
/// </summary>
public partial class BankService
{
    public async Task<RecipientLookup> FindRecipientAsync(string senderUserId, string? query)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0) return RecipientLookup.Fail("Enter the recipient's email or mobile number.");

        ApplicationUser? user;
        if (q.Contains('@'))
        {
            var normalized = q.ToUpperInvariant();
            user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);
        }
        else
        {
            var digits = PhoneDigits(q);
            if (digits.Length != 10) return RecipientLookup.Fail("Enter a 10-digit mobile number or an email address.");
            // Phone numbers are stored formatted ("(215) 555-0187"), so compare the digits in memory (few customers).
            var withPhones = await _db.Users.AsNoTracking().Where(u => u.PhoneNumber != null).ToListAsync();
            // Profile → Preferences: people who turned off "find me by phone" only match by email.
            var hidden = await _db.UserPreferences.Where(p => !p.DiscoverableByPhone).Select(p => p.UserId).ToListAsync();
            var matches = withPhones.Where(u => PhoneDigits(u.PhoneNumber!) == digits && !hidden.Contains(u.Id)).ToList();
            // Phone numbers aren't unique. Never guess: if two people share one, ask for an email instead.
            if (matches.Count > 1) return RecipientLookup.Fail("More than one person uses that number. Please use their email address instead.");
            user = matches.FirstOrDefault();
        }

        if (user != null && user.Id == senderUserId)
            return RecipientLookup.Fail("You can't send money to yourself. Use Transfer to move money between your own accounts.");
        // One message for "no such customer" AND "can't receive right now" (locked, no open checking), so this
        // lookup can't be used to discover who banks here or whose login is locked.
        if (user == null || !await IsCustomerAsync(user.Id) || user.LockoutEnd > DateTimeOffset.UtcNow || !await PrimaryCheckingQuery(user.Id).AnyAsync())
            return RecipientLookup.Fail(NotFoundMessage);
        return new RecipientLookup(user.Id, ShortName(user), null);
    }

    public Task<(BankResult Result, Payment? Payment)> SendPaymentAsync(string userId, int fromAccountId, string? recipient, decimal amount, string? memo)
    {
        if (CheckAmount(amount) is { } invalid) return Task.FromResult<(BankResult, Payment?)>((invalid, null));
        memo = Clean(memo);
        if (memo?.Length > 80) return Task.FromResult<(BankResult, Payment?)>((new(false, "Memo can be at most 80 characters."), null));

        Payment? made = null;
        return WrapAsync(InTransactionAsync(async () =>
        {
            var to = await FindRecipientAsync(userId, recipient);
            if (!to.Found) return new BankResult(false, to.Error!);
            var (result, payment) = await SendCoreAsync(userId, fromAccountId, to.UserId!, amount, memo, request: null);
            made = payment;
            return result;
        }), () => made);
    }

    public Task<(BankResult Result, Payment? Payment)> PayRequestAsync(string userId, int requestId, int fromAccountId)
    {
        Payment? made = null;
        return WrapAsync(InTransactionAsync(async () =>
        {
            // Only the person who was asked can pay (anyone else gets "not found").
            var request = await _db.MoneyRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.PayerUserId == userId);
            if (request == null) return new BankResult(false, "Request not found.");
            if (request.Status != MoneyRequestStatus.Pending) return new BankResult(false, $"This request is already {request.Status.ToString().ToLowerInvariant()}.");
            if (request.ExpiresAt <= DateTime.UtcNow) return new BankResult(false, "This request has expired.");

            var (result, payment) = await SendCoreAsync(userId, fromAccountId, request.RequesterUserId, request.Amount, request.Memo, request);
            if (!result.Success || payment == null) return result;

            request.Status = MoneyRequestStatus.Paid;            // [ConcurrencyCheck]: paying twice at once fails
            request.RespondedAt = DateTime.UtcNow;
            request.PaymentId = payment.Id;
            payment.MoneyRequestId = request.Id;
            var payer = await _db.Users.FirstAsync(u => u.Id == userId);
            await _alerts.AddAsync(request.RequesterUserId, AlertType.RequestPaid, "Request paid",
                $"{ShortName(payer)} paid your request for {request.Amount:C}" + (request.Memo == null ? "." : $" ({request.Memo})."),
                "/payments#requests");
            _audit.Add("Request.Pay", $"Paid request #{request.Id} for {request.Amount:C}", "MoneyRequest", request.Id.ToString(),
                       new { status = "Pending" }, new { status = "Paid", paymentId = payment.Id });
            await _db.SaveChangesAsync();
            made = payment;
            return new BankResult(true, $"Paid {request.Amount:C} to {ShortName(payment.Recipient!)}.");
        }), () => made);
    }

    public const string NotFoundMessage = "We couldn't find an Alto customer who can receive payments at that email or phone number.";

    /// <summary>
    /// The recipient's primary checking: their oldest open checking account, preferring Active ones.
    /// A Frozen account can still RECEIVE money (same rule as CanReceive: only Closed accounts can't).
    /// </summary>
    private IQueryable<Account> PrimaryCheckingQuery(string userId) =>
        _db.Accounts.Where(a => a.UserId == userId && a.AccountType == AccountType.Checking && a.Status != AccountStatus.Closed)
                    .OrderBy(a => a.Status != AccountStatus.Active).ThenBy(a => a.OpenedAt).ThenBy(a => a.Id);

    /// <summary>Moves the money for a payment. Must run inside InTransactionAsync.</summary>
    private async Task<(BankResult, Payment?)> SendCoreAsync(string senderId, int fromAccountId, string recipientId,
                                                            decimal amount, string? memo, MoneyRequest? request)
    {
        var from = await GetAccountAsync(fromAccountId, senderId);                 // ownership check
        if (from == null) return (new(false, "Account not found."), null);
        if (CanSend(from) is { } blocked) return (blocked, null);                   // frozen / closed
        if (await CheckDailyLimitAsync(from, amount) is { } overLimit) return (overLimit, null);
        if (amount > from.Balance) return (new(false, $"Insufficient funds. {from.DisplayName} has {from.Balance:C} available."), null);

        var sender = await _db.Users.FirstAsync(u => u.Id == senderId);
        var recipient = await _db.Users.FirstAsync(u => u.Id == recipientId);
        var to = await PrimaryCheckingQuery(recipientId).FirstOrDefaultAsync();
        if (to == null || recipient.LockoutEnd > DateTimeOffset.UtcNow)
            return (new(false, NotFoundMessage), null);

        string senderName = ShortName(sender), recipientName = ShortName(recipient);
        from.Balance -= amount;
        to.Balance += amount;
        var note = memo == null ? "" : $" - {memo}";
        var outLeg = AddTransaction(from, TransactionType.PaymentSent, amount, $"Payment to {recipientName}{note}", memo, to.Id);
        var inLeg = AddTransaction(to, TransactionType.PaymentReceived, amount, $"Payment from {senderName}{note}", memo, from.Id);
        outLeg.CounterpartyName = recipientName;   // each side only ever sees the other person's short name
        inLeg.CounterpartyName = senderName;
        await _db.SaveChangesAsync();
        LinkLegs(outLeg, inLeg);

        var payment = new Payment
        {
            SenderUserId = senderId, RecipientUserId = recipientId, FromAccountId = from.Id, ToAccountId = to.Id,
            Amount = amount, Memo = memo, CreatedAt = DateTime.UtcNow, Status = PaymentStatus.Completed,
            SentTransactionId = outLeg.Id, ReceivedTransactionId = inLeg.Id, MoneyRequestId = request?.Id,
            Sender = sender, Recipient = recipient
        };
        _db.Payments.Add(payment);
        await _alerts.AddAsync(recipientId, AlertType.MoneyReceived, "Money received",
            $"{senderName} sent you {amount:C}" + (memo == null ? "." : $" ({memo}).") + $" It's in {to.DisplayName} ••{to.Last4}.",
            $"/accounts/{to.Id}#activity");
        await _db.SaveChangesAsync();

        _audit.Add("Payment.Send", $"Sent {amount:C} from ••{from.Last4} to {recipientName}", "Payment", payment.Id.ToString(),
                   after: new { from = from.Id, to = to.Id, amount, memo, requestId = request?.Id });
        await _db.SaveChangesAsync();
        return (new(true, $"Sent {amount:C} to {recipientName}."), payment);
    }

    /// <summary>Is this user in the Customer role? (Admins can't send or receive payments.)</summary>
    private async Task<bool> IsCustomerAsync(string userId)
    {
        var roleId = await _db.Roles.Where(r => r.Name == SeedData.CustomerRole).Select(r => r.Id).FirstOrDefaultAsync();
        return roleId != null && await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
    }

    /// <summary>"Maya C." — the only part of another customer's name a payment ever shows.</summary>
    public static string ShortName(ApplicationUser u) =>
        string.IsNullOrEmpty(u.LastName) ? u.FirstName : $"{u.FirstName} {char.ToUpperInvariant(u.LastName[0])}.";

    public static string PhoneDigits(string s)
    {
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
    }

    // Runs the transaction, then pairs its result with the entity it produced (null when it failed).
    private static async Task<(BankResult, T?)> WrapAsync<T>(Task<BankResult> run, Func<T?> produced) where T : class
    {
        var result = await run;
        return (result, result.Success ? produced() : null);
    }
}

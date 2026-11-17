using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>What one card cycle step did (for the scheduler's summary).</summary>
public record CardCycleOutcome(int StatementsClosed, int AutopaysMade, int AutopaysFailed, int LateFees);

/// <summary>
/// ALTO PEAK CREDIT CARD: the money side. A card account's Balance is NEGATIVE when money is owed:
///   purchase  = debit  (Balance goes down, owed goes up)        payment / credits = credit (Balance goes up)
/// Every movement runs in InTransactionAsync (one DB transaction, the Balance concurrency check), so two purchases at
/// the same moment can't both use the last of the credit limit.
/// The statement cycle (statement close, interest, autopay, late fee) is RunCardCycleAsync, called by the scheduler.
/// </summary>
public partial class BankService
{
    // ---------------------------------------------------------------- purchases

    public Task<BankResult> CardPurchaseAsync(string userId, int cardAccountId, string? merchant, decimal amount)
    {
        if (CheckAmount(amount) is { } invalid) return Task.FromResult(invalid);
        merchant = Clean(merchant);
        if (merchant == null || merchant.Length < 2) return Task.FromResult(new BankResult(false, "Enter where you're buying (the merchant)."));
        if (merchant.Length > 60) return Task.FromResult(new BankResult(false, "Keep the merchant name under 60 characters."));

        return InTransactionAsync(async () =>
        {
            var card = await GetAccountAsync(cardAccountId, userId);   // ownership check
            if (card is not { IsCreditCard: true }) return new BankResult(false, "Card not found.");
            if (card.Status != AccountStatus.Active)
                return new BankResult(false, $"This card account is {card.Status.ToString().ToLowerInvariant()}, so it can't be used for purchases.");
            var plastic = await _db.Cards.Where(c => c.AccountId == card.Id && c.Status != CardStatus.Cancelled)
                .OrderByDescending(c => c.IssuedAt).FirstOrDefaultAsync();
            if (plastic == null) return new BankResult(false, "This account has no active card. Request a new card on the Card tab.");
            if (plastic.Status == CardStatus.Frozen) return new BankResult(false, $"Your card ••{plastic.Last4} is frozen. Unfreeze it on the Card tab to make purchases.");
            if (amount > card.AvailableCredit)
                return new BankResult(false, $"That's more than your available credit ({card.AvailableCredit:C}).");

            var settings = await _db.GetSettingsAsync();
            card.Balance -= amount;                                   // owed goes up (concurrency token)
            var t = AddTransaction(card, TransactionType.CardPurchase, amount, merchant, memo: $"Card ••{plastic.Last4}");
            var cashBack = CardMath.CashBack(amount, settings.CardCashBackRate);
            card.RewardsBalance += cashBack;
            await _db.SaveChangesAsync();
            _audit.Add("Card.Purchase", $"{merchant}: {amount:C} on ••{card.Last4}", "Account", card.Id.ToString(),
                       after: new { transactionId = t.Id, amount, cashBack, owed = card.AmountOwed });
            await _db.SaveChangesAsync();
            return new BankResult(true, $"{amount:C} at {merchant}. You earned {cashBack:C} cash back. Available credit: {card.AvailableCredit:C}.");
        });
    }

    // ---------------------------------------------------------------- payments

    public Task<BankResult> PayCardAsync(string userId, int cardAccountId, int fromAccountId, CardPaymentOption option, decimal? customAmount) =>
        InTransactionAsync(async () => (await PayCardCoreAsync(userId, cardAccountId, fromAccountId, option, customAmount, autopay: false)));

    private async Task<BankResult> PayCardCoreAsync(string userId, int cardAccountId, int fromAccountId, CardPaymentOption option, decimal? customAmount, bool autopay, CreditCardStatement? forStatement = null)
    {
        var card = await GetAccountAsync(cardAccountId, userId);
        if (card is not { IsCreditCard: true }) return new(false, "Card not found.");
        if (card.Status == AccountStatus.Closed) return new(false, "This card account is closed.");
        var from = await GetAccountAsync(fromAccountId, userId);
        if (from == null) return new(false, "Choose one of your checking or savings accounts.");
        if (CanSend(from) is { } blocked) return blocked;

        // Autopay can pay an older statement after downtime; a customer pays against the latest one.
        var target = forStatement ?? await LatestStatementAsync(card.Id);
        var (minimumLeft, statementLeft) = target == null ? (0m, 0m) : await DueLeftAsync(card, target);
        var owed = card.AmountOwed;
        var amount = option switch
        {
            CardPaymentOption.Minimum => minimumLeft,
            CardPaymentOption.StatementBalance => statementLeft,
            CardPaymentOption.CurrentBalance => owed,
            _ => customAmount ?? 0m
        };
        if (option != CardPaymentOption.Custom && amount <= 0)
            return new(false, option == CardPaymentOption.Minimum ? "Your minimum payment is already paid." : "There's nothing to pay for that option.");
        if (CheckAmount(amount) is { } invalid) return invalid;
        // Can't pay the card below $0 (no overpaying into a credit balance).
        if (amount > owed)
            return new(false, owed == 0 ? "Your card is paid off. There's nothing to pay." : $"That's more than you owe ({owed:C}). You can pay up to {owed:C}.");
        if (await CheckDailyLimitAsync(from, amount) is { } overLimit) return overLimit;
        if (amount > from.Balance) return new(false, $"Insufficient funds. {from.DisplayName} has {from.Balance:C} available.");

        from.Balance -= amount;
        card.Balance += amount;
        var outLeg = AddTransaction(from, TransactionType.TransferOut, amount, $"Payment to {card.DisplayName} ••{card.Last4}", counterpartyId: card.Id);
        var inLeg = AddTransaction(card, TransactionType.CardPayment, amount,
            autopay ? $"Autopay from {from.DisplayName} ••{from.Last4}" : $"Payment from {from.DisplayName} ••{from.Last4}, thank you", counterpartyId: from.Id);
        await _db.SaveChangesAsync();
        LinkLegs(outLeg, inLeg);
        _audit.Add(autopay ? "Card.Autopay" : "Card.Payment", $"Paid {amount:C} to ••{card.Last4} from ••{from.Last4}", "Account", card.Id.ToString(),
                   after: new { amount, option = option.ToString(), from = from.Id, owedAfter = card.AmountOwed });
        await _db.SaveChangesAsync();
        return new(true, $"Paid {amount:C} to {card.DisplayName}. You now owe {card.AmountOwed:C}.");
    }


    // ---------------------------------------------------------------- what's due

    // A statement covers the card's rows DATED in its period (bank days PeriodStart..PeriodEnd, both included), so
    // what's on a statement never depends on when the scheduler happened to run.
    private static decimal OwedDelta(TransactionType type, decimal amount) => Transaction.IsCreditType(type) ? -amount : amount;

    private Task<CreditCardStatement?> LatestStatementAsync(int cardId) =>
        _db.CreditCardStatements.AsNoTracking().Where(x => x.AccountId == cardId)
            .OrderByDescending(x => x.PeriodEnd).FirstOrDefaultAsync();

    /// <summary>
    /// Credits on the card from <paramref name="fromDay"/> (through <paramref name="upToDay"/>, if given): payments, refunds,
    /// fee waivers and statement credits all count toward what's due, minus credits that were taken back again
    /// (a provisional credit clawed back, a reversed payment).
    /// </summary>
    private async Task<decimal> CreditsSinceAsync(int cardId, DateOnly fromDay, DateOnly? upToDay)
    {
        var from = BankTime.StartOfDayUtc(fromDay);
        var q = _db.Transactions.AsNoTracking().Where(t => t.AccountId == cardId && t.Date >= from);
        if (upToDay is DateOnly d) { var end = BankTime.StartOfDayUtc(d.AddDays(1)); q = q.Where(t => t.Date < end); }
        var rows = await q.Select(t => new { t.Type, t.Amount }).ToListAsync();   // summed in C# (SQLite can't SUM decimals)
        var credits = rows.Where(r => Transaction.IsCreditType(r.Type)).Sum(r => r.Amount);
        var takenBack = rows.Where(r => r.Type is TransactionType.ProvisionalClawback or TransactionType.ReversalDebit).Sum(r => r.Amount);
        return Math.Max(0m, credits - takenBack);
    }

    /// <summary>What's left of a statement's minimum and balance: less the credits since it closed, and never more than is owed now.</summary>
    private async Task<(decimal MinimumLeft, decimal StatementLeft)> DueLeftAsync(Account card, CreditCardStatement s)
    {
        var paid = await CreditsSinceAsync(card.Id, s.PeriodEnd.AddDays(1), null);
        var owed = card.AmountOwed;
        return (Math.Min(owed, Math.Max(0m, s.MinimumPayment - paid)), Math.Min(owed, Math.Max(0m, s.StatementBalance - paid)));
    }

    /// <summary>What's due on the latest statement after the credits since it closed.</summary>
    public async Task<(CreditCardStatement? Statement, decimal PaidSinceClose, decimal MinimumLeft, decimal StatementLeft)> GetCardDueAsync(Account card)
    {
        var s = await LatestStatementAsync(card.Id);
        if (s == null) return (null, 0m, 0m, 0m);
        var paid = await CreditsSinceAsync(card.Id, s.PeriodEnd.AddDays(1), null);
        var (minLeft, stmtLeft) = await DueLeftAsync(card, s);
        return (s, paid, minLeft, stmtLeft);
    }

    // ---------------------------------------------------------------- rewards

    public const decimal MinimumRedemption = 10m;

    /// <summary>Redeems ALL cash back: into a checking/savings account (toAccountId), or as a statement credit (null).</summary>
    public Task<BankResult> RedeemCashBackAsync(string userId, int cardAccountId, int? toAccountId) =>
        InTransactionAsync(async () =>
        {
            var card = await GetAccountAsync(cardAccountId, userId);
            if (card is not { IsCreditCard: true }) return new BankResult(false, "Card not found.");
            if (card.Status == AccountStatus.Closed) return new BankResult(false, "This card account is closed.");
            var amount = card.RewardsBalance;
            if (amount < MinimumRedemption)
                return new BankResult(false, $"You can redeem once you have {MinimumRedemption:C} or more. You have {amount:C}.");

            string where;
            if (toAccountId is int toId)
            {
                var to = await GetAccountAsync(toId, userId);
                if (to == null) return new BankResult(false, "Choose one of your checking or savings accounts.");
                if (CanReceive(to) is { } blocked) return blocked;
                card.RewardsBalance = 0m;
                to.Balance += amount;
                AddTransaction(to, TransactionType.RewardRedemption, amount, $"Cash back from {card.DisplayName} ••{card.Last4}");
                where = $"{to.DisplayName} ••{to.Last4}";
            }
            else
            {
                // A statement credit only lowers what's owed; it can't leave the card with money on it.
                if (amount > card.AmountOwed)
                    return new BankResult(false, card.AmountOwed == 0
                        ? "Your card is paid off, so a statement credit has nothing to pay. Redeem to checking or savings instead."
                        : $"Your cash back ({amount:C}) is more than you owe ({card.AmountOwed:C}). Redeem to checking or savings instead.");
                card.RewardsBalance = 0m;
                card.Balance += amount;
                AddTransaction(card, TransactionType.CashBackCredit, amount, "Cash back statement credit");
                where = "your card balance";
            }
            await _db.SaveChangesAsync();
            _audit.Add("Card.RedeemCashBack", $"Redeemed {amount:C} cash back from ••{card.Last4} to {where}", "Account", card.Id.ToString(),
                       after: new { amount, toAccountId });
            await _db.SaveChangesAsync();
            return new BankResult(true, $"{amount:C} cash back added to {where}.");
        });

    // ---------------------------------------------------------------- statement cycle (scheduler)

    /// <summary>
    /// One card's cycle for the bank day <paramref name="today"/>, idempotent:
    ///   1. CLOSE each statement whose close day has ENDED (close date before today), one by one if the scheduler was off:
    ///      interest (only if the previous statement wasn't paid in full by its due date: the grace period), balance,
    ///      minimum payment, due date 25 days later.
    ///   2. AUTOPAY every statement whose due date has arrived and wasn't attempted yet, oldest first.
    ///   3. LATE FEE for each statement whose due date has passed with less than the minimum paid (once each).
    ///   4. A reminder alert 5 days before the due date when the minimum isn't paid and autopay is off.
    /// Each step is its own transaction; a unique (card, close date) index and concurrency flags stop double work.
    /// </summary>
    public async Task<CardCycleOutcome> RunCardCycleAsync(int cardAccountId, DateOnly today)
    {
        int closed = 0, autopays = 0, autopayFailed = 0, lateFees = 0;
        _db.ChangeTracker.Clear();

        // Most ticks have nothing to do: check with plain reads first, so the write lock (BEGIN IMMEDIATE) is only
        // taken when there is work, and customer requests aren't kept waiting by an idle scheduler.
        var next = await _db.Accounts.AsNoTracking().Where(a => a.Id == cardAccountId).Select(a => a.NextStatementDate).FirstOrDefaultAsync();
        var anyDue = await _db.CreditCardStatements.AsNoTracking().AnyAsync(x => x.AccountId == cardAccountId && !x.DueDateProcessed && x.DueDate <= today);
        if (!(next is DateOnly n && n < today) && !anyDue)
        {
            await DueReminderAsync(cardAccountId, today);
            return new CardCycleOutcome(0, 0, 0, 0);
        }

        for (var i = 0; i < 12; i++)
        {
            var r = await InTransactionAsync(async () => await CloseStatementIfDueAsync(cardAccountId, today));
            if (!r.Success) break;
            closed++;
        }

        for (var i = 0; i < 12; i++)
        {
            var autopay = await AutopayIfDueAsync(cardAccountId, today);
            if (autopay == null) break;
            if (autopay == true) autopays++; else autopayFailed++;
        }

        for (var i = 0; i < 12; i++)
        {
            var fee = await InTransactionAsync(async () => await LateFeeIfDueAsync(cardAccountId, today));
            if (!fee.Success) break;
            if (fee.Message == "fee") lateFees++;
        }

        await DueReminderAsync(cardAccountId, today);
        return new CardCycleOutcome(closed, autopays, autopayFailed, lateFees);
    }

    private async Task<BankResult> CloseStatementIfDueAsync(int cardId, DateOnly today)
    {
        var card = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == cardId);
        // The close day has to be over (close < today), so everything dated that day is on this statement.
        if (card is not { IsCreditCard: true } || card.Status == AccountStatus.Closed || card.NextStatementDate is not DateOnly close || close >= today)
            return new BankResult(false, "nothing due");

        var settings = await _db.GetSettingsAsync();
        var prev = await LatestStatementAsync(card.Id);
        var periodStart = prev?.PeriodEnd.AddDays(1) ?? BankTime.DateOf(DateTime.SpecifyKind(card.OpenedAt, DateTimeKind.Utc));
        var startUtc = BankTime.StartOfDayUtc(periodStart);
        var endUtc = BankTime.StartOfDayUtc(close.AddDays(1));

        // Owed at the start of the period = owed now minus everything dated from then on (works however late we run).
        var since = await _db.Transactions.AsNoTracking().Where(t => t.AccountId == card.Id && t.Date >= startUtc)
            .Select(t => new { t.Id, t.Date, t.Type, t.Amount }).ToListAsync();
        var owedAtStart = -card.Balance - since.Sum(t => OwedDelta(t.Type, t.Amount));
        var rows = since.Where(t => t.Date < endUtc).OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();

        // Grace period: no interest if the previous statement was paid in full by its due date (or nothing was owed).
        var paidInFull = prev == null || prev.StatementBalance <= 0
                         || await CreditsSinceAsync(card.Id, prev.PeriodEnd.AddDays(1), prev.DueDate) >= prev.StatementBalance;
        decimal interest = 0m;
        if (!paidInFull)
        {
            // Daily balance method: what was owed at the end of each day of the period.
            var daily = new List<decimal>();
            var running = owedAtStart;
            var i = 0;
            for (var day = periodStart; day <= close; day = day.AddDays(1))
            {
                var dayEnd = BankTime.StartOfDayUtc(day.AddDays(1));
                while (i < rows.Count && rows[i].Date < dayEnd) { running += OwedDelta(rows[i].Type, rows[i].Amount); i++; }
                daily.Add(Math.Max(0m, running));
            }
            interest = CardMath.DailyBalanceInterest(daily, settings.CardPurchaseAprPercent);
        }
        Transaction? interestRow = null;
        if (interest > 0)
        {
            card.Balance -= interest;
            interestRow = AddTransaction(card, TransactionType.InterestCharge, interest, "Interest charged on purchases",
                                         memo: $"{settings.CardPurchaseAprPercent:0.00}% APR, daily balance method");
            interestRow.Date = endUtc.AddSeconds(-1);   // the last moment of the close day: it belongs on this statement
        }
        await _db.SaveChangesAsync();

        var debits = rows.Where(t => !Transaction.IsCreditType(t.Type)).ToList();
        var owedAtClose = owedAtStart + rows.Sum(t => OwedDelta(t.Type, t.Amount)) + interest;
        var balance = Math.Max(0m, owedAtClose);
        var minimum = CardMath.MinimumPayment(balance, settings.CardMinimumPaymentFloor, settings.CardMinimumPaymentPercent);
        var statement = new CreditCardStatement
        {
            AccountId = card.Id, PeriodStart = periodStart, PeriodEnd = close,
            PreviousBalance = Math.Max(0m, owedAtStart),
            Purchases = debits.Where(t => t.Type is not (TransactionType.InterestCharge or TransactionType.LateFee)).Sum(t => t.Amount),
            PaymentsAndCredits = rows.Where(t => Transaction.IsCreditType(t.Type)).Sum(t => t.Amount),
            InterestCharged = interest + debits.Where(t => t.Type == TransactionType.InterestCharge).Sum(t => t.Amount),
            FeesCharged = debits.Where(t => t.Type == TransactionType.LateFee).Sum(t => t.Amount),
            StatementBalance = balance, MinimumPayment = minimum, DueDate = close.AddDays(25),
            AprPercent = settings.CardPurchaseAprPercent,
            LastTransactionId = Math.Max(interestRow?.Id ?? 0, rows.Count == 0 ? prev?.LastTransactionId ?? 0 : rows.Max(t => t.Id)),
            ClosedAt = DateTime.UtcNow
        };
        _db.CreditCardStatements.Add(statement);
        card.NextStatementDate = close.AddMonths(1);   // close days are 1-28, so the day never drifts
        _alerts.Add(card.UserId, AlertType.CardStatement, "Card statement ready",
            balance == 0
                ? $"Your {card.DisplayName} ••{card.Last4} statement closed with nothing owed."
                : $"Your {card.DisplayName} ••{card.Last4} statement balance is {balance:C}. Minimum payment {minimum:C} due {statement.DueDate:MMM d}."
                  + (interest > 0 ? $" Includes {interest:C} interest." : ""),
            $"/accounts/{card.Id}#statements");
        _audit.Add("Card.StatementClose", $"Statement for ••{card.Last4} closed {close:yyyy-MM-dd}: {balance:C}, minimum {minimum:C}, interest {interest:C}",
                   "Account", card.Id.ToString(), after: new { close, balance, minimum, interest, due = statement.DueDate });
        await _db.SaveChangesAsync();
        return new BankResult(true, "closed");
    }

    // true = paid, false = failed, null = nothing (left) to do
    private async Task<bool?> AutopayIfDueAsync(int cardId, DateOnly today)
    {
        _db.ChangeTracker.Clear();
        var card = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cardId);
        if (card is not { IsCreditCard: true } || card.Status == AccountStatus.Closed
            || card.Autopay == CardAutopay.None || card.AutopayFromAccountId is not int fromId) return null;
        // The oldest statement that's due and wasn't tried yet (after downtime there can be more than one).
        var s = await _db.CreditCardStatements.AsNoTracking()
            .Where(x => x.AccountId == cardId && x.AutopayAttemptedAt == null && !x.DueDateProcessed && x.DueDate <= today)
            .OrderBy(x => x.PeriodEnd).FirstOrDefaultAsync();
        if (s == null) return null;

        var option = card.Autopay == CardAutopay.Minimum ? CardPaymentOption.Minimum : CardPaymentOption.StatementBalance;
        var (minLeft, stmtLeft) = await DueLeftAsync(card, s);
        var target = option == CardPaymentOption.Minimum ? minLeft : stmtLeft;
        BankResult result = target <= 0
            ? new BankResult(true, "Nothing left to pay.")
            : await InTransactionAsync(async () => await PayCardCoreAsync(card.UserId, cardId, fromId, option, null, autopay: true, forStatement: s));

        // Record the attempt (its own save: a failed payment was rolled back above). "OK:" = paid, so no late fee.
        _db.ChangeTracker.Clear();
        var tracked = await _db.CreditCardStatements.FirstAsync(x => x.Id == s.Id);
        if (tracked.AutopayAttemptedAt != null) return null;   // another run got here first
        tracked.AutopayAttemptedAt = DateTime.UtcNow;
        var text = (result.Success ? "OK: " : "") + result.Message;
        tracked.AutopayResult = text.Length > 200 ? text[..200] : text;
        if (target > 0)
        {
            if (result.Success)
                _alerts.Add(card.UserId, AlertType.BillPaid, "Card autopay made",
                    $"Autopay paid {target:C} toward your {card.DisplayName} ••{card.Last4}.", $"/accounts/{cardId}");
            else
                _alerts.Add(card.UserId, AlertType.BillFailed, "Card autopay failed",
                    $"Autopay couldn't pay your {card.DisplayName} ••{card.Last4}: {result.Message} Please pay at least {minLeft:C} today to avoid a late fee.",
                    $"/credit-card/{cardId}/pay");
        }
        await _db.SaveChangesAsync();
        return target <= 0 || result.Success;
    }

    private async Task<BankResult> LateFeeIfDueAsync(int cardId, DateOnly today)
    {
        var s = await _db.CreditCardStatements.Where(x => x.AccountId == cardId && !x.DueDateProcessed && x.DueDate < today)
            .OrderBy(x => x.PeriodEnd).FirstOrDefaultAsync();
        if (s == null) return new BankResult(false, "nothing due");
        var card = await _db.Accounts.FirstAsync(a => a.Id == cardId);
        s.DueDateProcessed = true;
        // Everything credited by the end of the due date counts (payments, refunds, waivers), and so does a successful autopay.
        var paidByDue = await CreditsSinceAsync(cardId, s.PeriodEnd.AddDays(1), s.DueDate);
        var autopayPaid = s.AutopayResult?.StartsWith("OK:") == true;
        if (s.MinimumPayment <= 0 || paidByDue >= s.MinimumPayment || autopayPaid || card.Status == AccountStatus.Closed)
        {
            await _db.SaveChangesAsync();
            return new BankResult(true, "on time");
        }
        var settings = await _db.GetSettingsAsync();
        var fee = settings.CardLateFee;
        card.Balance -= fee;   // may go past the limit: a fee isn't a purchase
        var t = AddTransaction(card, TransactionType.LateFee, fee, "Late fee", memo: $"Minimum payment of {s.MinimumPayment:C} was due {s.DueDate:MMM d}");
        await _db.SaveChangesAsync();
        s.LateFeeCharged = true;
        s.LateFeeTransactionId = t.Id;
        _alerts.Add(card.UserId, AlertType.CardLateFee, "Late fee charged",
            $"The {s.MinimumPayment:C} minimum payment on your {card.DisplayName} ••{card.Last4} was due {s.DueDate:MMM d} and wasn't paid in full, so a {fee:C} late fee was added.",
            $"/accounts/{cardId}");
        _audit.Add("Card.LateFee", $"Late fee {fee:C} on ••{card.Last4} (minimum {s.MinimumPayment:C} due {s.DueDate:yyyy-MM-dd}, paid {paidByDue:C})",
                   "Account", card.Id.ToString(), after: new { statementId = s.Id, fee, paidByDue });
        await _db.SaveChangesAsync();
        return new BankResult(true, "fee");
    }

    // Once per statement, 5 days before the due date, if the minimum isn't paid and autopay is off.
    private async Task DueReminderAsync(int cardId, DateOnly today)
    {
        _db.ChangeTracker.Clear();
        var card = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cardId);
        if (card is not { IsCreditCard: true } || card.Status == AccountStatus.Closed || card.Autopay != CardAutopay.None) return;
        var s = await LatestStatementAsync(cardId);
        if (s == null || s.DueDate < today || s.DueDate > today.AddDays(5)) return;
        var (minLeft, _) = await DueLeftAsync(card, s);
        if (minLeft <= 0) return;
        await _alerts.AddAsync(card.UserId, AlertType.CardPaymentDue, "Card payment due soon",
            $"Your {card.DisplayName} ••{card.Last4} minimum payment of {minLeft:C} is due {s.DueDate:MMM d}.",
            $"/credit-card/{cardId}/pay", dedupeKey: $"card-due-{s.Id}");
        await _db.SaveChangesAsync();
    }
}

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

    /// <summary>What's due on the latest statement after the payments made since it closed.</summary>
    public async Task<(CreditCardStatement? Statement, decimal PaidSinceClose, decimal MinimumLeft, decimal StatementLeft)> GetCardDueAsync(Account card)
    {
        var s = await _db.CreditCardStatements.AsNoTracking().Where(x => x.AccountId == card.Id)
            .OrderByDescending(x => x.PeriodEnd).FirstOrDefaultAsync();
        if (s == null) return (null, 0m, 0m, 0m);
        var paid = await PaymentsSinceAsync(card.Id, s.LastTransactionId, null);
        return (s, paid, Math.Max(0m, s.MinimumPayment - paid), Math.Max(0m, s.StatementBalance - paid));
    }

    public Task<BankResult> PayCardAsync(string userId, int cardAccountId, int fromAccountId, CardPaymentOption option, decimal? customAmount) =>
        InTransactionAsync(async () => (await PayCardCoreAsync(userId, cardAccountId, fromAccountId, option, customAmount, autopay: false)));

    private async Task<BankResult> PayCardCoreAsync(string userId, int cardAccountId, int fromAccountId, CardPaymentOption option, decimal? customAmount, bool autopay)
    {
        var card = await GetAccountAsync(cardAccountId, userId);
        if (card is not { IsCreditCard: true }) return new(false, "Card not found.");
        if (card.Status == AccountStatus.Closed) return new(false, "This card account is closed.");
        var from = await GetAccountAsync(fromAccountId, userId);
        if (from == null) return new(false, "Choose one of your checking or savings accounts.");
        if (CanSend(from) is { } blocked) return blocked;

        var (_, _, minimumLeft, statementLeft) = await GetCardDueAsync(card);
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

    // Card payments and statement credits after a statement's last transaction (optionally only up to the end of a bank day).
    private async Task<decimal> PaymentsSinceAsync(int cardId, int afterTransactionId, DateOnly? upToDay)
    {
        var q = _db.Transactions.AsNoTracking().Where(t => t.AccountId == cardId && t.Id > afterTransactionId
                    && (t.Type == TransactionType.CardPayment || t.Type == TransactionType.CashBackCredit));
        if (upToDay is DateOnly d) { var end = BankTime.StartOfDayUtc(d.AddDays(1)); q = q.Where(t => t.Date < end); }
        return (await q.Select(t => t.Amount).ToListAsync()).Sum();   // SQLite can't SUM decimals
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

            card.RewardsBalance = 0m;
            string where;
            if (toAccountId is int toId)
            {
                var to = await GetAccountAsync(toId, userId);
                if (to == null) return new BankResult(false, "Choose one of your checking or savings accounts.");
                if (CanReceive(to) is { } blocked) return blocked;
                to.Balance += amount;
                AddTransaction(to, TransactionType.RewardRedemption, amount, $"Cash back from {card.DisplayName} ••{card.Last4}");
                where = $"{to.DisplayName} ••{to.Last4}";
            }
            else
            {
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
    ///   1. CLOSE every statement whose close date has arrived: interest (only if the previous statement wasn't paid in
    ///      full by its due date: the grace period), statement balance, minimum payment, due date 25 days later.
    ///   2. AUTOPAY on/after the due date, once per statement (minimum or statement balance minus what was already paid).
    ///   3. LATE FEE the day after the due date if the minimum wasn't paid by the end of the due date (once).
    /// Each step is its own transaction; a unique (card, close date) index and concurrency flags stop double work.
    /// </summary>
    public async Task<CardCycleOutcome> RunCardCycleAsync(int cardAccountId, DateOnly today)
    {
        int closed = 0, autopays = 0, autopayFailed = 0, lateFees = 0;
        _db.ChangeTracker.Clear();

        // 1) Statement close(s): catch up month by month if the scheduler was off.
        for (var i = 0; i < 12; i++)
        {
            var r = await InTransactionAsync(async () => await CloseStatementIfDueAsync(cardAccountId, today));
            if (!r.Success) break;
            closed++;
        }

        // 2) Autopay on the due date (or the first run after it).
        var autopay = await AutopayIfDueAsync(cardAccountId, today);
        if (autopay == true) autopays++; else if (autopay == false) autopayFailed++;

        // 3) Late fee after the due date.
        var fee = await InTransactionAsync(async () => await LateFeeIfDueAsync(cardAccountId, today));
        if (fee.Success && fee.Message == "fee") lateFees++;

        return new CardCycleOutcome(closed, autopays, autopayFailed, lateFees);
    }

    private async Task<BankResult> CloseStatementIfDueAsync(int cardId, DateOnly today)
    {
        var card = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == cardId);
        if (card is not { IsCreditCard: true } || card.Status == AccountStatus.Closed || card.NextStatementDate is not DateOnly close || close > today)
            return new BankResult(false, "nothing due");

        var settings = await _db.GetSettingsAsync();
        var prev = await _db.CreditCardStatements.AsNoTracking().Where(s => s.AccountId == card.Id)
            .OrderByDescending(s => s.PeriodEnd).FirstOrDefaultAsync();
        var afterId = prev?.LastTransactionId ?? 0;
        var periodStart = prev?.PeriodEnd.AddDays(1) ?? BankTime.DateOf(DateTime.SpecifyKind(card.OpenedAt, DateTimeKind.Utc));

        // Grace period: no interest if the previous statement was paid in full by its due date (or there was nothing owed).
        var paidInFull = prev == null || prev.StatementBalance <= 0
                         || await PaymentsSinceAsync(card.Id, prev.LastTransactionId, prev.DueDate) >= prev.StatementBalance;
        decimal interest = 0m;
        if (!paidInFull)
            interest = CardMath.DailyBalanceInterest(await EndOfDayOwedAsync(card, periodStart, close), settings.CardPurchaseAprPercent);
        if (interest > 0)
        {
            card.Balance -= interest;
            AddTransaction(card, TransactionType.InterestCharge, interest, "Interest charged on purchases",
                           memo: $"{settings.CardPurchaseAprPercent:0.00}% APR, daily balance method");
        }
        await _db.SaveChangesAsync();

        var rows = await _db.Transactions.AsNoTracking().Where(t => t.AccountId == card.Id && t.Id > afterId).ToListAsync();
        var balance = card.AmountOwed;
        var minimum = CardMath.MinimumPayment(balance, settings.CardMinimumPaymentFloor, settings.CardMinimumPaymentPercent);
        var statement = new CreditCardStatement
        {
            AccountId = card.Id, PeriodStart = periodStart, PeriodEnd = close,
            PreviousBalance = prev?.StatementBalance ?? 0m,
            Purchases = rows.Where(t => t.Type is TransactionType.CardPurchase or TransactionType.ProvisionalClawback or TransactionType.ReversalDebit).Sum(t => t.Amount),
            PaymentsAndCredits = rows.Where(t => t.IsCredit).Sum(t => t.Amount),
            InterestCharged = rows.Where(t => t.Type == TransactionType.InterestCharge).Sum(t => t.Amount),
            FeesCharged = rows.Where(t => t.Type == TransactionType.LateFee).Sum(t => t.Amount),
            StatementBalance = balance, MinimumPayment = minimum, DueDate = close.AddDays(25),
            AprPercent = settings.CardPurchaseAprPercent,
            LastTransactionId = rows.Count == 0 ? afterId : rows.Max(t => t.Id),
            ClosedAt = DateTime.UtcNow
        };
        _db.CreditCardStatements.Add(statement);
        card.NextStatementDate = close.AddMonths(1);
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

    /// <summary>What was owed at the end of each bank day from <paramref name="from"/> to <paramref name="to"/> (both included).</summary>
    private async Task<List<decimal>> EndOfDayOwedAsync(Account card, DateOnly from, DateOnly to)
    {
        var rows = await _db.Transactions.AsNoTracking().Where(t => t.AccountId == card.Id)
            .OrderBy(t => t.Date).ThenBy(t => t.Id).Select(t => new { t.Date, t.BalanceAfter }).ToListAsync();
        var list = new List<decimal>();
        var i = 0;
        decimal balance = 0m;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var end = BankTime.StartOfDayUtc(day.AddDays(1));
            while (i < rows.Count && rows[i].Date < end) { balance = rows[i].BalanceAfter; i++; }
            list.Add(Math.Max(0m, -balance));
        }
        return list;
    }

    // true = paid, false = failed, null = nothing to do
    private async Task<bool?> AutopayIfDueAsync(int cardId, DateOnly today)
    {
        _db.ChangeTracker.Clear();
        var card = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == cardId);
        if (card is not { IsCreditCard: true } || card.Autopay == CardAutopay.None || card.AutopayFromAccountId is not int fromId) return null;
        var s = await _db.CreditCardStatements.AsNoTracking().Where(x => x.AccountId == cardId)
            .OrderByDescending(x => x.PeriodEnd).FirstOrDefaultAsync();
        if (s == null || s.AutopayAttemptedAt != null || today < s.DueDate || s.StatementBalance <= 0) return null;

        var option = card.Autopay == CardAutopay.Minimum ? CardPaymentOption.Minimum : CardPaymentOption.StatementBalance;
        var (_, _, minLeft, stmtLeft) = await GetCardDueAsync(card);
        var target = option == CardPaymentOption.Minimum ? minLeft : stmtLeft;
        BankResult result = target <= 0
            ? new BankResult(true, "Already paid before the due date.")
            : await InTransactionAsync(async () => await PayCardCoreAsync(card.UserId, cardId, fromId, option, null, autopay: true));

        // Record the attempt (its own save: a failed payment was rolled back above).
        _db.ChangeTracker.Clear();
        var tracked = await _db.CreditCardStatements.FirstAsync(x => x.Id == s.Id);
        if (tracked.AutopayAttemptedAt != null) return null;   // another run got here first
        tracked.AutopayAttemptedAt = DateTime.UtcNow;
        tracked.AutopayResult = result.Message.Length > 200 ? result.Message[..200] : result.Message;
        if (target > 0)
        {
            if (result.Success)
                _alerts.Add(card.UserId, AlertType.BillPaid, "Card autopay made",
                    $"Autopay paid {target:C} toward your {card.DisplayName} ••{card.Last4}.", $"/accounts/{cardId}");
            else
                _alerts.Add(card.UserId, AlertType.BillFailed, "Card autopay failed",
                    $"Autopay couldn't pay your {card.DisplayName} ••{card.Last4}: {result.Message} Please pay at least {s.MinimumPayment:C} today to avoid a late fee.",
                    $"/accounts/{cardId}");
        }
        await _db.SaveChangesAsync();
        return target <= 0 ? null : result.Success;
    }

    private async Task<BankResult> LateFeeIfDueAsync(int cardId, DateOnly today)
    {
        var s = await _db.CreditCardStatements.Where(x => x.AccountId == cardId && !x.DueDateProcessed && x.DueDate < today)
            .OrderBy(x => x.PeriodEnd).FirstOrDefaultAsync();
        if (s == null) return new BankResult(false, "nothing due");
        var card = await _db.Accounts.FirstAsync(a => a.Id == cardId);
        s.DueDateProcessed = true;
        var paidByDue = await PaymentsSinceAsync(cardId, s.LastTransactionId, s.DueDate);
        if (s.MinimumPayment <= 0 || paidByDue >= s.MinimumPayment || card.Status == AccountStatus.Closed)
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
}

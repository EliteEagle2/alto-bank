using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.Services;

public partial interface IAdminService
{
    // ----- Alto Peak credit cards. Every change: ExecuteAsync (one transaction + an AuditLog row + a reason). -----
    Task<CreditCardListViewModel> GetCreditCardsPageAsync(CreditCardListQuery query, int pageSize = 20);
    Task<CreditCardDetailViewModel?> GetCreditCardAsync(int accountId);
    Task<AdminResult> ChangeCreditLimitAsync(int accountId, decimal newLimit, string reason);
    Task<AdminResult> WaiveLateFeeAsync(int statementId, string reason);
    /// <summary>Only when nothing is owed and there's no credit balance (exactly $0). Unredeemed cash back is forfeited.</summary>
    Task<AdminResult> CloseCreditCardAsync(int accountId, string reason);
    /// <summary>A card with money ON it (a refund after it was paid off): send that credit to the customer's checking or savings.</summary>
    Task<AdminResult> RefundCreditBalanceAsync(int accountId, int toAccountId, string reason);
}

public partial class AdminService
{
    public const decimal MaxAdminCreditLimit = 25_000m;

    public async Task<CreditCardListViewModel> GetCreditCardsPageAsync(CreditCardListQuery query, int pageSize = 20)
    {
        var q = _db.Accounts.AsNoTracking().Include(a => a.User).Where(a => a.AccountType == AccountType.CreditCard);
        if (query.Status is AccountStatus st) q = q.Where(a => a.Status == st);
        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var like = $"%{query.Customer.Trim()}%";
            q = q.Where(a => EF.Functions.Like(a.User!.FirstName + " " + a.User.LastName, like) || EF.Functions.Like(a.User!.Email!, like));
        }
        var page = await PageAsync(q.OrderBy(a => a.User!.LastName).ThenBy(a => a.Id), query.Page, pageSize);
        var ids = page.Items.Select(a => a.Id).ToList();
        var latest = (await _db.CreditCardStatements.AsNoTracking().Where(s => ids.Contains(s.AccountId)).ToListAsync())
            .GroupBy(s => s.AccountId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.PeriodEnd).First());
        var pending = await _db.AccountApplications.CountAsync(a => a.AccountType == AccountType.CreditCard && a.Status == ApplicationStatus.Pending);
        return new CreditCardListViewModel { Query = query, Cards = page, LatestStatements = latest, PendingApplications = pending };
    }

    public async Task<CreditCardDetailViewModel?> GetCreditCardAsync(int accountId)
    {
        var a = await _db.Accounts.AsNoTracking().Include(x => x.User).Include(x => x.Cards)
            .FirstOrDefaultAsync(x => x.Id == accountId && x.AccountType == AccountType.CreditCard);
        if (a == null) return null;
        return new CreditCardDetailViewModel
        {
            Card = a,
            Statements = await _db.CreditCardStatements.AsNoTracking().Where(s => s.AccountId == accountId).OrderByDescending(s => s.PeriodEnd).ToListAsync(),
            DepositAccounts = await _db.Accounts.AsNoTracking()
                .Where(x => x.UserId == a.UserId && x.Status != AccountStatus.Closed && (x.AccountType == AccountType.Checking || x.AccountType == AccountType.Savings))
                .ToListAsync(),
            Recent = await _db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(25).ToListAsync(),
            Application = await _db.AccountApplications.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId),
            AutopayFrom = a.AutopayFromAccountId is int f ? await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == f) : null
        };
    }

    public Task<AdminResult> ChangeCreditLimitAsync(int accountId, decimal newLimit, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var a = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && x.AccountType == AccountType.CreditCard);
            if (a == null) return Fail("Card not found.");
            if (a.Status == AccountStatus.Closed) return Fail("This card is closed.");
            if (decimal.Round(newLimit, 2) != newLimit) return Fail("Use whole cents.");
            if (newLimit < CreditDecisionRules.MinLimit || newLimit > MaxAdminCreditLimit)
                return Fail($"The limit must be between {CreditDecisionRules.MinLimit:C0} and {MaxAdminCreditLimit:C0}.");
            if (newLimit < a.AmountOwed) return Fail($"The limit can't be lower than what's owed ({a.AmountOwed:C}).");
            var before = a.CreditLimit;
            if (before == newLimit) return Fail($"The limit is already {newLimit:C}.");
            a.CreditLimit = newLimit;
            _alerts.Add(a.UserId, AlertType.CardStatement, "Credit limit changed",
                $"The credit limit on your {a.DisplayName} ••{a.Last4} is now {newLimit:C}.", $"/accounts/{a.Id}");
            _audit.Add("Card.LimitChange", $"Credit limit on ••{a.Last4}: {before:C} → {newLimit:C}", "Account", a.Id.ToString(),
                       new { limit = before }, new { limit = newLimit }, reason);
            return Ok($"Credit limit changed to {newLimit:C}.");
        });

    public Task<AdminResult> WaiveLateFeeAsync(int statementId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var s = await _db.CreditCardStatements.FirstOrDefaultAsync(x => x.Id == statementId);
            if (s == null) return Fail("Statement not found.");
            if (!s.LateFeeCharged || s.LateFeeTransactionId is not int feeId) return Fail("No late fee was charged on this statement.");
            if (s.LateFeeWaived) return Fail("That late fee was already waived.");
            var fee = await _db.Transactions.FirstAsync(t => t.Id == feeId);
            if (fee.Status != TransactionStatus.Posted || fee.ReversedByTransactionId != null) return Fail("That late fee was already credited back.");
            var a = await _db.Accounts.FirstAsync(x => x.Id == s.AccountId);
            if (a.Status == AccountStatus.Closed) return Fail("This card is closed.");
            a.Balance += fee.Amount;
            var w = AddTransaction(a, TransactionType.FeeWaiver, fee.Amount, "Late fee waived", memo: CustomerMemo);
            w.Category = fee.Category;
            w.ReversalOfTransactionId = fee.Id;   // the waiver undoes the fee: marked like a reversal so it can't happen twice
            fee.Status = TransactionStatus.Reversed;
            s.LateFeeWaived = true;
            await _db.SaveChangesAsync();
            fee.ReversedByTransactionId = w.Id;
            _alerts.Add(a.UserId, AlertType.CardLateFee, "Late fee waived",
                $"We waived the {fee.Amount:C} late fee on your {a.DisplayName} ••{a.Last4}.", $"/accounts/{a.Id}");
            _audit.Add("Card.WaiveFee", $"Waived the {fee.Amount:C} late fee on ••{a.Last4} (statement {s.PeriodEnd:yyyy-MM-dd})",
                       "Account", a.Id.ToString(), new { waived = false }, new { waived = true, amount = fee.Amount }, reason);
            return Ok($"Waived the {fee.Amount:C} late fee.");
        });

    public Task<AdminResult> RefundCreditBalanceAsync(int accountId, int toAccountId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var card = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && x.AccountType == AccountType.CreditCard);
            if (card == null) return Fail("Card not found.");
            if (card.Balance <= 0) return Fail("There's no credit balance on this card to refund.");
            var to = await _db.Accounts.FirstOrDefaultAsync(x => x.Id == toAccountId);
            if (to == null || to.UserId != card.UserId || !to.IsDepositAccount)
                return Fail("Choose one of the same customer's checking or savings accounts.");
            if (to.Status == AccountStatus.Closed) return Fail("Can't move money into a closed account.");
            var amount = card.Balance;
            card.Balance = 0m;
            to.Balance += amount;
            var outLeg = AddTransaction(card, TransactionType.TransferOut, amount, $"Credit balance refunded to ••{to.Last4}", CustomerMemo, to.Id);
            var inLeg = AddTransaction(to, TransactionType.TransferIn, amount, $"Credit balance refund from {card.DisplayName} ••{card.Last4}", CustomerMemo, card.Id);
            await _db.SaveChangesAsync();
            outLeg.LinkedTransactionId = inLeg.Id;
            inLeg.LinkedTransactionId = outLeg.Id;
            _alerts.Add(card.UserId, AlertType.CardStatement, "Card credit refunded",
                $"The {amount:C} credit on your {card.DisplayName} ••{card.Last4} was sent to {to.DisplayName} ••{to.Last4}.", $"/accounts/{to.Id}");
            _audit.Add("Card.RefundCredit", $"Refunded the {amount:C} credit balance on ••{card.Last4} to ••{to.Last4}", "Account", card.Id.ToString(),
                       new { balance = amount }, new { balance = 0m, to = to.Id }, reason);
            return Ok($"Refunded {amount:C} to {to.DisplayName} ••{to.Last4}.");
        });

    public Task<AdminResult> CloseCreditCardAsync(int accountId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var a = await _db.Accounts.Include(x => x.Cards).FirstOrDefaultAsync(x => x.Id == accountId && x.AccountType == AccountType.CreditCard);
            if (a == null) return Fail("Card not found.");
            if (a.Status == AccountStatus.Closed) return Fail("This card is already closed.");
            if (a.Balance != 0m)
                return Fail(a.Balance < 0
                    ? $"The customer still owes {a.AmountOwed:C}. A card can only be closed at $0."
                    : $"There's a {a.Balance:C} credit balance. Refund it before closing (a card can only be closed at $0).");
            var forfeited = a.RewardsBalance;
            a.Status = AccountStatus.Closed;
            a.StatusReason = reason.Trim();
            a.StatusChangedAt = a.ClosedAt = DateTime.UtcNow;
            a.NextStatementDate = null;
            a.Autopay = CardAutopay.None;
            a.AutopayFromAccountId = null;
            a.RewardsBalance = 0m;
            foreach (var c in a.Cards.Where(c => c.Status != CardStatus.Cancelled)) c.Status = CardStatus.Cancelled;
            _alerts.Add(a.UserId, AlertType.CardStatement, "Card closed",
                $"Your {a.DisplayName} ••{a.Last4} was closed." + (forfeited > 0 ? $" Unredeemed cash back of {forfeited:C} was forfeited." : ""), $"/accounts/{a.Id}");
            _audit.Add("Card.Close", $"Closed ••{a.Last4}" + (forfeited > 0 ? $" ({forfeited:C} cash back forfeited)" : ""), "Account", a.Id.ToString(),
                       new { status = "Active" }, new { status = "Closed", forfeited }, reason);
            return Ok($"Card ••{a.Last4} closed.");
        });
}

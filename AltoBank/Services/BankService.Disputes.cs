using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>
/// Disputes: the money side of provisional credit. Both rows point back at the disputed transaction
/// (ReversalOfTransactionId), like a reversal does, so they can't themselves be reversed, Insights nets them against the
/// original charge, and the admin "Reverse" refuses a transaction that already has one (no double refund).
/// </summary>
public partial class BankService
{
    public async Task<(BankResult Result, Transaction? Transaction)> PostProvisionalCreditInCurrentTransactionAsync(Dispute dispute)
    {
        RequireTransaction(nameof(PostProvisionalCreditInCurrentTransactionAsync));
        var (account, original) = await LoadDisputedAsync(dispute);
        if (account == null || original == null) return (new(false, "The disputed transaction no longer exists."), null);
        // A credit card can take a provisional credit (only a closed account can't); other accounts use the usual rule.
        var blocked = account.IsCreditCard
            ? (account.Status == AccountStatus.Closed ? new BankResult(false, $"{account.DisplayName} is closed.") : null)
            : CanReceive(account);
        if (blocked != null) return (blocked, null);

        account.Balance += dispute.RefundAmount;
        var t = AddTransaction(account, TransactionType.ProvisionalCredit, dispute.RefundAmount,
                               $"Provisional credit: {original.Description}", memo: dispute.Reference);
        t.ReversalOfTransactionId = original.Id;
        t.Category = original.Category;
        await _db.SaveChangesAsync();
        return (new(true, $"{dispute.RefundAmount:C} credited while we review your dispute."), t);
    }

    public async Task<(BankResult Result, Transaction? Transaction, bool Overdrew)> PostProvisionalClawbackInCurrentTransactionAsync(Dispute dispute)
    {
        RequireTransaction(nameof(PostProvisionalClawbackInCurrentTransactionAsync));
        var (account, original) = await LoadDisputedAsync(dispute);
        if (account == null || original == null) return (new(false, "The disputed transaction no longer exists."), null, false);
        if (account.Status == AccountStatus.Closed)
            return (new(false, $"{account.DisplayName} ••{account.Last4} is closed. Reopen it before denying, so the credit can be taken back."), null, false);

        // Deliberately NOT checked against the balance (decision 5): the provisional credit may already have been spent.
        account.Balance -= dispute.RefundAmount;
        var t = AddTransaction(account, TransactionType.ProvisionalClawback, dispute.RefundAmount,
                               $"Provisional credit reversed: {original.Description}", memo: dispute.Reference);
        t.ReversalOfTransactionId = original.Id;
        t.Category = original.Category;
        await _db.SaveChangesAsync();
        return (new(true, $"{dispute.RefundAmount:C} provisional credit taken back."), t, account.Balance < 0);
    }

    private void RequireTransaction(string method)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException($"{method} must be called inside a database transaction.");
    }

    private async Task<(Account? Account, Transaction? Original)> LoadDisputedAsync(Dispute dispute)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == dispute.AccountId && a.UserId == dispute.UserId);
        var original = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == dispute.TransactionId && t.AccountId == dispute.AccountId);
        return (account, original);
    }
}

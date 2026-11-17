using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>Mobile check deposit: the money side. The customer-facing steps (upload, limits, duplicates) are in CheckDepositService.</summary>
public partial class BankService
{
    public async Task<(BankResult Result, Transaction? Transaction)> PostCheckDepositInCurrentTransactionAsync(CheckDeposit deposit)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("PostCheckDepositInCurrentTransactionAsync must be called inside a database transaction.");
        if (deposit.Status != CheckDepositStatus.Pending) return (new(false, $"This check was already {deposit.Status.ToString().ToLowerInvariant()}."), null);
        if (CheckAmount(deposit.Amount) is { } invalid) return (invalid, null);

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == deposit.AccountId && a.UserId == deposit.UserId);
        if (account == null) return (new(false, "The account for this check no longer exists."), null);
        // Stricter than ordinary deposits (which a frozen account can still take): a check on a frozen account waits
        // until the freeze is lifted, because a freeze often means the account is under review for fraud.
        if (!account.IsDepositAccount) return (new(false, "A check can only go into a checking or savings account."), null);
        if (account.Status == AccountStatus.Frozen)
            return (new(false, $"{account.DisplayName} ••{account.Last4} is frozen. Unfreeze it first, or reject the check."), null);
        if (account.Status == AccountStatus.Closed)
            return (new(false, $"{account.DisplayName} ••{account.Last4} is closed, so it can't take a deposit. Reject the check instead."), null);

        account.Balance += deposit.Amount;   // Balance is the concurrency token: a parallel change fails the save
        var t = AddTransaction(account, TransactionType.CheckDeposit, deposit.Amount, "Mobile check deposit", memo: deposit.Reference);
        await _db.SaveChangesAsync();
        return (new(true, $"{deposit.Amount:C} deposited to {account.DisplayName} ••{account.Last4}."), t);
    }
}

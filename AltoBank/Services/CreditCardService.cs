using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>One printable statement: the statement row plus the transactions it covers.</summary>
public record CardStatementView(Account Card, ApplicationUser Owner, CreditCardStatement Statement, List<Transaction> Transactions, decimal RewardsBalance);

/// <summary>
/// ALTO PEAK, customer side without money: statements and autopay settings. (Purchases, payments and redemptions move
/// money, so they're in IBankService.) Everything is filtered by the signed-in customer: another customer's card or
/// statement is "not found".
/// </summary>
public interface ICreditCardService
{
    Task<List<CreditCardStatement>> GetStatementsAsync(string userId, int cardAccountId);
    Task<CardStatementView?> GetStatementAsync(string userId, int statementId);
    /// <summary>The customer's active checking and savings accounts (to pay from, or to receive cash back).</summary>
    Task<List<Account>> GetPayFromAccountsAsync(string userId);
    Task<BankResult?> SetAutopayAsync(string userId, int cardAccountId, CardAutopay mode, int? fromAccountId);
}

public class CreditCardService : ICreditCardService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public CreditCardService(ApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<CreditCardStatement>> GetStatementsAsync(string userId, int cardAccountId) =>
        _db.CreditCardStatements.AsNoTracking()
            .Where(s => s.AccountId == cardAccountId && s.Account!.UserId == userId)
            .OrderByDescending(s => s.PeriodEnd).ToListAsync();

    public async Task<CardStatementView?> GetStatementAsync(string userId, int statementId)
    {
        var s = await _db.CreditCardStatements.AsNoTracking().Include(x => x.Account!).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(x => x.Id == statementId && x.Account!.UserId == userId);   // ownership check
        if (s == null) return null;
        // The rows dated in the statement period (bank days), the same rule the statement close uses.
        var from = BankTime.StartOfDayUtc(s.PeriodStart);
        var to = BankTime.StartOfDayUtc(s.PeriodEnd.AddDays(1));
        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == s.AccountId && t.Date >= from && t.Date < to)
            .OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync();
        return new CardStatementView(s.Account!, s.Account!.User!, s, rows, s.Account.RewardsBalance);
    }

    public Task<List<Account>> GetPayFromAccountsAsync(string userId) =>
        _db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == AccountStatus.Active
                        && (a.AccountType == AccountType.Checking || a.AccountType == AccountType.Savings))
            .OrderBy(a => a.AccountType).ThenBy(a => a.OpenedAt).ToListAsync();

    public async Task<BankResult?> SetAutopayAsync(string userId, int cardAccountId, CardAutopay mode, int? fromAccountId)
    {
        var card = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == cardAccountId && a.UserId == userId && a.AccountType == AccountType.CreditCard);
        if (card == null) return null;
        if (!Enum.IsDefined(mode)) return new(false, "Choose an autopay option.");
        if (card.Status == AccountStatus.Closed) return new(false, "This card account is closed.");
        if (mode != CardAutopay.None)
        {
            var from = fromAccountId is int id ? (await GetPayFromAccountsAsync(userId)).FirstOrDefault(a => a.Id == id) : null;
            if (from == null) return new(false, "Choose the checking or savings account autopay pays from.");
        }
        var before = new { autopay = card.Autopay.ToString(), from = card.AutopayFromAccountId };
        card.Autopay = mode;
        card.AutopayFromAccountId = mode == CardAutopay.None ? null : fromAccountId;
        _audit.Add("Card.Autopay.Set", $"Autopay on ••{card.Last4}: {mode}", "Account", card.Id.ToString(),
                   before, new { autopay = mode.ToString(), from = card.AutopayFromAccountId });
        await _db.SaveChangesAsync();
        return new(true, mode switch
        {
            CardAutopay.Minimum => "Autopay is on: we'll pay the minimum payment on each due date.",
            CardAutopay.StatementBalance => "Autopay is on: we'll pay the full statement balance on each due date.",
            _ => "Autopay is off."
        });
    }
}

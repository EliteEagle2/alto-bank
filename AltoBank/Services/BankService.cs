using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>
/// All of the money logic lives here (not in the controllers) so the rules
/// — no negative amounts, no overdrafts, only touch your own accounts — are in one place.
///
/// LOADING STRATEGIES used in this class (EF Core):
///  • EAGER loading  — GetDashboardDataAsync: .Include()/.ThenInclude() load the user, their
///                     accounts, and recent transactions in ONE query.
///  • EXPLICIT loading — GetAccountWithHistoryAsync: load the account first, then later ask
///                     for its Transactions with Entry(...).Collection(...).LoadAsync().
///  • LAZY loading is NOT enabled. Lazy loading (UseLazyLoadingProxies + virtual navigation
///    properties) silently runs a new SQL query the first time you touch a navigation property.
///    That's convenient but easily causes the "N+1 queries" problem (e.g. one query per account
///    card on the dashboard) and makes it hard to see what the database is doing, so we always
///    load related data on purpose with Include or explicit loading instead.
/// </summary>
public class BankService : IBankService
{
    // Fictional routing number shown on account details (a well-known test value, not a real bank).
    public const string RoutingNumber = "110000000";
    public const decimal MaxAmount = 1_000_000m;

    private readonly ApplicationDbContext _db;

    public BankService(ApplicationDbContext db) => _db = db;

    // =====================================================================
    // Reading accounts
    // =====================================================================

    /// <summary>All accounts owned by the user, checking first.</summary>
    public async Task<List<Account>> GetAccountsAsync(string userId) =>
        await _db.Accounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountType).ThenBy(a => a.OpenedAt)
            .ToListAsync();

    /// <summary>
    /// Returns the account ONLY if it belongs to this user. This ownership check
    /// prevents someone from typing another person's account id into the URL.
    /// </summary>
    public Task<Account?> GetAccountAsync(int accountId, string userId) =>
        _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);

    /// <summary>
    /// EXPLICIT LOADING example: first load just the account, then load its Transactions
    /// collection in a second, deliberate step (only if the account belongs to the user).
    /// </summary>
    public async Task<Account?> GetAccountWithHistoryAsync(int accountId, string userId)
    {
        var account = await GetAccountAsync(accountId, userId);
        if (account == null) return null;

        await _db.Entry(account)
            .Collection(a => a.Transactions)
            .LoadAsync();

        return account;
    }

    /// <summary>
    /// EAGER LOADING example for the dashboard: one query brings back the user, all of their
    /// accounts (.Include), and each account's most recent transactions (.ThenInclude with a
    /// filtered include). No change tracking because the dashboard only reads; "WithIdentityResolution"
    /// still links each Transaction back to its Account (t.Account) so the table can show the account name.
    /// </summary>
    public Task<ApplicationUser?> GetDashboardDataAsync(string userId, int recentPerAccount) =>
        _db.Users
            .AsNoTrackingWithIdentityResolution()
            .Include(u => u.Accounts)
                .ThenInclude(a => a.Transactions
                    .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                    .Take(recentPerAccount))
            .FirstOrDefaultAsync(u => u.Id == userId);

    /// <summary>
    /// Money in vs. money out per day across ALL of a user's accounts (dashboard chart).
    /// Transfers between the user's own accounts are left out, since they aren't new money in or out.
    /// </summary>
    public async Task<List<DailyFlowDto>> GetDailyFlowsAsync(string userId, int days)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => t.Account!.UserId == userId && t.Date >= since
                        && t.Type != TransactionType.TransferIn && t.Type != TransactionType.TransferOut)
            .Select(t => new { t.Date, t.Type, t.Amount })
            .ToListAsync();

        var byDay = rows.GroupBy(r => DateOnly.FromDateTime(r.Date)).ToDictionary(g => g.Key, g => g.ToList());
        return Enumerable.Range(0, days)
            .Select(i => DateOnly.FromDateTime(since.AddDays(i)))
            .Select(d =>
            {
                var list = byDay.GetValueOrDefault(d) ?? new();
                return new DailyFlowDto(d,
                    In: list.Where(r => Transaction.IsCreditType(r.Type)).Sum(r => r.Amount),
                    Out: list.Where(r => !Transaction.IsCreditType(r.Type)).Sum(r => r.Amount));
            })
            .ToList();
    }

    /// <summary>
    /// One page of an account's transactions (used by the JSON API).
    /// Filtered by BOTH the account and its owner, so it's safe by construction: even if a caller
    /// forgot to check ownership first, another user's transactions could never come back.
    /// </summary>
    public async Task<(List<Transaction> Items, int TotalCount)> GetTransactionsPageAsync(string userId, int accountId, int page, int pageSize)
    {
        var query = _db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.Account!.UserId == userId);
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return (items, total);
    }

    // =====================================================================
    // Moving money
    // =====================================================================

    public Task<BankResult> DepositAsync(string userId, int accountId, decimal amount, string? description)
    {
        if (CheckAmount(amount) is { } invalid) return Task.FromResult(invalid);

        return InTransactionAsync(async () =>
        {
            var account = await GetAccountAsync(accountId, userId);
            if (account == null) return new BankResult(false, "Account not found.");

            // Change tracking: EF notices the Balance change on this tracked entity and
            // writes an UPDATE for it when SaveChangesAsync runs.
            account.Balance += amount;
            AddTransaction(account, TransactionType.Deposit, amount, description ?? "Deposit");
            await _db.SaveChangesAsync();
            return new BankResult(true, $"Deposited {amount:C} into {account.DisplayName}.");
        });
    }

    public Task<BankResult> WithdrawAsync(string userId, int accountId, decimal amount, string? description)
    {
        if (CheckAmount(amount) is { } invalid) return Task.FromResult(invalid);

        return InTransactionAsync(async () =>
        {
            var account = await GetAccountAsync(accountId, userId);
            if (account == null) return new BankResult(false, "Account not found.");

            // No overdrafts: you can't take out more than you have.
            if (amount > account.Balance)
                return new BankResult(false, $"Insufficient funds. Available balance is {account.Balance:C}.");

            account.Balance -= amount;
            AddTransaction(account, TransactionType.Withdrawal, amount, description ?? "Withdrawal");
            await _db.SaveChangesAsync();
            return new BankResult(true, $"Withdrew {amount:C} from {account.DisplayName}.");
        });
    }

    /// <summary>
    /// Moves money between two of the user's own accounts. Wrapped in a database
    /// transaction so BOTH sides succeed or NEITHER does — money is never lost or duplicated.
    /// </summary>
    public Task<BankResult> TransferAsync(string userId, int fromId, int toId, decimal amount, string? memo)
    {
        if (CheckAmount(amount) is { } invalid) return Task.FromResult(invalid);
        if (fromId == toId) return Task.FromResult(new BankResult(false, "Choose two different accounts."));

        return InTransactionAsync(async () =>
        {
            var from = await GetAccountAsync(fromId, userId);
            var to = await GetAccountAsync(toId, userId);
            if (from == null || to == null) return new BankResult(false, "Account not found.");

            if (amount > from.Balance)
                return new BankResult(false, $"Insufficient funds. {from.DisplayName} has {from.Balance:C} available.");

            from.Balance -= amount;
            to.Balance += amount;

            var note = string.IsNullOrWhiteSpace(memo) ? "" : $" - {memo.Trim()}";
            AddTransaction(from, TransactionType.TransferOut, amount, $"Transfer to {to.DisplayName}{note}");
            AddTransaction(to, TransactionType.TransferIn, amount, $"Transfer from {from.DisplayName}{note}");

            await _db.SaveChangesAsync();
            return new BankResult(true, $"Transferred {amount:C} from {from.DisplayName} to {to.DisplayName}.");
        });
    }

    // ---------- Shared safety rules for every money movement ----------

    /// <summary>Amount rules enforced HERE (not only in view models), so the API path can't skip them.</summary>
    private static BankResult? CheckAmount(decimal amount)
    {
        if (amount <= 0) return new(false, "Amount must be greater than zero.");
        if (amount > MaxAmount) return new(false, "Amount can't be more than $1,000,000.");
        if (decimal.Round(amount, 2) != amount) return new(false, "Amount can have at most 2 decimal places.");
        return null;
    }

    private const string ConcurrencyMessage =
        "Your balance changed while we were processing this request. Nothing was moved. Please try again.";

    /// <summary>
    /// Runs a money movement inside ONE database transaction:
    ///  • commits only if the step reports success, otherwise rolls back (nothing half-done);
    ///  • Account.Balance is a concurrency token ([ConcurrencyCheck]), so if two requests change the
    ///    same balance at once, the second SaveChanges fails instead of overwriting the first.
    ///    That prevents "lost update" overdrafts. We roll back and ask the user to retry.
    /// </summary>
    private async Task<BankResult> InTransactionAsync(Func<Task<BankResult>> work)
    {
        await using var dbTransaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var result = await work();
            if (result.Success) await dbTransaction.CommitAsync();
            else await dbTransaction.RollbackAsync();
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await dbTransaction.RollbackAsync();
            _db.ChangeTracker.Clear();   // forget the stale balances we loaded
            return new BankResult(false, ConcurrencyMessage);
        }
        catch
        {
            await dbTransaction.RollbackAsync(); // undo everything if anything failed
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // =====================================================================
    // Opening accounts
    // =====================================================================

    /// <summary>Opens an account on its own (used when a new user registers).</summary>
    public async Task<(BankResult Result, Account? Account)> OpenAccountAsync(string userId, OpenAccountRequest request)
    {
        Account? opened = null;
        var result = await InTransactionAsync(async () =>
        {
            var (r, account) = await OpenAccountCoreAsync(userId, request);
            opened = account;
            return r;
        });
        return (result, result.Success ? opened : null);
    }

    /// <summary>
    /// Submits an approved application: opens the account, moves the opening deposit,
    /// saves the application record, and updates the customer's saved profile —
    /// all inside ONE database transaction (all-or-nothing).
    /// </summary>
    public async Task<(BankResult Result, AccountApplication? Application)> SubmitApplicationAsync(
        string userId, AccountApplication application, OpenAccountRequest request)
    {
        var result = await InTransactionAsync(async () =>
        {
            var (r, account) = await OpenAccountCoreAsync(userId, request);
            if (!r.Success || account == null) return r;

            application.UserId = userId;
            application.AccountId = account.Id;
            application.Status = ApplicationStatus.Approved;
            _db.AccountApplications.Add(application);

            // Remember the applicant's details so the next application can prefill them.
            var user = await _db.Users.FirstAsync(u => u.Id == userId);
            user.FirstName = application.FirstName;
            user.LastName = application.LastName;
            user.MiddleInitial = application.MiddleInitial;
            user.DateOfBirth = application.DateOfBirth;
            user.PhoneNumber = application.Phone;
            user.Street = application.Street;
            user.Apt = application.Apt;
            user.City = application.City;
            user.State = application.State;
            user.Zip = application.Zip;

            await _db.SaveChangesAsync();
            return r;
        });
        return (result, result.Success ? application : null);
    }

    /// <summary>
    /// Shared account-opening logic. Must be called inside a database transaction
    /// (both public methods above start one).
    /// </summary>
    private async Task<(BankResult, Account?)> OpenAccountCoreAsync(string userId, OpenAccountRequest request)
    {
        if (request.InitialDeposit < 0) return (new(false, "Opening deposit can't be negative."), null);
        if (request.InitialDeposit > MaxAmount) return (new(false, "Opening deposit can't be more than $1,000,000."), null);

        // If the money comes from an existing account, it must be the user's own and have enough funds.
        Account? source = null;
        if (request.InitialDeposit > 0 && request.FundingAccountId is int sourceId)
        {
            source = await GetAccountAsync(sourceId, userId);
            if (source == null) return (new(false, "Funding account not found."), null);
            if (request.InitialDeposit > source.Balance)
                return (new(false, $"Insufficient funds. {source.DisplayName} has {source.Balance:C} available."), null);
        }

        bool isChecking = request.Type == AccountType.Checking;
        var account = new Account
        {
            UserId = userId,
            AccountType = request.Type,
            AccountNumber = await GenerateAccountNumberAsync(),
            Nickname = Clean(request.Nickname),
            OpenedAt = DateTime.UtcNow,
            // Card design only applies to checking; goals only apply to savings.
            CardDesign = isChecking ? (CardDesigns.All.Contains(request.CardDesign) ? request.CardDesign : CardDesigns.Aurora) : null,
            GoalAmount = isChecking ? null : request.GoalAmount,
            GoalName = isChecking ? null : Clean(request.GoalName)
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(); // saves the account so it gets an Id for its transactions

        if (request.InitialDeposit > 0)
        {
            var amount = request.InitialDeposit;
            account.Balance += amount;

            if (source == null)
            {
                // External deposit (simulated: the money "arrives" from outside Alto).
                var from = string.IsNullOrWhiteSpace(request.ExternalSource) ? "" : $" from {request.ExternalSource}";
                AddTransaction(account, TransactionType.Deposit, amount, $"Opening deposit{from}");
            }
            else
            {
                // Internal transfer: record BOTH sides so each account's history is complete.
                source.Balance -= amount;
                AddTransaction(source, TransactionType.TransferOut, amount, "Opening deposit");
                AddTransaction(account, TransactionType.TransferIn, amount, "Opening deposit");
            }
            await _db.SaveChangesAsync();
        }

        return (new(true, $"Your new {account.AccountType} account ending in {account.Last4} is open."), account);
    }

    // =====================================================================
    // Applications
    // =====================================================================

    public Task<List<AccountApplication>> GetApplicationsAsync(string userId) =>
        _db.AccountApplications
            .AsNoTracking()
            .Include(a => a.Account)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync();

    public Task<AccountApplication?> GetApplicationAsync(int applicationId, string userId) =>
        _db.AccountApplications
            .Include(a => a.Account)
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == userId);

    public Task<AccountApplication?> GetLatestApplicationAsync(string userId) =>
        _db.AccountApplications
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.SubmittedAt)
            .FirstOrDefaultAsync();

    // =====================================================================
    // Admin
    // =====================================================================

    /// <summary>Totals for the admin dashboard (served as JSON by /api/stats).</summary>
    public async Task<StatsDto> GetStatsAsync(int days)
    {
        // Customers = users who are NOT in the Admin role.
        var adminRoleId = await _db.Roles.Where(r => r.Name == "Admin").Select(r => r.Id).FirstOrDefaultAsync();
        var adminIds = await _db.UserRoles.Where(ur => ur.RoleId == adminRoleId).Select(ur => ur.UserId).ToListAsync();
        var customers = await _db.Users.CountAsync(u => !adminIds.Contains(u.Id));

        // SQLite can't SUM decimal columns in SQL, so balances are added up in memory.
        var accounts = await _db.Accounts.AsNoTracking()
            .Select(a => new { a.AccountType, a.Balance })
            .ToListAsync();

        // Transactions per day for the last N days (grouped in memory by local date).
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var recent = await _db.Transactions.AsNoTracking()
            .Where(t => t.Date >= since)
            .Select(t => new { t.Date, t.Amount })
            .ToListAsync();
        var byDay = recent
            .GroupBy(t => DateOnly.FromDateTime(t.Date))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Volume: g.Sum(x => x.Amount)));
        var perDay = Enumerable.Range(0, days)
            .Select(i => DateOnly.FromDateTime(since.AddDays(i)))
            .Select(d => new DailyActivityDto(d, byDay.GetValueOrDefault(d).Count, byDay.GetValueOrDefault(d).Volume))
            .ToList();

        return new StatsDto(
            TotalCustomers: customers,
            TotalAccounts: accounts.Count,
            TotalDeposits: accounts.Sum(a => a.Balance),
            AccountsByType: accounts.GroupBy(a => a.AccountType.ToString())
                                    .ToDictionary(g => g.Key, g => g.Count()),
            BalancesByType: accounts.GroupBy(a => a.AccountType.ToString())
                                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Balance)),
            ApplicationsLast30Days: await _db.AccountApplications.CountAsync(a => a.SubmittedAt >= since),
            TransactionsPerDay: perDay);
    }

    /// <summary>Latest applications across all customers, with the user and account eagerly loaded.</summary>
    public Task<List<AccountApplication>> GetRecentApplicationsAsync(int count) =>
        _db.AccountApplications
            .AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Account)
            .OrderByDescending(a => a.SubmittedAt)
            .Take(count)
            .ToListAsync();

    // =====================================================================
    // Helpers
    // =====================================================================

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Records a transaction row with the account's new balance as the running balance.
    private void AddTransaction(Account account, TransactionType type, decimal amount, string description)
    {
        _db.Transactions.Add(new Transaction
        {
            AccountId = account.Id,
            Type = type,
            Amount = amount,
            Description = description.Length > 100 ? description[..100] : description,
            BalanceAfter = account.Balance,
            Date = DateTime.UtcNow
        });
    }

    // Random 10-digit account number (never starts with 0), re-rolled until it's unique.
    // The unique index on AccountNumber in the database is a second safety net.
    private async Task<string> GenerateAccountNumberAsync()
    {
        string number;
        do
        {
            number = Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L).ToString();
        } while (await _db.Accounts.AnyAsync(a => a.AccountNumber == number));
        return number;
    }
}

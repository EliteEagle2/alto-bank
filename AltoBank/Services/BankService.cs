using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>
/// All of the CUSTOMER money logic lives here (not in the controllers), so the rules are in one
/// place and both the MVC pages and the JSON API obey them:
///   • only touch your own accounts (every lookup filters by userId — IDOR protection)
///   • no negative amounts, max $1,000,000, at most 2 decimals, no overdrafts
///   • account STATUS: Frozen blocks withdrawals and outgoing transfers (deposits still allowed);
///     Closed blocks everything
///   • DAILY TRANSFER LIMIT per account (account override, or the bank-wide default in BankSetting)
///   • every money movement runs in ONE database transaction with a concurrency check on Balance
///
/// LOADING STRATEGIES used in this class (EF Core):
///  • EAGER loading  — GetDashboardDataAsync: .Include()/.ThenInclude() load the user, their
///                     accounts, and recent transactions in ONE query.
///  • EXPLICIT loading — GetStatementAsync: load the account first, then later ask for just one
///                     month of its Transactions with Entry(...).Collection(...).Query().LoadAsync().
///  • LAZY loading is NOT enabled. Lazy loading (UseLazyLoadingProxies + virtual navigation
///    properties) silently runs a new SQL query the first time you touch a navigation property.
///    That's convenient but easily causes the "N+1 queries" problem (e.g. one query per account
///    card on the dashboard) and makes it hard to see what the database is doing, so we always
///    load related data on purpose with Include or explicit loading instead.
/// </summary>
public class BankService : IBankService
{
    // Placeholder routing number and bank details shown on Account Details / Direct Deposit (demo only).
    public const string RoutingNumber = "031300000";
    public const string BankName = "Alto Bank";
    public const string BankAddress = "1200 Chestnut Ave, Altoona, PA 16601";
    public const decimal MaxAmount = 1_000_000m;

    private readonly ApplicationDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ICardService _cards;

    public BankService(ApplicationDbContext db, IAuditLogger audit, ICardService cards)
    {
        _db = db;
        _audit = audit;
        _cards = cards;
    }

    // =====================================================================
    // Reading accounts
    // =====================================================================

    /// <summary>All accounts owned by the user (including closed ones, shown with their status), checking first.</summary>
    public async Task<List<Account>> GetAccountsAsync(string userId) =>
        await _db.Accounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.Status == AccountStatus.Closed).ThenBy(a => a.AccountType).ThenBy(a => a.OpenedAt)
            .ToListAsync();

    /// <summary>
    /// Returns the account ONLY if it belongs to this user. This ownership check
    /// prevents someone from typing another person's account id into the URL (IDOR).
    /// </summary>
    public Task<Account?> GetAccountAsync(int accountId, string userId) =>
        _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);

    /// <summary>
    /// EAGER LOADING for the Account Details page: the account, its owner and its cards in one query,
    /// only if the account belongs to this user (IDOR protection).
    /// </summary>
    public Task<Account?> GetAccountDetailsAsync(int accountId, string userId) =>
        _db.Accounts.AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Cards)
            .FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);

    /// <summary>
    /// One page of activity for the Account Details "Activity" tab and its JSON API, plus money in/out
    /// for the WHOLE filtered range. Filtered by owner too, so it's safe by construction.
    /// </summary>
    public async Task<ActivityResult> GetActivityAsync(string userId, int accountId, ActivityFilter filter, int page, int pageSize)
    {
        var credits = new[] { TransactionType.Deposit, TransactionType.TransferIn, TransactionType.Interest,
                              TransactionType.AdjustmentCredit, TransactionType.ReversalCredit };
        var q = _db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId && t.Account!.UserId == userId);

        if (filter.From is DateTime from) q = q.Where(t => t.Date >= from.Date);
        if (filter.To is DateTime to) { var end = to.Date.AddDays(1); q = q.Where(t => t.Date < end); }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var like = $"%{filter.Search.Trim()}%";
            q = q.Where(t => EF.Functions.Like(t.Description, like) || EF.Functions.Like(t.Memo!, like));
        }
        q = filter.Kind switch
        {
            "in" => q.Where(t => credits.Contains(t.Type)),
            "out" => q.Where(t => !credits.Contains(t.Type)),
            "deposit" => q.Where(t => t.Type == TransactionType.Deposit),
            "withdrawal" => q.Where(t => t.Type == TransactionType.Withdrawal),
            "transfer" => q.Where(t => t.Type == TransactionType.TransferIn || t.Type == TransactionType.TransferOut),
            "interest" => q.Where(t => t.Type == TransactionType.Interest),
            "adjustment" => q.Where(t => t.Type == TransactionType.AdjustmentCredit || t.Type == TransactionType.AdjustmentDebit),
            "reversal" => q.Where(t => t.Type == TransactionType.ReversalCredit || t.Type == TransactionType.ReversalDebit),
            _ => q
        };

        // Totals for the whole range (SQLite can't SUM decimals, so add the matching amounts in memory).
        var amounts = await q.Select(t => new { t.Type, t.Amount }).ToListAsync();
        var items = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToListAsync();

        return new ActivityResult(items, amounts.Count,
            MoneyIn: amounts.Where(a => Transaction.IsCreditType(a.Type)).Sum(a => a.Amount),
            MoneyOut: amounts.Where(a => !Transaction.IsCreditType(a.Type)).Sum(a => a.Amount));
    }

    /// <summary>
    /// EXPLICIT LOADING example (monthly statement): load the account first, then load ONLY that
    /// month's transactions with Entry(...).Collection(...).Query().Where(...).LoadAsync().
    /// </summary>
    public async Task<StatementData?> GetStatementAsync(string userId, int accountId, int year, int month)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) return null;
        var account = await _db.Accounts.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);
        if (account == null) return null;

        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        await _db.Entry(account)
            .Collection(a => a.Transactions)
            .Query().Where(t => t.Date >= start && t.Date < end)
            .LoadAsync();

        var rows = account.Transactions.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
        var before = await _db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.Date < start)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => (decimal?)t.BalanceAfter).FirstOrDefaultAsync();
        var opening = before ?? 0m;
        return new StatementData(account, account.User!, year, month, opening,
            ClosingBalance: rows.LastOrDefault()?.BalanceAfter ?? opening,
            TotalIn: rows.Where(t => t.IsCredit).Sum(t => t.Amount),
            TotalOut: rows.Where(t => !t.IsCredit).Sum(t => t.Amount),
            Transactions: rows);
    }

    /// <summary>Bank-wide settings (read-only copy for display: APYs, default limits).</summary>
    public async Task<BankSetting> GetSettingsAsync() =>
        await _db.BankSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == BankSetting.SingletonId) ?? new BankSetting();

    /// <summary>Interest posted to this account since January 1st (owner-filtered).</summary>
    public async Task<decimal> GetInterestThisYearAsync(string userId, int accountId)
    {
        var jan1 = new DateTime(DateTime.UtcNow.Year, 1, 1);
        var amounts = await _db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.Account!.UserId == userId && t.Type == TransactionType.Interest
                        && t.Status == TransactionStatus.Posted && t.Date >= jan1)
            .Select(t => t.Amount).ToListAsync();
        return amounts.Sum();
    }

    /// <summary>"Everyday Checking ••4821" labels for the user's accounts (to show transfer counterparties).</summary>
    public async Task<Dictionary<int, string>> GetAccountLabelsAsync(string userId) =>
        (await _db.Accounts.AsNoTracking().Where(a => a.UserId == userId).ToListAsync())
            .ToDictionary(a => a.Id, a => $"{a.DisplayName} ••{a.Last4}");

    /// <summary>Inline nickname edit from the Account Info tab. Owner-only; audited.</summary>
    public async Task<BankResult> UpdateNicknameAsync(string userId, int accountId, string? nickname)
    {
        nickname = Clean(nickname);
        if (nickname?.Length > 30) return new(false, "Nickname can be at most 30 characters.");
        var account = await GetAccountAsync(accountId, userId);
        if (account == null) return new(false, "Account not found.");
        if (account.Status == AccountStatus.Closed) return new(false, "Closed accounts can't be renamed.");
        var before = account.Nickname;
        account.Nickname = nickname;
        _audit.Add("Account.Rename", $"Renamed ••{account.Last4} to \"{account.DisplayName}\"", "Account", account.Id.ToString(),
                   new { nickname = before }, new { nickname });
        await _db.SaveChangesAsync();
        return new(true, $"Nickname saved: {account.DisplayName}");
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

    /// <summary>The account's daily transfer limit and how much of it has been used today (UTC day).</summary>
    public async Task<(decimal Limit, decimal UsedToday)> GetDailyTransferUsageAsync(Account account)
    {
        var settings = await _db.GetSettingsAsync();
        var limit = account.DailyTransferLimit ?? settings.DefaultDailyTransferLimit;
        var today = DateTime.UtcNow.Date;
        // SQLite can't SUM decimals in SQL, so the (few) amounts are added up in memory.
        var amounts = await _db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == account.Id && t.Type == TransactionType.TransferOut
                        && t.Status == TransactionStatus.Posted && t.Date >= today)
            .Select(t => t.Amount)
            .ToListAsync();
        return (limit, amounts.Sum());
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
            if (CanReceive(account) is { } blocked) return blocked;   // closed accounts can't take deposits

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
            if (CanSend(account) is { } blocked) return blocked;      // frozen / closed

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
    /// The two rows are linked to each other (LinkedTransactionId) so an admin can reverse both legs.
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
            if (CanSend(from) is { } cantSend) return cantSend;
            if (CanReceive(to) is { } cantReceive) return cantReceive;
            if (await CheckDailyLimitAsync(from, amount) is { } overLimit) return overLimit;

            if (amount > from.Balance)
                return new BankResult(false, $"Insufficient funds. {from.DisplayName} has {from.Balance:C} available.");

            from.Balance -= amount;
            to.Balance += amount;

            var cleanMemo = Clean(memo);
            var note = cleanMemo == null ? "" : $" - {cleanMemo}";
            var outLeg = AddTransaction(from, TransactionType.TransferOut, amount, $"Transfer to {to.DisplayName}{note}", cleanMemo, to.Id);
            var inLeg = AddTransaction(to, TransactionType.TransferIn, amount, $"Transfer from {from.DisplayName}{note}", cleanMemo, from.Id);
            await _db.SaveChangesAsync();
            LinkLegs(outLeg, inLeg);

            _audit.Add("Transfer", $"Transferred {amount:C} from ••{from.Last4} to ••{to.Last4}",
                       "Account", from.Id.ToString(), after: new { from = from.Id, to = to.Id, amount, memo = cleanMemo });
            await _db.SaveChangesAsync();
            return new BankResult(true, $"Transferred {amount:C} from {from.DisplayName} to {to.DisplayName}.");
        });
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
    /// Opens an account inside a transaction that the CALLER already started (used by IAdminService
    /// when an admin approves a pending application, so approval + account + deposit + audit are one unit).
    /// </summary>
    public Task<(BankResult Result, Account? Account)> OpenAccountInCurrentTransactionAsync(string userId, OpenAccountRequest request)
    {
        if (_db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("OpenAccountInCurrentTransactionAsync must be called inside a database transaction.");
        return OpenAccountCoreAsync(userId, request);
    }

    /// <summary>
    /// Submits an application. With "Auto-approve applications" ON it is approved at once: the account
    /// is opened, the opening deposit moved, the application saved, and the profile updated, all in
    /// ONE database transaction. With it OFF the application is saved as Pending for an admin to review.
    /// </summary>
    public async Task<(BankResult Result, AccountApplication? Application)> SubmitApplicationAsync(
        string userId, AccountApplication application, OpenAccountRequest request)
    {
        var settings = await _db.GetSettingsAsync();

        var result = await InTransactionAsync(async () =>
        {
            application.UserId = userId;
            application.CardDesign = request.CardDesign;
            application.FundingAccountId = request.InitialDeposit > 0 ? request.FundingAccountId : null;

            BankResult r;
            if (settings.AutoApproveApplications)
            {
                var (opened, account) = await OpenAccountCoreAsync(userId, request);
                if (!opened.Success || account == null) return opened;
                application.AccountId = account.Id;
                application.Status = ApplicationStatus.Approved;
                application.ReviewedAt = DateTime.UtcNow;
                application.ReviewedBy = "Auto-approved";
                r = opened;
            }
            else
            {
                application.Status = ApplicationStatus.Pending;
                r = new BankResult(true, "Thanks! Your application was submitted and is being reviewed.");
            }
            _db.AccountApplications.Add(application);
            await SaveProfileAsync(userId, application);
            await _db.SaveChangesAsync();

            _audit.Add("Application.Submit",
                $"{application.FirstName} {application.LastName} applied for {application.AccountType} ({application.Status})",
                "Application", application.Id.ToString(), after: new { status = application.Status.ToString(), application.AccountType, application.OpeningDeposit });
            await _db.SaveChangesAsync();
            return r;
        });
        return (result, result.Success ? application : null);
    }

    // Remember the applicant's details so the next application can prefill them.
    private async Task SaveProfileAsync(string userId, AccountApplication app)
    {
        var user = _db.Users.Local.FirstOrDefault(u => u.Id == userId) ?? await _db.Users.FirstAsync(u => u.Id == userId);
        user.FirstName = app.FirstName;
        user.LastName = app.LastName;
        user.MiddleInitial = app.MiddleInitial;
        user.DateOfBirth = app.DateOfBirth;
        user.PhoneNumber = app.Phone;
        user.Street = app.Street;
        user.Apt = app.Apt;
        user.City = app.City;
        user.State = app.State;
        user.Zip = app.Zip;
    }

    /// <summary>
    /// Shared account-opening logic. Must be called inside a database transaction.
    /// Checking accounts automatically get a virtual debit card.
    /// </summary>
    private async Task<(BankResult, Account?)> OpenAccountCoreAsync(string userId, OpenAccountRequest request)
    {
        if (request.InitialDeposit < 0) return (new(false, "Opening deposit can't be negative."), null);
        if (request.InitialDeposit > 0 && CheckAmount(request.InitialDeposit) is { } invalid) return (invalid, null);

        // If the money comes from an existing account it must be the user's own, able to send money
        // (not frozen/closed), within its daily transfer limit, and have enough funds.
        Account? source = null;
        if (request.InitialDeposit > 0 && request.FundingAccountId is int sourceId)
        {
            source = await GetAccountAsync(sourceId, userId);
            if (source == null) return (new(false, "Funding account not found."), null);
            if (CanSend(source) is { } blocked) return (blocked, null);
            if (await CheckDailyLimitAsync(source, request.InitialDeposit) is { } overLimit) return (overLimit, null);
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
            Status = AccountStatus.Active,
            // Card design only applies to checking; goals only apply to savings.
            CardDesign = isChecking ? (CardDesigns.All.Contains(request.CardDesign) ? request.CardDesign : CardDesigns.Aurora) : null,
            GoalAmount = isChecking ? null : request.GoalAmount,
            GoalName = isChecking ? null : Clean(request.GoalName)
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(); // saves the account so it gets an Id for its transactions

        if (isChecking)
        {
            var user = _db.Users.Local.FirstOrDefault(u => u.Id == userId) ?? await _db.Users.FirstAsync(u => u.Id == userId);
            await _cards.IssueAsync(account, $"{user.FirstName} {user.LastName}", account.CardDesign);
        }

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
                var outLeg = AddTransaction(source, TransactionType.TransferOut, amount, "Opening deposit", counterpartyId: account.Id);
                var inLeg = AddTransaction(account, TransactionType.TransferIn, amount, "Opening deposit", counterpartyId: source.Id);
                await _db.SaveChangesAsync();
                LinkLegs(outLeg, inLeg);
            }
        }
        await _db.SaveChangesAsync();

        _audit.Add("Account.Open", $"Opened {account.AccountType} ••{account.Last4} with {request.InitialDeposit:C}",
                   "Account", account.Id.ToString(),
                   after: new { account.AccountType, account.Nickname, opening = request.InitialDeposit, fundingAccountId = source?.Id });
        await _db.SaveChangesAsync();

        return (new(true, $"Your new {account.AccountType} account ending in {account.Last4} is open."), account);
    }

    // =====================================================================
    // Applications (customer's own)
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
    // Shared rules (also used by IAdminService through the static helpers)
    // =====================================================================

    /// <summary>Amount rules enforced HERE (not only in view models), so the API path can't skip them.</summary>
    public static BankResult? CheckAmount(decimal amount)
    {
        if (amount <= 0) return new(false, "Amount must be greater than zero.");
        if (amount > MaxAmount) return new(false, "Amount can't be more than $1,000,000.");
        if (decimal.Round(amount, 2) != amount) return new(false, "Amount can have at most 2 decimal places.");
        return null;
    }

    /// <summary>Can money LEAVE this account? (withdrawals, outgoing transfers, opening-deposit source)</summary>
    public static BankResult? CanSend(Account account) => account.Status switch
    {
        AccountStatus.Frozen => new(false, $"{account.DisplayName} is frozen, so withdrawals and transfers out are blocked. Deposits are still allowed. Please contact support."),
        AccountStatus.Closed => new(false, $"{account.DisplayName} is closed, so money can't be moved out of it."),
        _ => null
    };

    /// <summary>Can money come IN to this account? (deposits, incoming transfers) Frozen accounts can still receive.</summary>
    public static BankResult? CanReceive(Account account) =>
        account.Status == AccountStatus.Closed
            ? new(false, $"{account.DisplayName} is closed, so it can't receive money.")
            : null;

    private async Task<BankResult?> CheckDailyLimitAsync(Account from, decimal amount)
    {
        var (limit, used) = await GetDailyTransferUsageAsync(from);
        var left = Math.Max(0, limit - used);
        return amount > left
            ? new(false, $"This would go over the {limit:C} daily transfer limit for {from.DisplayName}. You can transfer up to {left:C} more today.")
            : null;
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
            else { await dbTransaction.RollbackAsync(); _db.ChangeTracker.Clear(); }
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

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Records a transaction row with the account's new balance as the running balance.
    private Transaction AddTransaction(Account account, TransactionType type, decimal amount, string description,
                                       string? memo = null, int? counterpartyId = null)
    {
        var t = new Transaction
        {
            AccountId = account.Id,
            Type = type,
            Amount = amount,
            Description = description.Length > 100 ? description[..100] : description,
            BalanceAfter = account.Balance,
            Memo = memo,
            CounterpartyAccountId = counterpartyId,
            Date = DateTime.UtcNow
        };
        _db.Transactions.Add(t);
        return t;
    }

    // Point the two legs of a transfer at each other (needs their Ids, so call after SaveChanges).
    private static void LinkLegs(Transaction outLeg, Transaction inLeg)
    {
        outLeg.LinkedTransactionId = inLeg.Id;
        inLeg.LinkedTransactionId = outLeg.Id;
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

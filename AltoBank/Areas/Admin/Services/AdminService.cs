using System.Security.Cryptography;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.Services;

/// <summary>
/// Admin portal logic. See IAdminService for the rules every change follows
/// (one DB transaction, an AuditLog row, a required reason, never hard-delete).
///
/// Every write goes through ExecuteAsync(...), which starts the transaction, checks the reason,
/// commits only on success, and rolls back on failure or a concurrency conflict.
///
/// Note on SQLite + decimals: SQLite stores decimals as text and can't SUM/compare them in SQL,
/// so balance/amount totals and range filters are done in memory after the other filters run in SQL.
/// That's fine at demo scale; a production database (SQL Server/Postgres) would do it in the query.
/// </summary>
public partial class AdminService : IAdminService
{
    private const int MinReasonLength = 5;

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditLogger _audit;
    private readonly IBankService _bank;
    private readonly ICardService _cards;
    private readonly IHttpContextAccessor _http;
    private readonly IAlertService _alerts;
    private readonly AltoBank.Services.Scheduling.ISchedulerRunner _scheduler;

    public AdminService(ApplicationDbContext db, UserManager<ApplicationUser> users, IAuditLogger audit,
                        IBankService bank, ICardService cards, IHttpContextAccessor http, IAlertService alerts,
                        AltoBank.Services.Scheduling.ISchedulerRunner scheduler)
    {
        _alerts = alerts;
        _scheduler = scheduler;
        _db = db;
        _users = users;
        _audit = audit;
        _bank = bank;
        _cards = cards;
        _http = http;
    }

    private string AdminEmail => _http.HttpContext?.User.Identity?.Name ?? "admin";

    // =====================================================================
    // Overview
    // =====================================================================

    public async Task<OverviewViewModel> GetOverviewAsync()
    {
        var settings = await _db.GetSettingsAsync();
        var today = BankTime.StartOfTodayUtc;   // bank midnight, in UTC
        var openAccounts = await _db.Accounts.AsNoTracking()
            .Where(a => a.Status != AccountStatus.Closed)
            .Select(a => new { a.AccountType, a.Balance }).ToListAsync();

        // "Large transactions": last 30 days at or above the threshold (amounts compared in memory).
        var since = BankTime.StartOfDayUtc(BankTime.Today.AddDays(-30));
        var recentAmounts = await _db.Transactions.AsNoTracking().Where(t => t.Date >= since)
            .Select(t => new { t.Id, t.Amount }).ToListAsync();
        var largeIds = recentAmounts.Where(t => t.Amount >= settings.LargeTransactionThreshold).Select(t => t.Id).ToList();
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        return new OverviewViewModel
        {
            Customers = (await _users.GetUsersInRoleAsync(SeedData.CustomerRole)).Count,
            TotalDeposits = openAccounts.Sum(a => a.Balance),
            AccountsByType = openAccounts.GroupBy(a => a.AccountType.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            TransactionsToday = await _db.Transactions.CountAsync(t => t.Date >= today),
            PendingApplications = await _db.AccountApplications.CountAsync(a => a.Status == ApplicationStatus.Pending),
            FrozenAccounts = await _db.Accounts.CountAsync(a => a.Status == AccountStatus.Frozen),
            LargeThreshold = settings.LargeTransactionThreshold,
            // Eager loading (.Include) so the panels can show names without extra queries.
            PendingList = await _db.AccountApplications.AsNoTracking().Include(a => a.User)
                .Where(a => a.Status == ApplicationStatus.Pending).OrderBy(a => a.SubmittedAt).Take(6).ToListAsync(),
            FrozenList = await _db.Accounts.AsNoTracking().Include(a => a.User)
                .Where(a => a.Status == AccountStatus.Frozen).OrderByDescending(a => a.StatusChangedAt).Take(6).ToListAsync(),
            LargeTransactions = await _db.Transactions.AsNoTracking().Include(t => t.Account!).ThenInclude(a => a.User)
                .Where(t => largeIds.Contains(t.Id)).OrderByDescending(t => t.Date).Take(6).ToListAsync(),
            RecentApplications = await _db.AccountApplications.AsNoTracking().Include(a => a.User)
                .OrderByDescending(a => a.SubmittedAt).Take(6).ToListAsync(),
            FailedScheduled = await _db.ScheduledPayments.AsNoTracking().Include(s => s.User).Include(s => s.Payee).Include(s => s.ToAccount)
                .Where(s => s.LastRunStatus == ScheduledRunStatus.Failed && s.LastRunAt >= weekAgo)
                .OrderByDescending(s => s.LastRunAt).Take(6).ToListAsync(),
            RecentAdminActivity = await _db.AuditLogs.AsNoTracking().Where(l => l.ActorRole == "Admin")
                .OrderByDescending(l => l.Id).Take(8).ToListAsync(),
            SchedulerLastRunAt = settings.SchedulerLastRunAt,
            SchedulerLastRunSummary = settings.SchedulerLastRunSummary
        };
    }

    public async Task<AdminStatsDto> GetStatsAsync(int days)
    {
        var overview = await GetOverviewAsync();
        var accounts = await _db.Accounts.AsNoTracking().Where(a => a.Status != AccountStatus.Closed)
            .Select(a => new { a.AccountType, a.Balance }).ToListAsync();

        var firstDay = BankTime.Today.AddDays(-(days - 1));
        var since = BankTime.StartOfDayUtc(firstDay);
        var recent = await _db.Transactions.AsNoTracking().Where(t => t.Date >= since)
            .Select(t => new { t.Date, t.Amount }).ToListAsync();
        var byDay = recent.GroupBy(t => BankTime.DateOf(t.Date))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Volume: g.Sum(x => x.Amount)));
        var perDay = Enumerable.Range(0, days).Select(i => firstDay.AddDays(i))
            .Select(d => new DailyActivityDto(d, byDay.GetValueOrDefault(d).Count, byDay.GetValueOrDefault(d).Volume))
            .ToList();

        // New accounts per week (last 12 weeks, weeks start on Monday).
        var thisMonday = BankTime.Today.AddDays(-(((int)BankTime.Today.DayOfWeek + 6) % 7));
        var firstWeek = thisMonday.AddDays(-7 * 11);
        var opened = await _db.Accounts.AsNoTracking()
            .Where(a => a.OpenedAt >= BankTime.StartOfDayUtc(firstWeek)).Select(a => a.OpenedAt).ToListAsync();
        var perWeek = Enumerable.Range(0, 12).Select(i => firstWeek.AddDays(7 * i))
            .Select(w => new WeeklyCountDto(w, opened.Count(o => BankTime.DateOf(o) >= w && BankTime.DateOf(o) < w.AddDays(7))))
            .ToList();

        return new AdminStatsDto(
            TotalCustomers: overview.Customers,
            TotalAccounts: accounts.Count,
            TotalDeposits: accounts.Sum(a => a.Balance),
            AccountsByType: overview.AccountsByType,
            DepositsByType: accounts.GroupBy(a => a.AccountType.ToString()).ToDictionary(g => g.Key, g => g.Sum(x => x.Balance)),
            TransactionsToday: overview.TransactionsToday,
            PendingApplications: overview.PendingApplications,
            FrozenAccounts: overview.FrozenAccounts,
            TransactionsPerDay: perDay,
            NewAccountsPerWeek: perWeek);
    }

    // =====================================================================
    // Customers
    // =====================================================================

    public async Task<PagedList<CustomerRow>> SearchCustomersAsync(CustomerListQuery query, int pageSize = 20)
    {
        var customerIds = (await _users.GetUsersInRoleAsync(SeedData.CustomerRole)).Select(u => u.Id).ToList();
        var q = _db.Users.AsNoTracking().Where(u => customerIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = $"%{query.Search.Trim()}%";
            q = q.Where(u => EF.Functions.Like(u.FirstName + " " + u.LastName, like) || EF.Functions.Like(u.Email!, like));
        }

        // Projection: only the columns the table needs (+ account count as a SQL subquery).
        var rows = await q.Select(u => new
        {
            u.Id, u.FirstName, u.LastName, u.Email, u.LockoutEnd, u.CreatedAt,
            Count = u.Accounts.Count(a => a.Status != AccountStatus.Closed)
        }).ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var balances = (await _db.Accounts.AsNoTracking()
                .Where(a => ids.Contains(a.UserId) && a.Status != AccountStatus.Closed)
                .Select(a => new { a.UserId, a.Balance }).ToListAsync())
            .GroupBy(a => a.UserId).ToDictionary(g => g.Key, g => g.Sum(x => x.Balance));

        var now = DateTimeOffset.UtcNow;
        var list = rows.Select(r => new CustomerRow(r.Id, $"{r.FirstName} {r.LastName}", r.Email ?? "", r.Count,
            balances.GetValueOrDefault(r.Id), r.LockoutEnd > now, r.CreatedAt)).ToList();

        IEnumerable<CustomerRow> sorted = query.Sort switch
        {
            "name" => list.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            "email" => list.OrderBy(r => r.Email, StringComparer.OrdinalIgnoreCase),
            "accounts" => list.OrderBy(r => r.AccountCount),
            "balance" => list.OrderBy(r => r.TotalBalance),
            "status" => list.OrderBy(r => r.IsLocked),
            _ => list.OrderBy(r => r.Joined ?? DateTime.MinValue)
        };
        if (query.Desc) sorted = sorted.Reverse();
        return Page(sorted.ToList(), query.Page, pageSize);
    }

    public async Task<CustomerDetailViewModel?> GetCustomerAsync(string userId)
    {
        // EAGER loading: the customer with accounts (+ cards), notes and applications in one go.
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Accounts).ThenInclude(a => a.Cards)
            .Include(u => u.Notes)
            .Include(u => u.Applications)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !await IsCustomerAsync(user)) return null;

        var activity = await _db.Transactions.AsNoTracking().Include(t => t.Account)
            .Where(t => t.Account!.UserId == userId)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(50).ToListAsync();

        return new CustomerDetailViewModel
        {
            User = user,
            Accounts = user.Accounts.OrderBy(a => a.Status == AccountStatus.Closed).ThenBy(a => a.AccountType).ThenBy(a => a.OpenedAt).ToList(),
            Activity = activity,
            Notes = user.Notes.OrderByDescending(n => n.CreatedAt).ToList(),
            Applications = user.Applications.OrderByDescending(a => a.SubmittedAt).ToList(),
            IsLocked = user.LockoutEnd > DateTimeOffset.UtcNow,
            Profile = ToProfile(user)
        };
    }

    public async Task<ViewAsViewModel?> GetViewAsAsync(string userId)
    {
        var user = await _db.Users.AsNoTrackingWithIdentityResolution()
            .Include(u => u.Accounts).ThenInclude(a => a.Transactions.OrderByDescending(t => t.Date).Take(8))
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !await IsCustomerAsync(user)) return null;
        var accounts = user.Accounts.OrderBy(a => a.Status == AccountStatus.Closed).ThenBy(a => a.AccountType).ToList();
        return new ViewAsViewModel
        {
            User = user,
            Accounts = accounts,
            Recent = accounts.SelectMany(a => a.Transactions).OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).Take(8).ToList(),
            TotalBalance = accounts.Where(a => a.Status != AccountStatus.Closed).Sum(a => a.Balance)
        };
    }

    public async Task<(AdminResult Result, string? UserId, string? TempPassword)> CreateCustomerAsync(CustomerProfileInput input)
    {
        string? newId = null, temp = null;
        var result = await ExecuteAsync(reason: null, reasonRequired: false, async () =>
        {
            var email = input.Email.Trim();
            if (await _users.FindByEmailAsync(email) != null) return Fail("A user with that email already exists.");

            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAt = DateTime.UtcNow, MustChangePassword = true };
            ApplyProfile(user, input);
            temp = GenerateTempPassword();
            var created = await _users.CreateAsync(user, temp);
            if (!created.Succeeded) return Fail(string.Join(" ", created.Errors.Select(e => e.Description)));
            if (IdentityError(await _users.AddToRoleAsync(user, SeedData.CustomerRole)) is { } roleError) return Fail(roleError);
            newId = user.Id;

            _audit.Add("Customer.Create", $"Created customer {user.FirstName} {user.LastName} ({email})",
                       "Customer", user.Id, after: ProfileSnapshot(user));
            return Ok($"Customer {user.FirstName} {user.LastName} created. Give them the temporary password below; they must change it at first login.");
        });
        return (result, newId, result.Success ? temp : null);
    }

    public Task<AdminResult> UpdateCustomerProfileAsync(string userId, CustomerProfileInput input) =>
        ExecuteAsync(null, false, async () =>
        {
            var user = await FindCustomerAsync(userId);
            if (user == null) return Fail("Customer not found.");
            var email = input.Email.Trim();
            var other = await _users.FindByEmailAsync(email);
            if (other != null && other.Id != user.Id) return Fail("Another user already uses that email.");

            var before = ProfileSnapshot(user);
            ApplyProfile(user, input);
            user.Email = email;
            user.UserName = email;
            var updated = await _users.UpdateAsync(user);   // also refreshes the normalized email/user name
            if (!updated.Succeeded) return Fail(string.Join(" ", updated.Errors.Select(e => e.Description)));

            _audit.Add("Customer.UpdateProfile", $"Updated profile of {user.FirstName} {user.LastName}", "Customer", user.Id, before, ProfileSnapshot(user));
            return Ok("Profile saved.");
        });

    public Task<AdminResult> OpenAccountForCustomerAsync(string userId, AccountType type, string? nickname, decimal openingDeposit, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var user = await FindCustomerAsync(userId);
            if (user == null) return Fail("Customer not found.");
            if (openingDeposit < 0 || (openingDeposit > 0 && BankService.CheckAmount(openingDeposit) is not null))
                return Fail("Opening deposit must be between $0 and $1,000,000 with at most 2 decimals.");

            // Same account-opening code as customer applications (checking gets a debit card),
            // running inside THIS transaction. The deposit is recorded as coming from the bank.
            var (result, account) = await _bank.OpenAccountInCurrentTransactionAsync(user.Id,
                new OpenAccountRequest(type, nickname, CardDesigns.Aurora, null, null, openingDeposit, null, "Alto Bank (admin)"));
            if (!result.Success || account == null) return Fail(result.Message);

            _audit.Add("Account.OpenForCustomer", $"Opened {type} ••{account.Last4} for {user.FirstName} {user.LastName} with {openingDeposit:C}",
                       "Account", account.Id.ToString(), after: AccountSnapshot(account), reason: reason);
            return Ok($"Opened {type} account ••{account.Last4} for {user.FirstName} {user.LastName}.");
        });

    public Task<AdminResult> AddNoteAsync(string userId, string text) =>
        ExecuteAsync(null, false, async () =>
        {
            var user = await FindCustomerAsync(userId);
            if (user == null) return Fail("Customer not found.");
            text = (text ?? "").Trim();
            if (text.Length < 2 || text.Length > 2000) return Fail("Notes must be between 2 and 2,000 characters.");

            var author = await _users.GetUserAsync(_http.HttpContext!.User);
            var note = new CustomerNote
            {
                CustomerUserId = user.Id, AuthorUserId = author!.Id,
                AuthorName = $"{author.FirstName} {author.LastName}".Trim(), Text = text, CreatedAt = DateTime.UtcNow
            };
            _db.CustomerNotes.Add(note);
            await _db.SaveChangesAsync();
            _audit.Add("Customer.AddNote", $"Added a note to {user.FirstName} {user.LastName}", "Customer", user.Id, after: new { note.Id, length = text.Length });
            return Ok("Note added.");
        });

    public Task<AdminResult> LockCustomerAsync(string userId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var user = await FindCustomerAsync(userId);
            if (user == null) return Fail("Customer not found.");
            if (user.LockoutEnd > DateTimeOffset.UtcNow) return Fail("This customer's login is already locked.");

            // Each Identity call returns a result; any failure stops here and the transaction rolls back.
            if (IdentityError(await _users.SetLockoutEnabledAsync(user, true)) is { } e1) return Fail(e1);
            if (IdentityError(await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)) is { } e2) return Fail(e2); // can't log in
            if (IdentityError(await _users.UpdateSecurityStampAsync(user)) is { } e3) return Fail(e3);                       // signs them out now
            _audit.Add("Customer.Lock", $"Locked login for {user.Email}", "Customer", user.Id,
                       new { locked = false }, new { locked = true }, reason);
            return Ok($"{user.FirstName} {user.LastName} can no longer log in.");
        });

    public Task<AdminResult> UnlockCustomerAsync(string userId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var user = await FindCustomerAsync(userId);
            if (user == null) return Fail("Customer not found.");
            if (!(user.LockoutEnd > DateTimeOffset.UtcNow)) return Fail("This customer's login isn't locked.");

            if (IdentityError(await _users.SetLockoutEndDateAsync(user, null)) is { } e1) return Fail(e1);
            if (IdentityError(await _users.ResetAccessFailedCountAsync(user)) is { } e2) return Fail(e2);
            _audit.Add("Customer.Unlock", $"Unlocked login for {user.Email}", "Customer", user.Id,
                       new { locked = true }, new { locked = false }, reason);
            return Ok($"{user.FirstName} {user.LastName} can log in again.");
        });

    public async Task<(AdminResult Result, string? TempPassword)> ResetPasswordAsync(string userId, string reason)
    {
        string? temp = null;
        var result = await ExecuteAsync(reason, true, async () =>
        {
            var user = await FindCustomerAsync(userId);
            if (user == null) return Fail("Customer not found.");

            temp = GenerateTempPassword();
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var reset = await _users.ResetPasswordAsync(user, token, temp);
            if (!reset.Succeeded) return Fail(string.Join(" ", reset.Errors.Select(e => e.Description)));
            user.MustChangePassword = true;
            if (IdentityError(await _users.UpdateAsync(user)) is { } e1) return Fail(e1);
            if (IdentityError(await _users.UpdateSecurityStampAsync(user)) is { } e2) return Fail(e2);
            // The temporary password itself is NEVER written to the audit log.
            _audit.Add("Customer.ResetPassword", $"Reset password for {user.Email} (must change at next login)", "Customer", user.Id,
                       after: new { mustChangePassword = true }, reason: reason);
            return Ok("Password reset. Share the temporary password below with the customer; it is shown only once.");
        });
        return (result, result.Success ? temp : null);
    }

    public Task<AdminResult> SetForcePasswordChangeAsync(string userId, bool required, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var user = await FindCustomerAsync(userId);
            if (user == null) return Fail("Customer not found.");
            var before = user.MustChangePassword;
            user.MustChangePassword = required;
            if (IdentityError(await _users.UpdateAsync(user)) is { } e1) return Fail(e1);
            _audit.Add("Customer.ForcePasswordChange", $"{(required ? "Required" : "Cleared")} password change for {user.Email}",
                       "Customer", user.Id, new { mustChangePassword = before }, new { mustChangePassword = required }, reason);
            return Ok(required ? "The customer must change their password at next login." : "Password change requirement removed.");
        });

    // =====================================================================
    // Accounts
    // =====================================================================

    public async Task<PagedList<Account>> SearchAccountsAsync(AccountListQuery query, int pageSize = 20)
    {
        var q = _db.Accounts.AsNoTracking().Include(a => a.User).AsQueryable();
        if (query.Type is AccountType type) q = q.Where(a => a.AccountType == type);
        if (query.Status is AccountStatus status) q = q.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Owner))
        {
            var like = $"%{query.Owner.Trim()}%";
            q = q.Where(a => EF.Functions.Like(a.User!.FirstName + " " + a.User.LastName, like)
                          || EF.Functions.Like(a.User.Email!, like) || EF.Functions.Like(a.AccountNumber, like)
                          || EF.Functions.Like(a.Nickname!, like));
        }
        q = q.OrderByDescending(a => a.OpenedAt).ThenByDescending(a => a.Id);

        if (query.MinBalance == null && query.MaxBalance == null)
            return await PageAsync(q, query.Page, pageSize);   // paged in SQL

        var all = await q.ToListAsync();                         // balance range filtered in memory (SQLite decimals)
        var filtered = all.Where(a => (query.MinBalance == null || a.Balance >= query.MinBalance)
                                   && (query.MaxBalance == null || a.Balance <= query.MaxBalance)).ToList();
        return Page(filtered, query.Page, pageSize);
    }

    public async Task<AccountDetailViewModel?> GetAccountAsync(int accountId)
    {
        // EAGER loading for the detail page: owner, cards and full transaction history.
        var account = await _db.Accounts.AsNoTracking()
            .Include(a => a.User)
            .Include(a => a.Cards)
            .Include(a => a.Transactions)
            .FirstOrDefaultAsync(a => a.Id == accountId);
        if (account == null) return null;

        var settings = await _db.GetSettingsAsync();
        var (limit, used) = await _bank.GetDailyTransferUsageAsync(account);
        return new AccountDetailViewModel
        {
            Account = account,
            Transactions = account.Transactions.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id).ToList(),
            OtherAccounts = await _db.Accounts.AsNoTracking()
                .Where(a => a.UserId == account.UserId && a.Id != account.Id && a.Status != AccountStatus.Closed).ToListAsync(),
            DailyLimit = limit,
            UsedToday = used,
            UsesDefaultLimit = account.DailyTransferLimit == null,
            Apy = account.AccountType == AccountType.Savings ? settings.SavingsApy : settings.CheckingApy
        };
    }

    public Task<AdminResult> FreezeAccountAsync(int accountId, string reason) =>
        ChangeAccountAsync(accountId, reason, "Account.Freeze", account =>
        {
            if (account.Status != AccountStatus.Active) return Fail($"Only an active account can be frozen (this one is {account.Status}).");
            SetStatus(account, AccountStatus.Frozen, reason);
            Notify(account, AlertType.AccountFrozen, "Account frozen",
                $"{account.DisplayName} ••{account.Last4} was frozen: withdrawals, transfers and payments out are blocked. Deposits still work. Please contact support.");
            return Ok($"{account.DisplayName} ••{account.Last4} is frozen. Withdrawals and transfers out are blocked; deposits still work.");
        });

    public Task<AdminResult> UnfreezeAccountAsync(int accountId, string reason) =>
        ChangeAccountAsync(accountId, reason, "Account.Unfreeze", account =>
        {
            if (account.Status != AccountStatus.Frozen) return Fail("Only a frozen account can be unfrozen.");
            SetStatus(account, AccountStatus.Active, null);
            Notify(account, AlertType.AccountUnfrozen, "Account unfrozen", $"{account.DisplayName} ••{account.Last4} is active again. Everything works as normal.");
            return Ok($"{account.DisplayName} ••{account.Last4} is active again.");
        });

    public Task<AdminResult> ReopenAccountAsync(int accountId, string reason) =>
        ChangeAccountAsync(accountId, reason, "Account.Reopen", account =>
        {
            if (account.Status != AccountStatus.Closed) return Fail("Only a closed account can be reopened.");
            SetStatus(account, AccountStatus.Active, null);
            account.ClosedAt = null;
            return Ok($"{account.DisplayName} ••{account.Last4} is reopened. (Its old cards stay cancelled; the customer can request a new one.)");
        });

    public Task<AdminResult> CloseAccountAsync(int accountId, int? moveBalanceToAccountId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var account = await _db.Accounts.Include(a => a.Cards).FirstOrDefaultAsync(a => a.Id == accountId);
            if (account == null) return Fail("Account not found.");
            if (account.Status == AccountStatus.Closed) return Fail("This account is already closed.");
            var before = AccountSnapshot(account);
            var moved = 0m;

            if (account.Balance > 0)
            {
                // A balance must go somewhere first: another open account of the SAME customer.
                if (moveBalanceToAccountId is not int targetId) return Fail($"The balance is {account.Balance:C}. Choose another of the customer's accounts to move it to before closing.");
                var target = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == targetId);
                if (target == null || target.UserId != account.UserId || target.Id == account.Id)
                    return Fail("The balance can only be moved to another account owned by the same customer.");
                if (target.Status == AccountStatus.Closed) return Fail("Can't move money into a closed account.");

                moved = account.Balance;
                account.Balance = 0;
                target.Balance += moved;
                var outLeg = AddTransaction(account, TransactionType.TransferOut, moved, $"Balance moved to ••{target.Last4} (account closed)", reason, target.Id);
                var inLeg = AddTransaction(target, TransactionType.TransferIn, moved, $"Balance moved from ••{account.Last4} (account closed)", reason, account.Id);
                await _db.SaveChangesAsync();
                outLeg.LinkedTransactionId = inLeg.Id;
                inLeg.LinkedTransactionId = outLeg.Id;
            }

            SetStatus(account, AccountStatus.Closed, reason);
            account.ClosedAt = DateTime.UtcNow;
            foreach (var card in account.Cards.Where(c => c.Status != CardStatus.Cancelled))
            {
                card.Status = CardStatus.Cancelled;
                card.CancelledAt = DateTime.UtcNow;
                card.CancelReason = CardCancelReasons.AccountClosed;
            }

            _audit.Add("Account.Close", $"Closed {account.DisplayName} ••{account.Last4}" + (moved > 0 ? $" after moving {moved:C}" : ""),
                       "Account", account.Id.ToString(), before, AccountSnapshot(account), reason);
            return Ok($"{account.DisplayName} ••{account.Last4} is closed." + (moved > 0 ? $" {moved:C} was moved first." : ""));
        });

    public Task<AdminResult> AdjustBalanceAsync(int accountId, bool isCredit, decimal amount, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            if (BankService.CheckAmount(amount) is { } invalid) return Fail(invalid.Message);
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            if (account == null) return Fail("Account not found.");
            if (account.Status == AccountStatus.Closed) return Fail("Can't adjust a closed account. Reopen it first.");
            if (!isCredit && amount > account.Balance) return Fail($"Overdrafts aren't allowed: the balance is only {account.Balance:C}.");

            var before = AccountSnapshot(account);
            account.Balance += isCredit ? amount : -amount;
            var tx = AddTransaction(account, isCredit ? TransactionType.AdjustmentCredit : TransactionType.AdjustmentDebit,
                                    amount, "Admin adjustment", memo: CustomerMemo);
            await _db.SaveChangesAsync();
            _audit.Add("Account.Adjust", $"{(isCredit ? "Credited" : "Debited")} {amount:C} {(isCredit ? "to" : "from")} ••{account.Last4}",
                       "Account", account.Id.ToString(), before, new { balance = account.Balance, transactionId = tx.Id }, reason);
            return Ok($"{(isCredit ? "Credited" : "Debited")} {amount:C}. New balance {account.Balance:C}.");
        });

    public Task<AdminResult> UpdateAccountSettingsAsync(int accountId, string? nickname, decimal? dailyLimit, string reason) =>
        ChangeAccountAsync(accountId, reason, "Account.UpdateSettings", account =>
        {
            nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();
            if (nickname?.Length > 30) return Fail("Nickname can be at most 30 characters.");
            if (dailyLimit is < 0 or > BankService.MaxAmount) return Fail("Daily limit must be between $0 and $1,000,000 (leave blank for the bank default).");
            account.Nickname = nickname;
            account.DailyTransferLimit = dailyLimit;
            return Ok("Account settings saved.");
        });

    public Task<AdminResult> PostInterestAsync(int accountId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            if (account == null) return Fail("Account not found.");
            if (account.AccountType != AccountType.Savings) return Fail("Interest can only be posted to savings accounts.");
            if (account.Status == AccountStatus.Closed) return Fail("Can't post interest to a closed account.");
            // Idempotency: one month's interest at most once per calendar month (UTC), so it can't be paid twice.
            if ((await AccountsPaidThisMonthAsync()).Contains(account.Id))
                return Fail($"Interest for {MonthStart:MMMM yyyy} was already posted to this account.");
            var settings = await _db.GetSettingsAsync();
            var interest = MonthlyInterest(account.Balance, settings.SavingsApy);
            if (interest <= 0) return Fail("No interest is due (the balance is too small).");

            var before = AccountSnapshot(account);
            account.Balance += interest;
            AddTransaction(account, TransactionType.Interest, interest, "Interest earned", memo: $"{settings.SavingsApy:P2} APY (posted by admin)");
            _audit.Add("Account.PostInterest", $"Posted {interest:C} interest to ••{account.Last4}", "Account", account.Id.ToString(),
                       before, AccountSnapshot(account), reason);
            return Ok($"Posted {interest:C} interest at {settings.SavingsApy:P2} APY.");
        });

    // =====================================================================
    // Transactions
    // =====================================================================

    public async Task<PagedList<Transaction>> SearchTransactionsAsync(TransactionListQuery query, int pageSize = 25)
    {
        var q = FilterTransactions(query);
        if (query.MinAmount == null && query.MaxAmount == null) return await PageAsync(q, query.Page, pageSize);
        return Page(FilterAmounts(await q.ToListAsync(), query), query.Page, pageSize);
    }

    public async Task<List<Transaction>> ExportTransactionsAsync(TransactionListQuery query) =>
        FilterAmounts(await FilterTransactions(query).Take(20_000).ToListAsync(), query);

    private IQueryable<Transaction> FilterTransactions(TransactionListQuery query)
    {
        var q = _db.Transactions.AsNoTracking().Include(t => t.Account!).ThenInclude(a => a.User).AsQueryable();
        if (query.From is DateTime from) { var start = BankTime.StartOfDayUtc(from); q = q.Where(t => t.Date >= start); }
        if (query.To is DateTime to) { var end = BankTime.StartOfDayUtc(to.AddDays(1)); q = q.Where(t => t.Date < end); }
        if (query.Type is TransactionType type) q = q.Where(t => t.Type == type);
        if (query.AccountId is int accountId) q = q.Where(t => t.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var like = $"%{query.Customer.Trim()}%";
            q = q.Where(t => EF.Functions.Like(t.Account!.User!.FirstName + " " + t.Account.User.LastName, like)
                          || EF.Functions.Like(t.Account.User.Email!, like));
        }
        return q.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id);
    }

    private static List<Transaction> FilterAmounts(List<Transaction> list, TransactionListQuery query) =>
        list.Where(t => (query.MinAmount == null || t.Amount >= query.MinAmount)
                     && (query.MaxAmount == null || t.Amount <= query.MaxAmount)).ToList();

    public async Task<TransactionDetailViewModel?> GetTransactionAsync(int transactionId)
    {
        var tx = await _db.Transactions.AsNoTracking().Include(t => t.Account!).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(t => t.Id == transactionId);
        if (tx == null) return null;

        async Task<Transaction?> Load(int? id) => id == null ? null
            : await _db.Transactions.AsNoTracking().Include(t => t.Account).FirstOrDefaultAsync(t => t.Id == id);

        var vm = new TransactionDetailViewModel
        {
            Transaction = tx,
            Linked = await Load(tx.LinkedTransactionId),
            ReversalOf = await Load(tx.ReversalOfTransactionId),
            ReversedBy = await Load(tx.ReversedByTransactionId)
        };
        vm.CannotReverseReason = WhyNotReversible(tx, vm.Linked);
        return vm;
    }

    /// <summary>
    /// Reverses a transaction by adding an OFFSETTING transaction (never by editing or deleting the
    /// original). The original is marked Reversed and the two rows are linked to each other.
    /// Transfers are reversed on BOTH legs together. Can't reverse twice or reverse a reversal.
    /// </summary>
    public Task<AdminResult> ReverseTransactionAsync(int transactionId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var tx = await _db.Transactions.Include(t => t.Account).FirstOrDefaultAsync(t => t.Id == transactionId);
            if (tx == null) return Fail("Transaction not found.");
            var linked = tx.LinkedTransactionId is int lid
                ? await _db.Transactions.Include(t => t.Account).FirstOrDefaultAsync(t => t.Id == lid) : null;
            if (WhyNotReversible(tx, linked) is { } why) return Fail(why);

            var legs = linked == null ? new[] { tx } : new[] { tx, linked };
            var reversals = new List<(Transaction Original, Transaction Reversal)>();
            foreach (var leg in legs)
            {
                var account = leg.Account!;
                var undoCredit = leg.IsCredit;   // undoing a credit takes money back out
                if (undoCredit && account.Balance < leg.Amount)
                    return Fail($"Can't reverse: ••{account.Last4} only has {account.Balance:C}, and overdrafts aren't allowed.");
                account.Balance += undoCredit ? -leg.Amount : leg.Amount;
                var rev = AddTransaction(account, undoCredit ? TransactionType.ReversalDebit : TransactionType.ReversalCredit,
                                         leg.Amount, $"Reversal: {leg.Description}", memo: CustomerMemo, counterpartyId: leg.CounterpartyAccountId);
                rev.ReversalOfTransactionId = leg.Id;
                leg.Status = TransactionStatus.Reversed;
                reversals.Add((leg, rev));
            }
            await _db.SaveChangesAsync();
            foreach (var (original, reversal) in reversals) original.ReversedByTransactionId = reversal.Id;

            // A Send & Request payment: mark the payment itself Reversed too.
            var legIds = legs.Select(l => l.Id).ToList();
            // The request it paid (if any) becomes Reversed too, and both customers are told.
            foreach (var p in await _db.Payments.Include(p => p.Sender).Include(p => p.Recipient)
                         .Where(p => legIds.Contains(p.SentTransactionId!.Value) || legIds.Contains(p.ReceivedTransactionId!.Value)).ToListAsync())
            {
                p.Status = PaymentStatus.Reversed;
                if (p.MoneyRequestId is int rid && await _db.MoneyRequests.FirstOrDefaultAsync(r => r.Id == rid) is { } paidRequest)
                    paidRequest.Status = MoneyRequestStatus.Reversed;
                _alerts.Add(p.SenderUserId, AlertType.PaymentReversed, "Payment reversed",
                    $"Your {p.Amount:C} payment to {BankService.ShortName(p.Recipient!)} was reversed and the money returned to your account.", "/payments#activity");
                _alerts.Add(p.RecipientUserId, AlertType.PaymentReversed, "Payment reversed",
                    $"The {p.Amount:C} payment from {BankService.ShortName(p.Sender!)} was reversed and taken back out of your account.", "/payments#activity");
            }

            _audit.Add("Transaction.Reverse", $"Reversed transaction #{tx.Id} ({tx.TypeLabel} {tx.Amount:C})" + (linked != null ? $" and its transfer leg #{linked.Id}" : ""),
                       "Transaction", tx.Id.ToString(),
                       before: legs.Select(l => new { l.Id, status = "Posted", l.Amount }),
                       after: reversals.Select(r => new { original = r.Original.Id, reversal = r.Reversal.Id, balanceAfter = r.Reversal.BalanceAfter }),
                       reason: reason);
            return Ok($"Reversed transaction #{tx.Id}" + (linked != null ? " (both sides of the transfer)." : "."));
        });

    private static string? WhyNotReversible(Transaction tx, Transaction? linked)
    {
        if (tx.IsReversal || tx.ReversalOfTransactionId != null) return "This transaction is itself a reversal and can't be reversed.";
        if (tx.Status == TransactionStatus.Reversed) return "This transaction was already reversed.";
        if (linked != null && linked.Status == TransactionStatus.Reversed) return "The other side of this transfer was already reversed.";
        if (tx.Account?.Status == AccountStatus.Closed || linked?.Account?.Status == AccountStatus.Closed)
            return "One of the accounts is closed. Reopen it before reversing.";
        return null;
    }

    // =====================================================================
    // Applications
    // =====================================================================

    public async Task<PagedList<AccountApplication>> SearchApplicationsAsync(ApplicationListQuery query, int pageSize = 20)
    {
        var q = _db.AccountApplications.AsNoTracking().Include(a => a.User).Include(a => a.Account).AsQueryable();
        if (query.Status is ApplicationStatus status) q = q.Where(a => a.Status == status);
        // Pending first (oldest waiting longest at the top), then everything else newest first.
        q = query.Status == ApplicationStatus.Pending ? q.OrderBy(a => a.SubmittedAt) : q.OrderByDescending(a => a.SubmittedAt);
        return await PageAsync(q, query.Page, pageSize);
    }

    public async Task<Dictionary<ApplicationStatus, int>> CountApplicationsAsync() =>
        await _db.AccountApplications.GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

    public Task<AccountApplication?> GetApplicationAsync(int applicationId) =>
        _db.AccountApplications.AsNoTracking().Include(a => a.User).Include(a => a.Account)
            .FirstOrDefaultAsync(a => a.Id == applicationId);

    public Task<AdminResult> ApproveApplicationAsync(int applicationId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var app = await _db.AccountApplications.FirstOrDefaultAsync(a => a.Id == applicationId);
            if (app == null) return Fail("Application not found.");
            if (app.Status != ApplicationStatus.Pending) return Fail($"Only pending applications can be approved (this one is {app.Status}).");

            // Re-uses the customer account-opening code inside THIS transaction; funds, status and
            // daily limit on the funding account are re-checked now, at approval time.
            var external = app.FundingSource?.StartsWith("External: ") == true ? app.FundingSource["External: ".Length..] : null;
            var (result, account) = await _bank.OpenAccountInCurrentTransactionAsync(app.UserId, new OpenAccountRequest(
                app.AccountType, app.Nickname, app.CardDesign, null, null, app.OpeningDeposit, app.FundingAccountId, external));
            if (!result.Success || account == null) return Fail($"Can't approve: {result.Message}");

            app.Status = ApplicationStatus.Approved;
            app.AccountId = account.Id;
            app.ReviewedAt = DateTime.UtcNow;
            app.ReviewedBy = AdminEmail;
            _audit.Add("Application.Approve", $"Approved application #{app.Id} ({app.AccountType}) → account ••{account.Last4}",
                       "Application", app.Id.ToString(), new { status = "Pending" }, new { status = "Approved", accountId = account.Id }, reason);
            return Ok($"Application #{app.Id} approved. Account ••{account.Last4} is open.");
        });

    public Task<AdminResult> DenyApplicationAsync(int applicationId, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var app = await _db.AccountApplications.FirstOrDefaultAsync(a => a.Id == applicationId);
            if (app == null) return Fail("Application not found.");
            if (app.Status != ApplicationStatus.Pending) return Fail($"Only pending applications can be denied (this one is {app.Status}).");

            app.Status = ApplicationStatus.Denied;
            app.DenialReason = reason.Trim();   // shown to the customer on their profile + decision page
            app.ReviewedAt = DateTime.UtcNow;
            app.ReviewedBy = AdminEmail;
            _audit.Add("Application.Deny", $"Denied application #{app.Id} ({app.AccountType})", "Application", app.Id.ToString(),
                       new { status = "Pending" }, new { status = "Denied" }, reason);
            return Ok($"Application #{app.Id} denied. The customer will see your reason.");
        });

    // =====================================================================
    // Settings
    // =====================================================================

    public async Task<SettingsInput> GetSettingsAsync()
    {
        var s = await _db.GetSettingsAsync();
        return new SettingsInput
        {
            SavingsApyPercent = s.SavingsApy * 100m,
            CheckingApyPercent = s.CheckingApy * 100m,
            DefaultDailyTransferLimit = s.DefaultDailyTransferLimit,
            LargeTransactionThreshold = s.LargeTransactionThreshold,
            AutoApproveApplications = s.AutoApproveApplications,
            UpdatedAt = s.UpdatedAt,
            UpdatedBy = s.UpdatedBy
        };
    }

    public Task<AdminResult> UpdateSettingsAsync(SettingsInput input) =>
        ExecuteAsync(input.Reason, true, async () =>
        {
            var s = await _db.GetSettingsAsync();
            var before = SettingsSnapshot(s);
            s.SavingsApy = Math.Round(input.SavingsApyPercent / 100m, 6);
            s.CheckingApy = Math.Round(input.CheckingApyPercent / 100m, 6);
            s.DefaultDailyTransferLimit = Math.Round(input.DefaultDailyTransferLimit, 2);
            s.LargeTransactionThreshold = Math.Round(input.LargeTransactionThreshold, 2);
            s.AutoApproveApplications = input.AutoApproveApplications;
            s.UpdatedAt = DateTime.UtcNow;
            s.UpdatedBy = AdminEmail;
            _audit.Add("Settings.Update", "Updated bank settings", "BankSetting", s.Id.ToString(), before, SettingsSnapshot(s), input.Reason);
            return Ok("Settings saved.");
        });

    /// <summary>Posts one month of interest to every ACTIVE savings account with a balance, in one transaction.</summary>
    public async Task<(AdminResult Result, InterestRunSummary? Summary)> RunMonthlyInterestAsync(string reason)
    {
        InterestRunSummary? summary = null;
        var result = await ExecuteAsync(reason, true, async () =>
        {
            var settings = await _db.GetSettingsAsync();
            // Idempotency: skip accounts that already got interest this calendar month, so re-running can't pay twice.
            var alreadyPaid = await AccountsPaidThisMonthAsync();
            var savings = (await _db.Accounts
                    .Where(a => a.AccountType == AccountType.Savings && a.Status == AccountStatus.Active).ToListAsync())
                .Where(a => a.Balance > 0 && !alreadyPaid.Contains(a.Id)).ToList();
            if (savings.Count == 0 && alreadyPaid.Count > 0)
                return Fail($"Monthly interest for {MonthStart:MMMM yyyy} was already paid to every eligible account.");

            var lines = new List<(string, decimal)>();
            foreach (var account in savings)
            {
                var interest = MonthlyInterest(account.Balance, settings.SavingsApy);
                if (interest <= 0) continue;
                account.Balance += interest;
                AddTransaction(account, TransactionType.Interest, interest, "Interest earned", memo: $"{settings.SavingsApy:P2} APY (monthly run)");
                lines.Add(($"{account.DisplayName} ••{account.Last4}", interest));
            }
            if (lines.Count == 0) return Fail("No active savings accounts were due interest.");

            summary = new InterestRunSummary(lines.Count, lines.Sum(l => l.Item2), lines);
            _audit.Add("Interest.MonthlyRun", $"Monthly interest: {summary.AccountsCredited} accounts, {summary.TotalPaid:C} total",
                       "BankSetting", BankSetting.SingletonId.ToString(),
                       after: new { accounts = summary.AccountsCredited, total = summary.TotalPaid, apy = settings.SavingsApy }, reason: reason);
            return Ok($"Monthly interest posted: {summary.AccountsCredited} accounts credited, {summary.TotalPaid:C} paid in total.");
        });
        return (result, result.Success ? summary : null);
    }

    // =====================================================================
    // Audit log
    // =====================================================================

    public Task<PagedList<AuditLog>> SearchAuditAsync(AuditListQuery query, int pageSize = 25) =>
        PageAsync(FilterAudit(query), query.Page, pageSize);

    public Task<List<AuditLog>> ExportAuditAsync(AuditListQuery query) => FilterAudit(query).Take(20_000).ToListAsync();

    public Task<List<string>> GetAuditActionsAsync() =>
        _db.AuditLogs.Select(l => l.Action).Distinct().OrderBy(a => a).ToListAsync();

    private IQueryable<AuditLog> FilterAudit(AuditListQuery query)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.User)) q = q.Where(l => EF.Functions.Like(l.ActorEmail!, $"%{query.User.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(query.Action)) q = q.Where(l => l.Action == query.Action);
        if (query.From is DateTime from) { var start = BankTime.StartOfDayUtc(from); q = q.Where(l => l.Timestamp >= start); }
        if (query.To is DateTime to) { var end = BankTime.StartOfDayUtc(to.AddDays(1)); q = q.Where(l => l.Timestamp < end); }
        return q.OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.Id);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static AdminResult Ok(string message) => new(true, message);
    private static AdminResult Fail(string message) => new(false, message);

    // null if the Identity operation worked, otherwise its error text (so we never audit a failed step as a success).
    private static string? IdentityError(IdentityResult result) =>
        result.Succeeded ? null : string.Join(" ", result.Errors.Select(e => e.Description));

    /// <summary>
    /// Runs one admin change: checks the reason, opens a DB transaction, runs the work,
    /// saves + commits on success (including the AuditLog row the work added), otherwise rolls back.
    /// </summary>
    private async Task<AdminResult> ExecuteAsync(string? reason, bool reasonRequired, Func<Task<AdminResult>> work)
    {
        if (reasonRequired && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinReasonLength))
            return Fail($"A reason of at least {MinReasonLength} characters is required for this action.");
        if (reason?.Length > 500) return Fail("Reason can be at most 500 characters.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var result = await work();
            if (result.Success)
            {
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            else
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
            }
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            return Fail("Someone changed this record at the same time. Nothing was saved. Please reload and try again.");
        }
        catch
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // Load (tracked) → change → audit with before/after. Used by the simple account status/settings actions.
    private Task<AdminResult> ChangeAccountAsync(int accountId, string reason, string action, Func<Account, AdminResult> change) =>
        ExecuteAsync(reason, true, async () =>
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
            if (account == null) return Fail("Account not found.");
            var before = AccountSnapshot(account);
            var result = change(account);
            if (!result.Success) return result;
            _audit.Add(action, $"{action.Replace("Account.", "")}: {account.DisplayName} ••{account.Last4}",
                       "Account", account.Id.ToString(), before, AccountSnapshot(account), reason);
            return result;
        });

    // Alert the account owner (saved with the rest of the admin change, in the same transaction).
    private void Notify(Account account, AlertType type, string title, string message) =>
        _alerts.Add(account.UserId, type, title, message, $"/accounts/{account.Id}");

    /// <summary>What the customer sees on an adjustment or reversal. The admin's reason is internal and stays in the AuditLog.</summary>
    private const string CustomerMemo = "Posted by Alto Bank. Questions? Contact support.";

    private static void SetStatus(Account account, AccountStatus status, string? reason)
    {
        account.Status = status;
        account.StatusReason = reason?.Trim();
        account.StatusChangedAt = DateTime.UtcNow;
    }

    private Transaction AddTransaction(Account account, TransactionType type, decimal amount, string description,
                                       string? memo = null, int? counterpartyId = null)
    {
        var t = new Transaction
        {
            AccountId = account.Id, Type = type, Amount = amount,
            Description = description.Length > 100 ? description[..100] : description,
            Memo = memo?.Length > 200 ? memo[..200] : memo,
            BalanceAfter = account.Balance, CounterpartyAccountId = counterpartyId, Date = DateTime.UtcNow
        };
        _db.Transactions.Add(t);
        return t;
    }

    private static DateTime MonthStart => BankTime.StartOfMonthUtc(BankTime.Today.Year, BankTime.Today.Month);   // bank month

    // Ids of accounts that already received (non-reversed) interest this calendar month.
    private async Task<HashSet<int>> AccountsPaidThisMonthAsync() =>
        (await _db.Transactions.AsNoTracking()
            .Where(t => t.Type == TransactionType.Interest && t.Status == TransactionStatus.Posted && t.Date >= MonthStart)
            .Select(t => t.AccountId).Distinct().ToListAsync()).ToHashSet();

    public static decimal MonthlyInterest(decimal balance, decimal apy) =>
        Math.Round(balance * apy / 12m, 2, MidpointRounding.AwayFromZero);

    private async Task<ApplicationUser?> FindCustomerAsync(string userId)
    {
        var user = await _users.FindByIdAsync(userId);
        return user != null && await IsCustomerAsync(user) ? user : null;   // admins can't manage other admins here
    }

    private Task<bool> IsCustomerAsync(ApplicationUser user) => _users.IsInRoleAsync(user, SeedData.CustomerRole);

    private static object AccountSnapshot(Account a) => new
    {
        a.Id, status = a.Status.ToString(), a.Balance, a.Nickname, a.DailyTransferLimit, a.StatusReason, a.ClosedAt
    };

    private static object SettingsSnapshot(BankSetting s) => new
    {
        s.SavingsApy, s.CheckingApy, s.DefaultDailyTransferLimit, s.LargeTransactionThreshold, s.AutoApproveApplications
    };

    private static object ProfileSnapshot(ApplicationUser u) => new
    {
        u.FirstName, u.MiddleInitial, u.LastName, u.Email, u.PhoneNumber, u.DateOfBirth, u.Street, u.Apt, u.City, u.State, u.Zip
    };

    private static CustomerProfileInput ToProfile(ApplicationUser u) => new()
    {
        FirstName = u.FirstName, MiddleInitial = u.MiddleInitial, LastName = u.LastName, Email = u.Email ?? "",
        Phone = u.PhoneNumber, DateOfBirth = u.DateOfBirth, Street = u.Street, Apt = u.Apt, City = u.City, State = u.State, Zip = u.Zip
    };

    private static void ApplyProfile(ApplicationUser user, CustomerProfileInput p)
    {
        user.FirstName = p.FirstName.Trim();
        user.LastName = p.LastName.Trim();
        user.MiddleInitial = string.IsNullOrWhiteSpace(p.MiddleInitial) ? null : p.MiddleInitial.Trim().ToUpperInvariant();
        user.PhoneNumber = string.IsNullOrWhiteSpace(p.Phone) ? null : p.Phone.Trim();
        user.DateOfBirth = p.DateOfBirth;
        user.Street = Blank(p.Street); user.Apt = Blank(p.Apt); user.City = Blank(p.City);
        user.State = Blank(p.State)?.ToUpperInvariant(); user.Zip = Blank(p.Zip);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // e.g. "Alto-kqmx4821!" — meets the password rules (upper, lower, digit, symbol, 8+ chars).
    private static string GenerateTempPassword()
    {
        const string letters = "abcdefghjkmnpqrstuvwxyz";
        var word = string.Concat(Enumerable.Range(0, 4).Select(_ => letters[RandomNumberGenerator.GetInt32(letters.Length)]));
        return $"Alto-{word}{RandomNumberGenerator.GetInt32(1000, 10000)}!";
    }

    private static async Task<PagedList<T>> PageAsync<T>(IQueryable<T> q, int page, int pageSize)
    {
        var total = await q.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedList<T> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    private static PagedList<T> Page<T>(List<T> all, int page, int pageSize)
    {
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(all.Count / (double)pageSize)));
        return new PagedList<T> { Items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList(), Page = page, PageSize = pageSize, TotalCount = all.Count };
    }
}

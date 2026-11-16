using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>How much this customer can still deposit by mobile today (bank day).</summary>
public record CheckDepositLimits(decimal PerCheck, decimal Daily, decimal UsedToday)
{
    public decimal LeftToday => Math.Max(0m, Daily - UsedToday);
}

/// <summary>
/// MOBILE CHECK DEPOSIT, customer side (no money moves here: a check only posts when an admin approves it, through
/// IAdminService → IBankService). Every read and write is filtered by the signed-in customer, so another customer's
/// deposit id (or its images) is simply "not found".
/// </summary>
public interface ICheckDepositService
{
    Task<CheckDepositLimits> GetLimitsAsync(string userId);
    /// <summary>Accounts a check can go into: the customer's ACTIVE checking and savings accounts.</summary>
    Task<List<Account>> GetEligibleAccountsAsync(string userId);
    Task<(BankResult Result, CheckDeposit? Deposit)> SubmitAsync(string userId, int accountId, decimal amount, IFormFile? front, IFormFile? back);
    Task<List<CheckDeposit>> ListAsync(string userId, int take = 20);
    Task<CheckDeposit?> GetAsync(string userId, int id);
    /// <summary>Pending checks for one account (account header "Pending" line and the top of Activity).</summary>
    Task<List<CheckDeposit>> GetPendingAsync(string userId, int accountId);
    /// <summary>The customer's own check image ("front" or "back"); null if it isn't theirs.</summary>
    Task<(Stream Stream, string ContentType)?> OpenImageForCustomerAsync(string userId, int id, string side);
    /// <summary>Any check image, for staff (the admin controller is Admin-only).</summary>
    Task<(Stream Stream, string ContentType)?> OpenImageForStaffAsync(int id, string side);
}

public class CheckDepositService : ICheckDepositService
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStore _files;
    private readonly IAlertService _alerts;
    private readonly IAuditLogger _audit;

    public CheckDepositService(ApplicationDbContext db, IFileStore files, IAlertService alerts, IAuditLogger audit)
    {
        _db = db;
        _files = files;
        _alerts = alerts;
        _audit = audit;
    }

    public async Task<CheckDepositLimits> GetLimitsAsync(string userId)
    {
        var s = await _db.GetSettingsAsync();
        var today = BankTime.StartOfTodayUtc;   // bank midnight, in UTC
        // Pending and approved checks count toward today's limit; a rejected one doesn't. (SQLite can't SUM decimals.)
        var amounts = await _db.CheckDeposits.AsNoTracking()
            .Where(d => d.UserId == userId && d.SubmittedAt >= today && d.Status != CheckDepositStatus.Rejected)
            .Select(d => d.Amount).ToListAsync();
        return new CheckDepositLimits(s.MobileDepositPerCheckLimit, s.MobileDepositDailyLimit, amounts.Sum());
    }

    public Task<List<Account>> GetEligibleAccountsAsync(string userId) =>
        _db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == AccountStatus.Active)
            .OrderBy(a => a.AccountType).ThenBy(a => a.OpenedAt)
            .ToListAsync();

    public async Task<(BankResult Result, CheckDeposit? Deposit)> SubmitAsync(string userId, int accountId, decimal amount, IFormFile? front, IFormFile? back)
    {
        // 1) Amount and account
        if (BankService.CheckAmount(amount) is { } badAmount) return (badAmount, null);
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId);
        if (account == null) return (new(false, "Choose one of your accounts."), null);
        if (account.Status != AccountStatus.Active)
            return (new(false, $"{account.DisplayName} is {account.Status.ToString().ToLowerInvariant()}, so it can't take a mobile deposit."), null);

        // 2) Limits (server-side; the page shows them too)
        var limits = await GetLimitsAsync(userId);
        if (amount > limits.PerCheck)
            return (new(false, $"Mobile deposits are limited to {limits.PerCheck:C} per check. For a larger check, visit a branch."), null);
        if (amount > limits.LeftToday)
            return (new(false, limits.LeftToday == 0
                ? $"You've reached today's {limits.Daily:C} mobile deposit limit. You can deposit more tomorrow."
                : $"That would go over today's {limits.Daily:C} mobile deposit limit. You can deposit up to {limits.LeftToday:C} more today."), null);

        // 3) The photos: real JPG/PNG (by signature), 5 MB max. Nothing is written to disk until everything passes.
        var f = await _files.ReadImageAsync(front, "front of the check");
        if (!f.Ok) return (new(false, f.Error!), null);
        var b = await _files.ReadImageAsync(back, "back of the check");
        if (!b.Ok) return (new(false, b.Error!), null);
        if (f.Image!.Sha256 == b.Image!.Sha256)
            return (new(false, "The front and back photos are the same picture. Take a photo of each side."), null);

        // 4) Duplicate guard: the same front image already deposited (and not rejected) by anyone.
        var earlier = await _db.CheckDeposits.AsNoTracking()
            .Where(d => d.FrontHash == f.Image.Sha256 && d.Status != CheckDepositStatus.Rejected)
            .OrderBy(d => d.SubmittedAt).FirstOrDefaultAsync();
        if (earlier != null)
            return (new(false, earlier.UserId == userId
                ? $"You already deposited this check on {BankTime.ToLocal(earlier.SubmittedAt):MMM d} ({earlier.Reference}). A check can only be deposited once."
                : "This check has already been deposited. If you think that's a mistake, please contact us."), null);

        // 5) Save the files, then the record + "received" alert + audit row together.
        var frontName = await _files.SaveAsync(f.Image);
        var backName = await _files.SaveAsync(b.Image);
        var deposit = new CheckDeposit
        {
            UserId = userId, AccountId = account.Id, Amount = amount,
            FrontImage = frontName, BackImage = backName, FrontHash = f.Image.Sha256,
            SubmittedAt = DateTime.UtcNow
        };
        await using var tx = await _db.Database.BeginTransactionAsync();
        _db.CheckDeposits.Add(deposit);
        await _db.SaveChangesAsync();
        _alerts.Add(userId, AlertType.CheckReceived, "Check received",
            $"We got your {amount:C} check for {account.DisplayName} ••{account.Last4}. We'll review it, usually within one business day.",
            $"/checks/{deposit.Id}");
        _audit.Add("CheckDeposit.Submit", $"Submitted {deposit.Reference}: {amount:C} to ••{account.Last4}", "CheckDeposit", deposit.Id.ToString(),
                   after: new { amount, accountId = account.Id, front = frontName, back = backName });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (new(true, $"Check received. {amount:C} will show as pending in {account.DisplayName} until we review it."), deposit);
    }

    public Task<List<CheckDeposit>> ListAsync(string userId, int take = 20) =>
        _db.CheckDeposits.AsNoTracking().Include(d => d.Account)
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.SubmittedAt).ThenByDescending(d => d.Id)
            .Take(take).ToListAsync();

    public Task<CheckDeposit?> GetAsync(string userId, int id) =>
        _db.CheckDeposits.AsNoTracking().Include(d => d.Account)
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

    public Task<List<CheckDeposit>> GetPendingAsync(string userId, int accountId) =>
        _db.CheckDeposits.AsNoTracking()
            .Where(d => d.UserId == userId && d.AccountId == accountId && d.Status == CheckDepositStatus.Pending)
            .OrderByDescending(d => d.SubmittedAt).ToListAsync();

    public async Task<(Stream Stream, string ContentType)?> OpenImageForCustomerAsync(string userId, int id, string side) =>
        Open(await _db.CheckDeposits.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId), side);

    public async Task<(Stream Stream, string ContentType)?> OpenImageForStaffAsync(int id, string side) =>
        Open(await _db.CheckDeposits.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id), side);

    private (Stream Stream, string ContentType)? Open(CheckDeposit? d, string side) =>
        d == null ? null : side switch
        {
            "front" => _files.Open(d.FrontImage),
            "back" => _files.Open(d.BackImage),
            _ => null
        };
}

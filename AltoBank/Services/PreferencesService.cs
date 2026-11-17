using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>Display, privacy, payment and statement settings (Profile → Preferences).</summary>
public record PreferenceSettings(int? DefaultAccountId, bool HideBalances, string Theme, bool Paperless, bool DiscoverableByPhone);

/// <summary>Alert settings (Profile → Alerts).</summary>
public record AlertSettings(IReadOnlyCollection<AlertCategory> Muted, decimal? LowBalanceAlert, decimal? LargeDebitAlert);

/// <summary>
/// PROFILE SETTINGS: reads and saves a customer's UserPreferences row. Customers without a row get the
/// defaults (nothing is written until they save). Every save is audited with before/after values.
/// </summary>
public interface IPreferencesService
{
    /// <summary>The customer's settings, or the defaults (an unsaved, untracked object).</summary>
    Task<UserPreferences> GetAsync(string userId);
    Task<BankResult> SavePreferencesAsync(string userId, PreferenceSettings settings);
    Task<BankResult> SaveAlertsAsync(string userId, AlertSettings settings);
    Task<BankResult> SetPreferredNameAsync(string userId, string? preferredName);
    /// <summary>The default "from" account if it's still the customer's own, open account; otherwise null.</summary>
    Task<int?> DefaultAccountIdAsync(string userId);
    /// <summary>The customer's last few sign-ins (from the audit log), newest first.</summary>
    Task<List<AltoBank.ViewModels.SignInEntry>> RecentSignInsAsync(string userId, int take = 5);
}

public class PreferencesService : IPreferencesService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditLogger _audit;

    public PreferencesService(ApplicationDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<UserPreferences> GetAsync(string userId) =>
        await _db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId) ?? new UserPreferences { UserId = userId };

    public async Task<BankResult> SavePreferencesAsync(string userId, PreferenceSettings s)
    {
        var theme = (s.Theme ?? "").Trim().ToLowerInvariant();
        if (!UserPreferences.Themes.Contains(theme)) return new BankResult(false, "Choose one of the listed themes.");
        if (s.DefaultAccountId is int id &&
            !await _db.Accounts.AnyAsync(a => a.Id == id && a.UserId == userId && a.Status != AccountStatus.Closed && a.AccountType != AccountType.CreditCard))   // ownership check
            return new BankResult(false, "Choose one of your open accounts as the default.");

        var p = await LoadOrCreateAsync(userId);
        var before = Snapshot(p);
        p.DefaultAccountId = s.DefaultAccountId;
        p.HideBalances = s.HideBalances;
        p.Theme = theme;
        p.Paperless = s.Paperless;
        p.DiscoverableByPhone = s.DiscoverableByPhone;
        return await SaveAsync(p, before, "Profile.Preferences", "Updated display, payment and statement preferences", "Preferences saved.");
    }

    public async Task<BankResult> SaveAlertsAsync(string userId, AlertSettings s)
    {
        foreach (var (value, label) in new[] { (s.LowBalanceAlert, "Low balance"), (s.LargeDebitAlert, "Large purchase") })
        {
            if (value is not decimal v) continue;
            if (v <= 0 || v > UserPreferences.MaxThreshold) return new BankResult(false, $"{label} amount must be between $0.01 and {UserPreferences.MaxThreshold:C}.");
            if (decimal.Round(v, 2) != v) return new BankResult(false, $"{label} amount can have at most 2 decimal places.");
        }

        var p = await LoadOrCreateAsync(userId);
        var before = Snapshot(p);
        var muted = s.Muted.Where(c => c != AlertCategory.Security).Distinct().OrderBy(c => c).ToList();
        p.MutedAlerts = muted.Count == 0 ? null : string.Join(",", muted);
        p.LowBalanceAlert = s.LowBalanceAlert;
        p.LargeDebitAlert = s.LargeDebitAlert;
        return await SaveAsync(p, before, "Profile.Alerts", "Updated alert settings", "Alert settings saved.");
    }

    public async Task<BankResult> SetPreferredNameAsync(string userId, string? preferredName)
    {
        var name = string.IsNullOrWhiteSpace(preferredName) ? null : preferredName.Trim();
        if (name?.Length > 30) return new BankResult(false, "Preferred name can be at most 30 characters.");
        var p = await LoadOrCreateAsync(userId);
        if (p.PreferredName == name) return new BankResult(true, "");
        var before = Snapshot(p);
        p.PreferredName = name;
        return await SaveAsync(p, before, "Profile.PreferredName", "Updated preferred name", "Saved.");
    }

    public async Task<int?> DefaultAccountIdAsync(string userId)
    {
        var id = await _db.UserPreferences.Where(p => p.UserId == userId).Select(p => p.DefaultAccountId).FirstOrDefaultAsync();
        if (id == null) return null;
        return await _db.Accounts.AnyAsync(a => a.Id == id && a.UserId == userId && a.Status != AccountStatus.Closed && a.AccountType != AccountType.CreditCard) ? id : null;
    }

    public async Task<List<AltoBank.ViewModels.SignInEntry>> RecentSignInsAsync(string userId, int take = 5) =>
        (await _db.AuditLogs.AsNoTracking()
            .Where(l => l.Action == "Auth.Login" && l.TargetId == userId)
            .OrderByDescending(l => l.Id).Take(take)
            .Select(l => new { l.Timestamp, l.IpAddress, l.Summary }).ToListAsync())
        .Select(l => new AltoBank.ViewModels.SignInEntry(l.Timestamp, FriendlyIp(l.IpAddress), l.Summary.Contains("demo") ? "Demo button" : "Password"))
        .ToList();

    // Loopback addresses mean the sign-in came from the machine running the app.
    private static string? FriendlyIp(string? ip) =>
        ip is "::1" or "127.0.0.1" or "::ffff:127.0.0.1" ? "This device (localhost)" : ip;

    private async Task<UserPreferences> LoadOrCreateAsync(string userId)
    {
        var p = await _db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == userId);
        if (p != null) return p;
        p = new UserPreferences { UserId = userId };
        _db.UserPreferences.Add(p);
        return p;
    }

    private async Task<BankResult> SaveAsync(UserPreferences p, object before, string action, string summary, string message)
    {
        p.UpdatedAt = DateTime.UtcNow;
        _audit.Add(action, summary, "UserPreferences", p.UserId, before, Snapshot(p));
        await _db.SaveChangesAsync();   // the settings row and its audit row are saved together
        return new BankResult(true, message);
    }

    private static object Snapshot(UserPreferences p) => new
    {
        p.PreferredName, p.DefaultAccountId, p.HideBalances, p.Theme, p.Paperless, p.DiscoverableByPhone,
        p.LowBalanceAlert, p.LargeDebitAlert, p.MutedAlerts
    };
}

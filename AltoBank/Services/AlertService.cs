using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>
/// ALERTS CENTER (the bell in the customer top bar).
///   AddAsync(...) only ADDS the alert to the DbContext; the caller's SaveChanges (inside its own DB transaction)
///   saves it, so "money received" and its alert are committed together or not at all.
///   Every read/update is filtered by the signed-in user's id (IDOR protection).
/// </summary>
public interface IAlertService
{
    /// <summary>Adds an alert (not saved yet; the caller's SaveChanges saves it).</summary>
    Alert Add(string userId, AlertType type, string title, string message, string? link = null);
    /// <summary>Same, but with a dedupeKey it does nothing if that user already has that alert (checks the database).</summary>
    Task<Alert?> AddAsync(string userId, AlertType type, string title, string message, string? link = null, string? dedupeKey = null);
    Task<int> UnreadCountAsync(string userId);
    Task<List<Alert>> ListAsync(string userId, bool unreadOnly, int take = 100);
    Task<Alert?> MarkReadAsync(string userId, int alertId);   // null = not this user's alert
    Task<int> MarkAllReadAsync(string userId);
}

public class AlertService : IAlertService
{
    private readonly ApplicationDbContext _db;

    public AlertService(ApplicationDbContext db) => _db = db;

    public async Task<Alert?> AddAsync(string userId, AlertType type, string title, string message, string? link = null, string? dedupeKey = null)
    {
        if (dedupeKey != null &&
            (_db.Alerts.Local.Any(a => a.UserId == userId && a.DedupeKey == dedupeKey) ||
             await _db.Alerts.AnyAsync(a => a.UserId == userId && a.DedupeKey == dedupeKey)))
            return null;

        var alert = Add(userId, type, title, message, link);
        alert.DedupeKey = dedupeKey;
        return alert;
    }

    public Alert Add(string userId, AlertType type, string title, string message, string? link = null)
    {
        var alert = new Alert
        {
            UserId = userId, Type = type,
            Title = Trim(title, 100), Message = Trim(message, 300),
            Link = link != null && link.StartsWith('/') && !link.StartsWith("//") ? Trim(link, 200) : null,   // only ever a local path
            CreatedAt = DateTime.UtcNow
        };
        // Profile → Alerts: a MUTED category still goes to the Alerts history, but arrives already read, so it
        // doesn't light up the bell. Security alerts (freezes, failed payments, reversals) can't be muted.
        var prefs = _db.UserPreferences.Local.FirstOrDefault(p => p.UserId == userId)
                    ?? _db.UserPreferences.AsNoTracking().FirstOrDefault(p => p.UserId == userId);
        if (prefs?.IsMuted(type) == true) alert.ReadAt = alert.CreatedAt;
        _db.Alerts.Add(alert);
        return alert;
    }

    public Task<int> UnreadCountAsync(string userId) =>
        _db.Alerts.CountAsync(a => a.UserId == userId && a.ReadAt == null);

    public Task<List<Alert>> ListAsync(string userId, bool unreadOnly, int take = 100) =>
        _db.Alerts.AsNoTracking()
            .Where(a => a.UserId == userId && (!unreadOnly || a.ReadAt == null))
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(take).ToListAsync();

    public async Task<Alert?> MarkReadAsync(string userId, int alertId)
    {
        var alert = await _db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId && a.UserId == userId);
        if (alert == null) return null;
        alert.ReadAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return alert;
    }

    public async Task<int> MarkAllReadAsync(string userId)
    {
        var unread = await _db.Alerts.Where(a => a.UserId == userId && a.ReadAt == null).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var a in unread) a.ReadAt = now;
        await _db.SaveChangesAsync();
        return unread.Count;
    }

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}

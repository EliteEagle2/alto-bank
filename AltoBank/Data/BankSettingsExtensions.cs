using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Data;

public static class BankSettingsExtensions
{
    /// <summary>
    /// Loads the single BankSetting row (Id = 1). If it's somehow missing, returns the defaults
    /// (and adds the row so the next SaveChanges creates it).
    /// </summary>
    public static async Task<BankSetting> GetSettingsAsync(this ApplicationDbContext db)
    {
        var settings = await db.BankSettings.FirstOrDefaultAsync(s => s.Id == BankSetting.SingletonId);
        if (settings != null) return settings;
        settings = new BankSetting();
        db.BankSettings.Add(settings);
        return settings;
    }
}

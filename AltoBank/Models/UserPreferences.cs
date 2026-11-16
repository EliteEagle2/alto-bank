using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>Groups of alerts a customer can mute (Profile → Alerts). Security alerts can't be muted.</summary>
public enum AlertCategory { MoneyIn, Requests, Bills, Balance, Budgets, Deposits, Disputes, Security }

/// <summary>
/// PROFILE SETTINGS: one row per customer (created the first time they save a setting; until then the
/// defaults below apply). Display, privacy, payment and alert preferences that don't belong on the Identity user.
/// </summary>
public class UserPreferences
{
    public const decimal MaxThreshold = 100_000m;
    public static readonly string[] Themes = { "aurora", "violet", "emerald", "sunset" };

    [Key, StringLength(450)] public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    /// <summary>Shown in greetings ("Good morning, Jay") instead of the legal first name.</summary>
    [StringLength(30)] public string? PreferredName { get; set; }

    /// <summary>Pre-selected "from" account on Transfer, Send &amp; Request and Bill Pay (must be the customer's own).</summary>
    public int? DefaultAccountId { get; set; }

    /// <summary>Privacy mode: balances are blurred until tapped/hovered.</summary>
    public bool HideBalances { get; set; }

    /// <summary>Accent theme for online banking: aurora (default), violet, emerald or sunset.</summary>
    [StringLength(12)] public string Theme { get; set; } = "aurora";

    /// <summary>Paperless statements (e-statements only).</summary>
    public bool Paperless { get; set; } = true;

    /// <summary>Whether other customers can find you by mobile number in Send &amp; Request (email always works).</summary>
    public bool DiscoverableByPhone { get; set; } = true;

    /// <summary>Alert when a debit takes an account below this balance (null = off).</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal? LowBalanceAlert { get; set; }

    /// <summary>Alert on any single debit of at least this amount (null = off).</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal? LargeDebitAlert { get; set; }

    /// <summary>Muted alert categories, comma-separated (e.g. "Budgets,Deposits"). Muted alerts still go to the
    /// Alerts history but arrive already read, so they don't light up the bell.</summary>
    [StringLength(200)] public string? MutedAlerts { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public IReadOnlySet<AlertCategory> Muted =>
        (MutedAlerts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<AlertCategory>(s, out var c) ? c : (AlertCategory?)null)
            .Where(c => c != null && c != AlertCategory.Security).Select(c => c!.Value).ToHashSet();

    public bool IsMuted(AlertType type) => CategoryOf(type) is var c && c != AlertCategory.Security && Muted.Contains(c);

    public static AlertCategory CategoryOf(AlertType type) => type switch
    {
        AlertType.MoneyReceived => AlertCategory.MoneyIn,
        AlertType.RequestReceived or AlertType.RequestPaid or AlertType.RequestDeclined
            or AlertType.RequestCancelled or AlertType.RequestExpired => AlertCategory.Requests,
        AlertType.BillPaid or AlertType.TransferScheduled => AlertCategory.Bills,
        AlertType.LowBalance or AlertType.LargeDebit => AlertCategory.Balance,
        AlertType.BudgetNearLimit => AlertCategory.Budgets,
        AlertType.CheckReceived or AlertType.CheckApproved or AlertType.CheckRejected => AlertCategory.Deposits,
        AlertType.DisputeUpdate => AlertCategory.Disputes,
        // Failed payments, freezes and reversals are about your money being blocked or taken back: always on.
        _ => AlertCategory.Security
    };

    public static (string Label, string Help) Describe(AlertCategory c) => c switch
    {
        AlertCategory.MoneyIn => ("Money received", "Someone sent you money."),
        AlertCategory.Requests => ("Money requests", "Requests to you, and updates on requests you sent."),
        AlertCategory.Bills => ("Bills & scheduled transfers", "A scheduled bill or transfer went out."),
        AlertCategory.Balance => ("Balance alerts", "Low balance and large purchase alerts (amounts below)."),
        AlertCategory.Budgets => ("Budgets", "You're close to or over a budget."),
        AlertCategory.Deposits => ("Mobile check deposits", "A check deposit was approved or rejected."),
        AlertCategory.Disputes => ("Disputes", "Updates on a transaction you disputed."),
        _ => ("Security & account status", "Failed payments, frozen accounts, reversals. Always on.")
    };
}

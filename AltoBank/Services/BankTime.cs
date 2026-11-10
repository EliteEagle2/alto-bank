namespace AltoBank.Services;

/// <summary>
/// The bank's clock and time zone. Times are always STORED in UTC; this class converts only where
/// "which day is it?" matters, using one bank-wide time zone (appsettings "Bank:TimeZone",
/// default America/New_York):
///   • the daily transfer limit resets at bank midnight, not UTC midnight
///   • scheduled payments are due on bank-local dates
///   • money requests expire, budgets reset, statements and "Today/Yesterday" labels follow bank days
/// Static so Razor views, EF queries and the background scheduler all use the same rules.
/// </summary>
public static class BankTime
{
    public const string DefaultTimeZoneId = "America/New_York";

    public static TimeZoneInfo Zone { get; private set; } = Find(DefaultTimeZoneId);

    /// <summary>Called once at startup with the configured time zone id (IANA, e.g. "America/New_York").</summary>
    public static void Configure(string? timeZoneId) => Zone = Find(string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId);

    private static TimeZoneInfo Find(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    /// <summary>Current instant (UTC). Use for anything stored.</summary>
    public static DateTime UtcNow => DateTime.UtcNow;

    /// <summary>Bank-local wall-clock time right now.</summary>
    public static DateTime Now => ToLocal(DateTime.UtcNow);

    /// <summary>Today's date at the bank.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>Converts a stored UTC time to bank-local time (for display and date math).</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime? ToLocal(DateTime? utc) => utc == null ? null : ToLocal(utc.Value);

    /// <summary>The bank-local date a stored UTC time falls on.</summary>
    public static DateOnly DateOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    /// <summary>The UTC instant when a bank-local day starts (midnight at the bank). Use for "since today" queries.</summary>
    public static DateTime StartOfDayUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), Zone);

    /// <summary>Same, for a bank-local date coming from a form/query string (time part ignored).</summary>
    public static DateTime StartOfDayUtc(DateTime localDate) => StartOfDayUtc(DateOnly.FromDateTime(localDate));

    public static DateTime StartOfTodayUtc => StartOfDayUtc(Today);

    /// <summary>UTC instant when a bank-local month starts.</summary>
    public static DateTime StartOfMonthUtc(int year, int month) => StartOfDayUtc(new DateOnly(year, month, 1));

    public static DateOnly FirstOfThisMonth => new(Today.Year, Today.Month, 1);
}

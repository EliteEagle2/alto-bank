using AltoBank.Models;

namespace AltoBank.Services.Scheduling;

/// <summary>
/// Date math for scheduled payments. Every date here is a BANK-LOCAL calendar date (DateOnly).
///   Once     → just the start date
///   Weekly   → every chosen weekday, starting on the first one on/after the start date
///   Monthly  → the chosen day each month; 29/30/31 in a shorter month means that month's LAST day
/// Used by the scheduler (what's due), the calendar (what's coming) and the forms (first date).
/// </summary>
public static class Recurrence
{
    /// <summary>The first date the item runs on, on or after its start date.</summary>
    public static DateOnly First(ScheduleFrequency frequency, DateOnly start, DayOfWeek? dayOfWeek, int? dayOfMonth) => frequency switch
    {
        ScheduleFrequency.Weekly => start.AddDays(((int)(dayOfWeek ?? start.DayOfWeek) - (int)start.DayOfWeek + 7) % 7),
        ScheduleFrequency.Monthly => MonthDay(start.Year, start.Month, dayOfMonth ?? start.Day) is var d && d >= start
            ? d : MonthDay(start.AddMonths(1).Year, start.AddMonths(1).Month, dayOfMonth ?? start.Day),
        _ => start
    };

    /// <summary>The occurrence after <paramref name="current"/>, or null when there isn't one (one-time, or past the end date).</summary>
    public static DateOnly? After(ScheduledPayment s, DateOnly current)
    {
        DateOnly? next = s.Frequency switch
        {
            // The next chosen weekday AFTER current (always 7 days when current is on that weekday), so the
            // item stays on its weekday even if a date ever got off it.
            ScheduleFrequency.Weekly => First(ScheduleFrequency.Weekly, current.AddDays(1), s.DayOfWeek, null),
            ScheduleFrequency.Monthly => MonthDay(current.AddMonths(1).Year, current.AddMonths(1).Month, s.DayOfMonth ?? current.Day),
            _ => null
        };
        return next != null && s.EndDate is DateOnly end && next > end ? null : next;
    }

    /// <summary>Every date the item will run on between from and to (inclusive), starting at NextRunDate. For the calendar.</summary>
    public static IEnumerable<DateOnly> Between(ScheduledPayment s, DateOnly from, DateOnly to, int max = 60)
    {
        if (s.Status != ScheduleStatus.Active || s.NextRunDate is not DateOnly d) yield break;
        for (int i = 0; i < max && d <= to; i++)
        {
            if (d >= from) yield return d;
            if (After(s, d) is not DateOnly next) yield break;
            d = next;
        }
    }

    /// <summary>Day N of a month, clamped to the month's length (31 in November → Nov 30).</summary>
    public static DateOnly MonthDay(int year, int month, int day) =>
        new(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));
}

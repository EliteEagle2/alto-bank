using System.Text.RegularExpressions;

namespace AltoBank.Services;

/// <summary>
/// Small, pure helper functions for the account application rules.
/// Kept separate from the controller so each rule is easy to read and explain.
/// </summary>
public static class ApplicationRules
{
    public const int MinimumAge = 18;

    /// <summary>Builds a real date from MM/DD/YYYY, or returns null if it doesn't exist (e.g. 02/30).</summary>
    public static DateOnly? TryGetDate(int? month, int? day, int? year)
    {
        if (month is not (>= 1 and <= 12) || year is not (>= 1900 and <= 2100) || day is null) return null;
        if (day < 1 || day > DateTime.DaysInMonth(year.Value, month.Value)) return null;
        return new DateOnly(year.Value, month.Value, day.Value);
    }

    /// <summary>Age in whole years on the given day (birthday not reached yet = one year younger).</summary>
    public static int AgeOn(DateOnly birth, DateOnly today)
    {
        var age = today.Year - birth.Year;
        if (birth > today.AddYears(-age)) age--;
        return age;
    }

    /// <summary>
    /// Returns the 10 digits of a U.S. phone number, or null if it isn't valid.
    /// Accepts any formatting ("(814) 555-0123", "814.555.0123", "+1 814 555 0123").
    /// </summary>
    public static string? NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = Regex.Replace(input, @"\D", "");
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        // Area code and exchange can't start with 0 or 1 in the North American numbering plan.
        return Regex.IsMatch(digits, @"^[2-9]\d{2}[2-9]\d{6}$") ? digits : null;
    }

    public static string FormatPhone(string digits) => $"({digits[..3]}) {digits[3..6]}-{digits[6..]}";

    /// <summary>
    /// The e-signature must be the applicant's name: "First Last" or "First M Last" / "First M. Last".
    /// Case and extra spaces don't matter.
    /// </summary>
    public static bool SignatureMatches(string? signature, string first, string? middleInitial, string last)
    {
        string Norm(string s) => Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(signature)) return false;
        var sig = Norm(signature.Replace(".", " "));
        var options = new List<string> { Norm($"{first} {last}") };
        if (!string.IsNullOrWhiteSpace(middleInitial)) options.Add(Norm($"{first} {middleInitial} {last}"));
        return options.Contains(sig);
    }

    /// <summary>Compares two free-text answers ignoring case and repeated spaces.</summary>
    public static bool SameText(string? a, string? b) =>
        a != null && b != null &&
        string.Equals(Regex.Replace(a.Trim(), @"\s+", " "), Regex.Replace(b.Trim(), @"\s+", " "), StringComparison.OrdinalIgnoreCase);
}

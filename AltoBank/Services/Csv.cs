using System.Globalization;
using System.Text;

namespace AltoBank.Services;

/// <summary>Tiny CSV builder used by the CSV exports (admin transactions, audit log, customer activity).</summary>
public static class Csv
{
    public static string Build(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        return sb.ToString();
    }

    // Quote values that contain commas, quotes or newlines. Neutralize spreadsheet formulas
    // (values starting with = + - @) so a crafted description can't run as a formula in Excel.
    private static string Escape(object? value)
    {
        var s = value switch
        {
            null => "",
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
        if (s.Length > 0 && "=+-@".Contains(s[0]) && value is string) s = "'" + s;
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}

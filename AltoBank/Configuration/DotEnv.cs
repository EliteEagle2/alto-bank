namespace AltoBank.Configuration;

/// <summary>
/// Reads a local <c>.env</c> file (KEY=VALUE per line) into environment variables BEFORE the app builds its
/// configuration, so ASP.NET Core picks the values up like any other environment variable.
///   • Keys use the usual ASP.NET Core form: <c>Bank__OwnerEmail</c> = configuration key <c>Bank:OwnerEmail</c>.
///   • A variable that's already set in the real environment WINS (the file never overrides it).
///   • Blank lines and lines starting with # are ignored; values may be wrapped in "double" or 'single' quotes.
/// The .env file holds secrets (the owner's password), so it's git-ignored; .env.example shows the keys.
/// </summary>
public static class DotEnv
{
    /// <summary>Loads the first .env found in <paramref name="directories"/>. Returns the file used, or null.</summary>
    public static string? Load(params string[] directories)
    {
        var path = directories.Select(d => Path.Combine(d, ".env")).FirstOrDefault(File.Exists);
        if (path == null) return null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ")) line = line[7..].TrimStart();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                value = value[1..^1];
            if (Environment.GetEnvironmentVariable(key) == null)
                Environment.SetEnvironmentVariable(key, value);
        }
        return path;
    }
}

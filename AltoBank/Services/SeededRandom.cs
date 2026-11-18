using System.Security.Cryptography;

namespace AltoBank.Services;

/// <summary>
/// Where new account and card numbers get their randomness. Normally real randomness (a card number comes from the
/// cryptographic generator). While the SEEDER runs it uses a fixed seed instead (<see cref="Use"/>), so a reseed
/// gives every demo account, card and loan the same numbers every time and the screenshots and README stay correct.
/// The override is an AsyncLocal: it only applies to code running inside the seeder's own async flow, never to a web
/// request or the scheduler running at the same time.
/// </summary>
public static class SeededRandom
{
    private static readonly AsyncLocal<Random?> Seeded = new();

    /// <summary>Use a fixed seed until the returned scope is disposed.</summary>
    public static IDisposable Use(int seed)
    {
        var previous = Seeded.Value;
        Seeded.Value = new Random(seed);
        return new Scope(previous);
    }

    /// <summary>A digit 0-9 (or any value below <paramref name="max"/>).</summary>
    public static int Next(int max) => Seeded.Value?.Next(max) ?? RandomNumberGenerator.GetInt32(max);

    public static long NextInt64(long min, long max) => Seeded.Value?.NextInt64(min, max) ?? Random.Shared.NextInt64(min, max);

    private sealed class Scope(Random? previous) : IDisposable
    {
        public void Dispose() => Seeded.Value = previous;
    }
}

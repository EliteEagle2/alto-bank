using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AltoBank.Data;

/// <summary>
/// SQLite allows one writer at a time. The background scheduler and web requests can write at the same
/// moment, so every connection gets a BUSY TIMEOUT: instead of failing at once with "database is locked",
/// SQLite waits (up to 10 s) for the other writer to finish. Combined with WAL mode (set once at startup in
/// Program.cs), readers never block writers and writers never block readers.
/// </summary>
public class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    private const string Pragma = "PRAGMA busy_timeout = 10000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragma;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragma;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

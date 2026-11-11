using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AltoBank.Data;
using AltoBank.Models;

namespace AltoBank.Services;

/// <summary>Who performed an action. Usually read from the logged-in user; passed explicitly for logins.</summary>
public record AuditActor(string? UserId, string? Email, string Role);

/// <summary>
/// Writes AuditLog rows. Two ways to use it:
///   Add(...)        — adds the row to the DbContext only. The caller's SaveChanges (inside its
///                     DB transaction) saves it, so the audit entry and the change it describes
///                     are committed together or not at all.
///   WriteAsync(...) — adds AND saves right away (used for standalone events like logins).
/// </summary>
public interface IAuditLogger
{
    AuditLog Add(string action, string summary, string? targetType = null, string? targetId = null,
                 object? before = null, object? after = null, string? reason = null, AuditActor? actor = null);

    Task WriteAsync(string action, string summary, string? targetType = null, string? targetId = null,
                    object? before = null, object? after = null, string? reason = null, AuditActor? actor = null);
}

public class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditLogger(ApplicationDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public AuditLog Add(string action, string summary, string? targetType = null, string? targetId = null,
                        object? before = null, object? after = null, string? reason = null, AuditActor? actor = null)
    {
        actor ??= CurrentActor();
        var entry = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUserId = actor.UserId,
            ActorEmail = actor.Email,
            ActorRole = actor.Role,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Summary = summary.Length > 400 ? summary[..400] : summary,
            BeforeJson = before == null ? null : JsonSerializer.Serialize(before, Json),
            AfterJson = after == null ? null : JsonSerializer.Serialize(after, Json),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        };
        _db.AuditLogs.Add(entry);
        return entry;
    }

    public async Task WriteAsync(string action, string summary, string? targetType = null, string? targetId = null,
                                 object? before = null, object? after = null, string? reason = null, AuditActor? actor = null)
    {
        Add(action, summary, targetType, targetId, before, after, reason, actor);
        await _db.SaveChangesAsync();
    }

    // Set by ActAs(...) for work the bank does on its own (the scheduler), even when an admin's request started it.
    private static readonly AsyncLocal<AuditActor?> Override = new();

    /// <summary>
    /// Until the returned object is disposed, audit rows written on this async flow use <paramref name="actor"/>
    /// instead of the logged-in user. Used by the scheduler so a bill it pays is logged as "System", not as the
    /// admin who pressed "Run scheduler now".
    /// </summary>
    public static IDisposable ActAs(AuditActor actor)
    {
        var previous = Override.Value;
        Override.Value = actor;
        return new Restore(previous);
    }

    private sealed class Restore(AuditActor? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }

    // The logged-in user making this request (or "System" when there's no web request, e.g. the seeder).
    private AuditActor CurrentActor()
    {
        if (Override.Value is { } forced) return forced;
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return new AuditActor(null, null, _http.HttpContext == null ? "System" : "Anonymous");

        var role = user.IsInRole(SeedData.AdminRole) ? "Admin" : user.IsInRole(SeedData.CustomerRole) ? "Customer" : "User";
        return new AuditActor(user.FindFirstValue(ClaimTypes.NameIdentifier), user.Identity.Name, role);
    }
}

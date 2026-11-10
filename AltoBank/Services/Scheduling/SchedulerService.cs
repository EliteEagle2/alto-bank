namespace AltoBank.Services.Scheduling;

/// <summary>What one scheduler run did (written to the console and, from Feature 2 on, to the database).</summary>
public record SchedulerRunSummary(string Trigger, DateTime StartedAt, DateTime FinishedAt,
    int Processed, int Succeeded, int Failed, int Skipped, int RequestsExpired)
{
    public override string ToString() =>
        $"[Scheduler] {Trigger} run at {BankTime.ToLocal(StartedAt):yyyy-MM-dd HH:mm:ss}: {Processed} processed, " +
        $"{Succeeded} succeeded, {Failed} failed, {Skipped} skipped, {RequestsExpired} request(s) expired " +
        $"({(FinishedAt - StartedAt).TotalMilliseconds:0} ms)";
}

/// <summary>The work of one run. Scoped (it uses the DbContext), so the BackgroundService creates a scope per run.</summary>
public interface ISchedulerRunner
{
    Task<SchedulerRunSummary> RunAsync(string trigger, CancellationToken cancellationToken = default);
}

/// <summary>
/// .NET BACKGROUND SERVICE (IHostedService): starts with the web app and runs every minute
/// (appsettings "Scheduler:IntervalSeconds", default 60; "Scheduler:Enabled": false turns it off).
/// A BackgroundService is a SINGLETON, but the DbContext is SCOPED, so each run creates its own DI scope
/// and asks it for ISchedulerRunner. One run never overlaps the next (the loop awaits each run).
/// Errors are logged and the loop keeps going; nothing is retried silently.
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SchedulerService> _log;
    private readonly TimeSpan _interval;
    private readonly bool _enabled;

    public SchedulerService(IServiceScopeFactory scopes, ILogger<SchedulerService> log, IConfiguration config)
    {
        _scopes = scopes;
        _log = log;
        _interval = TimeSpan.FromSeconds(Math.Max(5, config.GetValue("Scheduler:IntervalSeconds", 60)));
        _enabled = config.GetValue("Scheduler:Enabled", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) { _log.LogInformation("[Scheduler] disabled (Scheduler:Enabled = false)."); return; }
        _log.LogInformation("[Scheduler] started; runs every {Seconds} s.", _interval.TotalSeconds);
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ISchedulerRunner>();
                var summary = await runner.RunAsync("timer", stoppingToken);
                _log.LogInformation("{Summary}", summary.ToString());
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Scheduler] run failed; will try again at the next tick.");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }
}

/// <summary>One scheduler run. Feature 1: expire money requests older than 14 days (and alert the requester).</summary>
public class SchedulerRunner : ISchedulerRunner
{
    private readonly IPaymentRequestService _requests;

    public SchedulerRunner(IPaymentRequestService requests) => _requests = requests;

    public async Task<SchedulerRunSummary> RunAsync(string trigger, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var expired = await _requests.ExpireDueAsync();
        return new SchedulerRunSummary(trigger, started, DateTime.UtcNow, 0, 0, 0, 0, expired);
    }
}

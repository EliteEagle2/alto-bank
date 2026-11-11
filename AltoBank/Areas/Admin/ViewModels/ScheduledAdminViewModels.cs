using AltoBank.Models;

namespace AltoBank.Areas.Admin.ViewModels;

/// <summary>Filters for /admin/scheduled. Failures = list failed RUNS (with their reasons) instead of items.</summary>
public class ScheduledListQuery
{
    public ScheduleStatus? Status { get; set; }
    public ScheduleKind? Kind { get; set; }
    public string? Customer { get; set; }
    public bool Failures { get; set; }
    public int Page { get; set; } = 1;
}

public class ScheduledAdminViewModel
{
    public ScheduledListQuery Query { get; set; } = new();
    public PagedList<ScheduledPayment> Items { get; set; } = new();
    public PagedList<ScheduledRun> FailedRuns { get; set; } = new();
    public int ActiveCount { get; set; }
    public int DueToday { get; set; }
    public int FailedLast7Days { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunSummary { get; set; }
    public List<SchedulerRunLog> RecentRuns { get; set; } = new();
}

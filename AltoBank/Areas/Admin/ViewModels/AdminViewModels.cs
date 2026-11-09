namespace AltoBank.Areas.Admin.ViewModels;

/// <summary>One KPI tile (Areas/Admin/Views/Shared/_KpiTile.cshtml).</summary>
/// <param name="Key">Hook for JavaScript (data-kpi) when the value is loaded from the API.</param>
/// <param name="Tone">Optional color accent: "warn", "danger", "good".</param>
public record KpiTileViewModel(
    string Key, string Label, string Icon,
    string? Value = null, string? Hint = null, string? Tone = null,
    string? LinkController = null, Dictionary<string, string>? LinkQuery = null);

namespace AltoBank.Areas.Admin.ViewModels;

/// <summary>One link in the admin console navigation.</summary>
public record AdminNavLink(string Controller, string Icon, string Text);

/// <summary>A labelled group of links (Label null = no heading, e.g. Overview).</summary>
public record AdminNavGroup(string? Label, AdminNavLink[] Links);

/// <summary>
/// The admin console's navigation, in ONE place: _AdminSidebar renders it, and the layout uses it for the
/// "Admin / Group" breadcrumb. Add a new admin page here.
/// </summary>
public static class AdminNav
{
    public static readonly AdminNavGroup[] Groups =
    {
        new(null, new[] { new AdminNavLink("Dashboard", "bi-speedometer2", "Overview") }),
        new("People", new[]
        {
            new AdminNavLink("Customers", "bi-people", "Customers"),
            new AdminNavLink("Applications", "bi-file-earmark-check", "Applications"),
        }),
        new("Money", new[]
        {
            new AdminNavLink("Accounts", "bi-wallet2", "Accounts"),
            new AdminNavLink("Transactions", "bi-arrow-left-right", "Transactions"),
            new AdminNavLink("Payments", "bi-send", "P2P Payments"),
            new AdminNavLink("Scheduled", "bi-calendar-week", "Scheduled Payments"),
        }),
        new("System", new[]
        {
            new AdminNavLink("Settings", "bi-sliders", "Settings"),
            new AdminNavLink("AuditLog", "bi-journal-text", "Audit Log"),
        }),
    };

    /// <summary>The group label for a controller ("Money" for Accounts), or null.</summary>
    public static string? GroupOf(string? controller) =>
        Groups.FirstOrDefault(g => g.Links.Any(l => string.Equals(l.Controller, controller, StringComparison.OrdinalIgnoreCase)))?.Label;
}

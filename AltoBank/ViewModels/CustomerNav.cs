namespace AltoBank.ViewModels;

/// <summary>
/// The customer navigation in ONE place, used by the sidebar, the phone tab bar and the section tabs.
/// The sidebar shows only the groups (seven places to go). A group with several pages (Move money, Pay &amp; request,
/// Insights) shows its pages as tabs at the top of each of those pages (_SectionTabs), so every feature is still one
/// click away once you're in the right place.
/// </summary>
public static class CustomerNav
{
    /// <summary>One page. Matches the current request when the controller matches and (if given) the action does too.</summary>
    public record Page(string Label, string Icon, string Controller, string Action, string? MatchAction = null)
    {
        public bool IsCurrent(string? controller, string? action) =>
            string.Equals(controller, Controller, StringComparison.OrdinalIgnoreCase)
            && (MatchAction == null || string.Equals(action, MatchAction, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A sidebar entry. The first page is where the entry goes; Pages are its tabs.</summary>
    public record Group(string Label, string Icon, Page[] Pages, Page[]? AlsoActiveOn = null)
    {
        public Page Home => Pages[0];
        public bool HasTabs => Pages.Length > 1;
        public bool IsCurrent(string? controller, string? action) =>
            Pages.Any(p => p.IsCurrent(controller, action)) || (AlsoActiveOn ?? Array.Empty<Page>()).Any(p => p.IsCurrent(controller, action));
    }

    public static readonly Group Home = new("Home", "bi-grid-1x2", new[] { new Page("Home", "bi-grid-1x2", "Dashboard", "Index") });

    public static readonly Group Accounts = new("Accounts", "bi-wallet2",
        new[] { new Page("Accounts", "bi-wallet2", "Accounts", "Index") },
        AlsoActiveOn: new[] { new Page("", "", "Transactions", "History", "History"), new Page("", "", "Cards", "Index") });

    public static readonly Group MoveMoney = new("Move money", "bi-arrow-left-right", new[]
    {
        new Page("Transfer", "bi-arrow-left-right", "Transactions", "Transfer", "Transfer"),
        new Page("Deposit", "bi-arrow-down-circle", "Transactions", "Deposit", "Deposit"),
        new Page("Withdraw", "bi-arrow-up-circle", "Transactions", "Withdraw", "Withdraw"),
        new Page("Deposit a check", "bi-camera", "Checks", "Index"),
    });

    public static readonly Group Pay = new("Pay & request", "bi-send", new[]
    {
        new Page("Send & request", "bi-send", "Payments", "Index"),
        new Page("Bill Pay", "bi-receipt", "BillPay", "Index"),
        new Page("Upcoming", "bi-calendar-week", "Upcoming", "Index"),
    });

    public static readonly Group Insights = new("Insights", "bi-pie-chart", new[]
    {
        new Page("Spending", "bi-pie-chart", "Insights", "Index"),
        new Page("Budgets", "bi-sliders", "Budgets", "Index"),
    });

    public static readonly Group Alerts = new("Alerts", "bi-bell", new[] { new Page("Alerts", "bi-bell", "Alerts", "Index") });
    public static readonly Group Profile = new("Profile", "bi-person", new[] { new Page("Profile", "bi-person", "Profile", "Index") });

    /// <summary>Sidebar order: everyday banking first, then settings.</summary>
    public static readonly Group[] Main = { Home, Accounts, MoveMoney, Pay, Insights };
    public static readonly Group[] Settings = { Alerts, Profile };

    /// <summary>The group the current page belongs to (for the section tabs), or null.</summary>
    public static Group? Current(string? controller, string? action) =>
        Main.Concat(Settings).FirstOrDefault(g => g.IsCurrent(controller, action));
}

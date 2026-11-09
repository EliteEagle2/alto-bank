using AltoBank.Models;

namespace AltoBank.ViewModels;

// Small models passed into partial views (Views/**/_*.cshtml).

/// <summary>
/// Settings for the shared _TransactionTable partial, so the Dashboard and the
/// History page can reuse one table but show different columns.
/// </summary>
public class TransactionTableViewModel
{
    public List<Transaction> Transactions { get; set; } = new();
    public bool ShowAccount { get; set; }          // Dashboard: which account it hit
    public bool ShowType { get; set; }             // History: Deposit / Withdrawal / ...
    public bool ShowRunningBalance { get; set; }   // History: balance after each row
    public string DateFormat { get; set; } = "MMM d";
    public string EmptyMessage { get; set; } = "No transactions yet.";
}

/// <summary>One product tile on the home page (rendered by Home/_FeatureTile).</summary>
public record FeatureTileViewModel(
    string? AnchorId, string Icon, string Title, string Text,
    string LinkText, string Controller, string Action);

/// <summary>Icon + message + optional button, rendered by Shared/_EmptyState.</summary>
public record EmptyStateViewModel(
    string Icon, string Message,
    string? ButtonText = null, string? Controller = null, string? Action = null);

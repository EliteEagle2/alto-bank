namespace AltoBank.Models;

/// <summary>
/// The debit card designs a customer can choose for a checking account.
/// The value is stored in Account.CardDesign; CssClass maps it to the card style in site.css.
/// </summary>
public static class CardDesigns
{
    public const string Aurora = "aurora"; // "Summit Gold": navy with a gold highlight (default). Keys are stored on accounts, so they never change.
    public const string Violet = "violet";
    public const string Onyx = "onyx";
    /// <summary>The Alto Peak credit card's own design (not offered for debit cards).</summary>
    public const string Peak = "peak";

    public static readonly string[] All = { Aurora, Violet, Onyx };

    public static string DisplayName(string? design) => design switch
    {
        Violet => "Glacier",
        Onyx => "Onyx",
        Peak => "Alto Peak",
        _ => "Summit Gold"
    };

    // CSS class added to .alto-card for each design ("" = the default Summit Gold).
    public static string CssClass(string? design) => design switch
    {
        Violet => "alto-card--violet",
        Onyx => "alto-card--onyx",
        Peak => "alto-card--peak",
        _ => ""
    };
}

namespace AltoBank.Models;

/// <summary>
/// The debit card designs a customer can choose for a checking account.
/// The value is stored in Account.CardDesign; CssClass maps it to the card style in site.css.
/// </summary>
public static class CardDesigns
{
    public const string Aurora = "aurora"; // cyan -> violet (default)
    public const string Violet = "violet";
    public const string Onyx = "onyx";

    public static readonly string[] All = { Aurora, Violet, Onyx };

    public static string DisplayName(string? design) => design switch
    {
        Violet => "Violet Nebula",
        Onyx => "Onyx Glow",
        _ => "Aurora"
    };

    // CSS class added to .alto-card for each design ("" = the default cyan/violet gradient).
    public static string CssClass(string? design) => design switch
    {
        Violet => "alto-card--violet",
        Onyx => "alto-card--onyx",
        _ => ""
    };
}

using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AltoBank.TagHelpers;

/// <summary>
/// CUSTOM TAG HELPER: &lt;money amount="..." /&gt;
/// Renders a decimal as US currency inside a &lt;span&gt;. Options:
///   colorize="true"  → green for positive, red for negative (credits vs. debits)
///   show-sign="true" → prefix "+" for positive amounts ("−" is always shown for negatives)
///
/// Examples:
///   &lt;money amount="@account.Balance" /&gt;                         →  $4,494.41
///   &lt;money amount="@t.SignedAmount" colorize="true" show-sign="true" /&gt;  →  +$250.00 (green)
///
/// It's registered for all views by "@addTagHelper *, AltoBank" in Views/_ViewImports.cshtml.
/// </summary>
[HtmlTargetElement("money", TagStructure = TagStructure.NormalOrSelfClosing)]
public class MoneyTagHelper : TagHelper
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public decimal Amount { get; set; }
    public bool Colorize { get; set; }
    public bool ShowSign { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Always dollars AND cents: money is never rounded for display.
        var text = Math.Abs(Amount).ToString("C2", Us);
        if (Amount < 0) text = "−" + text;          // proper minus sign
        else if (ShowSign && Amount > 0) text = "+" + text;

        var css = "money";
        if (Colorize && Amount > 0) css += " amount-credit";
        if (Colorize && Amount < 0) css += " amount-debit";

        // Keep any class the page added, e.g. <money class="fs-4" ...>
        var existing = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", string.IsNullOrEmpty(existing) ? css : $"{existing} {css}");
        output.Content.SetContent(text);
    }
}

using System.ComponentModel.DataAnnotations;

namespace AltoBank.ViewModels;

// Small models for the reusable partials on the public pages.

/// <summary>Top banner on every inner page (Shared/_PageHero).</summary>
public record PageHeroViewModel(
    string Eyebrow, string Title, string Subtitle,
    string? CtaText = null, string? CtaController = null, string? CtaAction = null);

/// <summary>Icon + title + text card (Shared/_BenefitCard).</summary>
public record BenefitViewModel(string Icon, string Title, string Text);

/// <summary>One big number with a label (Shared/_StatStrip).</summary>
public record StatViewModel(string Value, string Label);

/// <summary>One numbered step in a "How it works" list (Shared/_Steps).</summary>
public record StepViewModel(string Title, string Text);

/// <summary>Gradient call-to-action band (Shared/_CtaBand).</summary>
public record CtaViewModel(string Title, string Text, string ButtonText, string Controller, string Action);

/// <summary>CSS-drawn card graphic (Shared/_CardArt). Variant adds a color class; Nickname shows next to the chip.</summary>
public record CardArtViewModel(string Tier, string Variant = "", string Number = "0000  1234  5678  9010",   // marketing art: an obviously fake number
    string Holder = "Alex Sample", string? Nickname = null);   // and a placeholder name, never a real customer's

/// <summary>Contact Us form. Validation attributes give us server + client-side checks.</summary>
public class ContactViewModel
{
    public static readonly string[] Topics =
        { "General question", "Account help", "Report fraud", "Careers", "Press" };

    [Required, StringLength(80), Display(Name = "Your name")]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Topic { get; set; } = Topics[0];

    [Required, StringLength(1000, MinimumLength = 10, ErrorMessage = "Please write at least 10 characters.")]
    public string Message { get; set; } = string.Empty;
}

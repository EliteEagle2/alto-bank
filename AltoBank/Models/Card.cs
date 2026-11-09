using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltoBank.Models;

/// <summary>Debit cards come with checking accounts. Credit is reserved for a future feature.</summary>
public enum CardType
{
    Debit,
    Credit
}

/// <summary>
/// Active — usable. Frozen — temporarily turned off by the customer (can be unfrozen).
/// Cancelled — permanently off (lost, stolen, damaged, replaced, or account closed). Cards are never deleted.
/// </summary>
public enum CardStatus
{
    Active,
    Frozen,
    Cancelled
}

/// <summary>
/// A VIRTUAL card linked to an account. Numbers are fake (16 digits, valid Luhn checksum).
/// The CVV is NOT stored: it's calculated from the card number with a server secret
/// (see CardService.ComputeCvv) so the database never holds it.
/// </summary>
public class Card
{
    public int Id { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }

    public CardType Type { get; set; } = CardType.Debit;
    public CardStatus Status { get; set; } = CardStatus.Active;

    [Required, StringLength(16)]
    public string CardNumber { get; set; } = string.Empty;

    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }

    [Required, StringLength(60)]
    public string NameOnCard { get; set; } = string.Empty;

    // Color design (see CardDesigns), copied from the account when the card is issued.
    [StringLength(20)]
    public string? Design { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FrozenAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    [StringLength(40)]
    public string? CancelReason { get; set; }

    // When a card is replaced, the new card points back at the one it replaced.
    public int? ReplacesCardId { get; set; }

    [NotMapped] public string Last4 => CardNumber.Length >= 4 ? CardNumber[^4..] : CardNumber;
    [NotMapped] public string Expiry => $"{ExpiryMonth:D2}/{ExpiryYear % 100:D2}";
    [NotMapped] public string MaskedNumber => $"•••• •••• •••• {Last4}";
    [NotMapped] public string FormattedNumber =>
        CardNumber.Length == 16 ? $"{CardNumber[..4]} {CardNumber[4..8]} {CardNumber[8..12]} {CardNumber[12..]}" : CardNumber;
}

/// <summary>Reasons a customer can give when cancelling or replacing a card.</summary>
public static class CardCancelReasons
{
    public const string Lost = "Lost";
    public const string Stolen = "Stolen";
    public const string Damaged = "Damaged";
    public const string NoLongerNeeded = "No longer needed";
    public const string Replaced = "Replaced";
    public const string AccountClosed = "Account closed";

    // What a customer may choose (the others are set by the system).
    public static readonly string[] CustomerChoices = { Lost, Stolen, Damaged, NoLongerNeeded };
}

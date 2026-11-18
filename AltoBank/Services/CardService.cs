using System.Security.Cryptography;
using System.Text;
using AltoBank.Data;
using AltoBank.Models;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>
/// Virtual debit cards: issue, freeze/unfreeze, cancel, replace.
/// Every customer method checks the card belongs to the logged-in user (IDOR protection),
/// runs in a DB transaction, and writes an audit log entry.
/// </summary>
public interface ICardService
{
    /// <summary>Creates a new card for an account (adds it to the DbContext; caller saves).</summary>
    Task<Card> IssueAsync(Account account, string nameOnCard, string? design, int? replacesCardId = null);

    Task<List<Card>> GetCardsAsync(string userId, int accountId);
    Task<Card?> GetCardAsync(string userId, int cardId);

    Task<BankResult> FreezeAsync(string userId, int cardId);
    Task<BankResult> UnfreezeAsync(string userId, int cardId);
    Task<BankResult> CancelAsync(string userId, int cardId, string reason);
    Task<(BankResult Result, Card? NewCard)> ReplaceAsync(string userId, int cardId, string reason);

    /// <summary>CVV calculated from the card number + server secret (never stored).</summary>
    string ComputeCvv(Card card);
}

public class CardService : ICardService
{
    // "4410" = Alto Bank's made-up card prefix (matches the card art on the website).
    private const string Prefix = "4410";
    private const int YearsValid = 4;

    private readonly ApplicationDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly byte[] _cvvKey;

    public CardService(ApplicationDbContext db, IAuditLogger audit, IConfiguration config)
    {
        _db = db;
        _audit = audit;
        // Secret used to derive CVVs. In production it would live in a key vault, never in source control.
        _cvvKey = Encoding.UTF8.GetBytes(config["Cards:CvvSecret"] ?? "alto-demo-cvv-secret-change-me");
    }

    public async Task<Card> IssueAsync(Account account, string nameOnCard, string? design, int? replacesCardId = null)
    {
        var expires = DateTime.UtcNow.AddYears(YearsValid);
        var card = new Card
        {
            Account = account,
            AccountId = account.Id,
            Type = CardType.Debit,
            Status = CardStatus.Active,
            CardNumber = await GenerateNumberAsync(),
            ExpiryMonth = expires.Month,
            ExpiryYear = expires.Year,
            NameOnCard = nameOnCard.Trim().ToUpperInvariant(),
            Design = CardDesigns.All.Contains(design) ? design : CardDesigns.Aurora,
            IssuedAt = DateTime.UtcNow,
            ReplacesCardId = replacesCardId
        };
        _db.Cards.Add(card);
        return card;
    }

    // Ownership: cards are only returned when their account belongs to userId.
    public Task<List<Card>> GetCardsAsync(string userId, int accountId) =>
        _db.Cards.Where(c => c.AccountId == accountId && c.Account!.UserId == userId)
                 .OrderByDescending(c => c.IssuedAt).ToListAsync();

    public Task<Card?> GetCardAsync(string userId, int cardId) =>
        _db.Cards.Include(c => c.Account)
                 .FirstOrDefaultAsync(c => c.Id == cardId && c.Account!.UserId == userId);

    public Task<BankResult> FreezeAsync(string userId, int cardId) =>
        ChangeAsync(userId, cardId, "Card.Freeze", card =>
        {
            if (card.Status != CardStatus.Active) return Fail($"Only an active card can be frozen (this card is {card.Status}).");
            card.Status = CardStatus.Frozen;
            card.FrozenAt = DateTime.UtcNow;
            return Ok($"Card ending in {card.Last4} is frozen. Unfreeze it any time.");
        });

    public Task<BankResult> UnfreezeAsync(string userId, int cardId) =>
        ChangeAsync(userId, cardId, "Card.Unfreeze", card =>
        {
            if (card.Status != CardStatus.Frozen) return Fail("Only a frozen card can be unfrozen.");
            if (card.Account?.Status == AccountStatus.Closed) return Fail("This account is closed, so its card can't be turned back on.");
            card.Status = CardStatus.Active;
            card.FrozenAt = null;
            return Ok($"Card ending in {card.Last4} is active again.");
        });

    public Task<BankResult> CancelAsync(string userId, int cardId, string reason) =>
        ChangeAsync(userId, cardId, "Card.Cancel", card =>
        {
            if (!CardCancelReasons.CustomerChoices.Contains(reason)) return Fail("Choose a reason for cancelling.");
            if (card.Status == CardStatus.Cancelled) return Fail("This card is already cancelled.");
            Cancel(card, reason);
            return Ok($"Card ending in {card.Last4} is cancelled. You can request a new card any time.");
        }, reason);

    public async Task<(BankResult Result, Card? NewCard)> ReplaceAsync(string userId, int cardId, string reason)
    {
        if (!CardCancelReasons.CustomerChoices.Contains(reason) || reason == CardCancelReasons.NoLongerNeeded)
            return (Fail("Choose why you need a new card (lost, stolen, or damaged)."), null);

        await using var tx = await _db.Database.BeginTransactionAsync();
        var old = await GetCardAsync(userId, cardId);
        if (old?.Account == null) return (Fail("Card not found."), null);
        if (old.Account.Status == AccountStatus.Closed) return (Fail("This account is closed, so a new card can't be issued."), null);
        if (await _db.Cards.AnyAsync(c => c.ReplacesCardId == old.Id)) return (Fail("A replacement for this card was already issued."), null);
        // One usable card per account: never issue a second card while another one is still active or frozen.
        if (await _db.Cards.AnyAsync(c => c.AccountId == old.AccountId && c.Id != old.Id && c.Status != CardStatus.Cancelled))
            return (Fail("This account already has a card that is active or frozen. Cancel it first."), null);

        var before = Snapshot(old);
        if (old.Status != CardStatus.Cancelled) Cancel(old, reason);
        var user = await _db.Users.FirstAsync(u => u.Id == userId);
        var fresh = await IssueAsync(old.Account, $"{user.FirstName} {user.LastName}", old.Design, replacesCardId: old.Id);
        await _db.SaveChangesAsync();

        _audit.Add("Card.Replace", $"Replaced card ••{old.Last4} with ••{fresh.Last4} ({reason})",
                   "Card", old.Id.ToString(), before, new { old = Snapshot(old), newCard = Snapshot(fresh) }, reason);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return (Ok($"Card ending in {old.Last4} is cancelled. Your new card ends in {fresh.Last4} and is ready to use."), fresh);
    }

    /// <summary>
    /// CVV = 3 digits taken from an HMAC-SHA256 of the card number using a server secret.
    /// The same card always gets the same CVV, but nobody can work it out without the secret,
    /// and it is never saved in the database.
    /// </summary>
    public string ComputeCvv(Card card)
    {
        var hash = HMACSHA256.HashData(_cvvKey, Encoding.UTF8.GetBytes(card.CardNumber));
        var value = BitConverter.ToUInt32(hash, 0) % 1000;
        return value.ToString("D3");
    }

    // ---------- helpers ----------

    private static BankResult Ok(string m) => new(true, m);
    private static BankResult Fail(string m) => new(false, m);

    private static void Cancel(Card card, string reason)
    {
        card.Status = CardStatus.Cancelled;
        card.CancelledAt = DateTime.UtcNow;
        card.CancelReason = reason;
    }

    private static object Snapshot(Card c) => new { c.Id, last4 = c.Last4, status = c.Status.ToString(), c.CancelReason };

    // Load (with ownership check) → change → audit → save, all in one DB transaction.
    private async Task<BankResult> ChangeAsync(string userId, int cardId, string action, Func<Card, BankResult> change, string? reason = null)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var card = await GetCardAsync(userId, cardId);
        if (card == null) return Fail("Card not found.");
        var before = Snapshot(card);
        var result = change(card);
        if (!result.Success) return result;   // transaction rolls back on dispose
        _audit.Add(action, $"{action.Replace("Card.", "")} card ••{card.Last4}", "Card", card.Id.ToString(), before, Snapshot(card), reason);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return result;
    }

    // 16 digits: prefix + 11 random digits + Luhn check digit, re-rolled until unique.
    private async Task<string> GenerateNumberAsync()
    {
        string number;
        do
        {
            var body = Prefix + string.Concat(Enumerable.Range(0, 11).Select(_ => SeededRandom.Next(10)));   // fixed while seeding (Services/SeededRandom.cs)
            number = body + LuhnCheckDigit(body);
        } while (await _db.Cards.AnyAsync(c => c.CardNumber == number) || _db.Cards.Local.Any(c => c.CardNumber == number));
        return number;
    }

    /// <summary>The Luhn checksum digit that real card numbers end with.</summary>
    public static int LuhnCheckDigit(string digits)
    {
        int sum = 0;
        bool doubleIt = true;   // starting from the rightmost digit of the body
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int d = digits[i] - '0';
            if (doubleIt) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            doubleIt = !doubleIt;
        }
        return (10 - sum % 10) % 10;
    }
}

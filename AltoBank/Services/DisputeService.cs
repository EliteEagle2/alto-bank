using AltoBank.Data;
using AltoBank.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Services;

/// <summary>What the customer fills in on the dispute form (the website and the API both use it).</summary>
public record DisputeInput(int TransactionId, DisputeReason Reason, string? Description, decimal? ExpectedAmount,
                           int? DuplicateOfTransactionId, bool FreezeCard);

/// <summary>
/// DISPUTES, customer side. Submitting may post a provisional credit (through IBankService, in the same transaction as the
/// dispute). Deciding is staff-only (IAdminService). Every read is filtered by the signed-in customer, so another customer's
/// dispute, transaction or attachment is simply "not found".
/// </summary>
public interface IDisputeService
{
    public const int MinDescription = 20, MaxDescription = 1000;

    /// <summary>The customer's transaction if it can be disputed; otherwise null + why (or null + null if it isn't theirs).</summary>
    Task<(Transaction? Transaction, string? Error)> GetDisputableAsync(string userId, int transactionId);
    /// <summary>Earlier/later charges that look the same (same account, same description), for "Duplicate charge".</summary>
    Task<List<Transaction>> GetSimilarChargesAsync(string userId, Transaction t);
    /// <summary>Of these transactions, which ones can be disputed, and the status of any dispute on each.</summary>
    /// Partial = approved for less than the charge ("Wrong amount"), shown as "Partly refunded".
    Task<(HashSet<int> Eligible, Dictionary<int, DisputeStatus> Statuses, HashSet<int> Partial)> GetRowInfoAsync(string userId, IReadOnlyCollection<Transaction> transactions);
    Task<(BankResult Result, Dispute? Dispute)> SubmitAsync(string userId, DisputeInput input, IFormFile? attachment);
    Task<List<Dispute>> ListAsync(string userId);
    /// <summary>Which of the customer's accounts a transaction is on (null if it isn't theirs).</summary>
    Task<int?> GetAccountIdAsync(string userId, int transactionId);
    /// <summary>One of the customer's own transactions (e.g. the refund a dispute links to), or null.</summary>
    Task<Transaction?> GetOwnTransactionAsync(string userId, int transactionId);
    Task<Dispute?> GetAsync(string userId, int id);
    Task<(Stream Stream, string ContentType)?> OpenAttachmentForCustomerAsync(string userId, int id);
    Task<(Stream Stream, string ContentType)?> OpenAttachmentForStaffAsync(int id);
}

public class DisputeService : IDisputeService
{
    private readonly ApplicationDbContext _db;
    private readonly IBankService _bank;
    private readonly ICardService _cards;
    private readonly IFileStore _files;
    private readonly IAlertService _alerts;
    private readonly IAuditLogger _audit;

    public DisputeService(ApplicationDbContext db, IBankService bank, ICardService cards, IFileStore files, IAlertService alerts, IAuditLogger audit)
    {
        _db = db; _bank = bank; _cards = cards; _files = files; _alerts = alerts; _audit = audit;
    }

    public async Task<(Transaction? Transaction, string? Error)> GetDisputableAsync(string userId, int transactionId)
    {
        var t = await _db.Transactions.Include(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == transactionId && x.Account!.UserId == userId);   // ownership check
        if (t == null) return (null, null);
        var disputed = await _db.Disputes.AnyAsync(d => d.TransactionId == t.Id);
        var refunded = await _db.Transactions.AnyAsync(r => r.ReversalOfTransactionId == t.Id);
        return DisputeRules.WhyNot(t, disputed, refunded) is { } why ? (null, why) : (t, null);
    }

    public Task<List<Transaction>> GetSimilarChargesAsync(string userId, Transaction t)
    {
        var from = t.Date.AddDays(-30);
        var to = t.Date.AddDays(30);
        return _db.Transactions.AsNoTracking()
            .Where(x => x.AccountId == t.AccountId && x.Account!.UserId == userId && x.Id != t.Id && x.Type == t.Type
                        && x.Description == t.Description && x.Status == TransactionStatus.Posted && x.Date >= from && x.Date <= to)
            .OrderByDescending(x => x.Date).Take(10).ToListAsync();
    }

    public async Task<(HashSet<int> Eligible, Dictionary<int, DisputeStatus> Statuses, HashSet<int> Partial)> GetRowInfoAsync(string userId, IReadOnlyCollection<Transaction> transactions)
    {
        var ids = transactions.Select(t => t.Id).ToList();
        var disputes = await _db.Disputes.AsNoTracking()
            .Where(d => d.UserId == userId && ids.Contains(d.TransactionId))
            .Select(d => new { d.TransactionId, d.Status, d.RefundAmount }).ToListAsync();
        var statuses = disputes.ToDictionary(d => d.TransactionId, d => d.Status);
        var amounts = transactions.ToDictionary(t => t.Id, t => t.Amount);
        var partial = disputes.Where(d => d.Status == DisputeStatus.Approved && d.RefundAmount < amounts[d.TransactionId])
            .Select(d => d.TransactionId).ToHashSet();
        // The rows usually come without their Account loaded, so look up which of these accounts are closed.
        var accountIds = transactions.Select(t => t.AccountId).Distinct().ToList();
        var closed = (await _db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id) && a.Status == AccountStatus.Closed).Select(a => a.Id).ToListAsync()).ToHashSet();
        var refunded = (await _db.Transactions.AsNoTracking()
            .Where(r => r.ReversalOfTransactionId != null && ids.Contains(r.ReversalOfTransactionId.Value))
            .Select(r => r.ReversalOfTransactionId!.Value).ToListAsync()).ToHashSet();
        var eligible = transactions
            .Where(t => !closed.Contains(t.AccountId) && DisputeRules.WhyNot(t, statuses.ContainsKey(t.Id), refunded.Contains(t.Id)) == null)
            .Select(t => t.Id).ToHashSet();
        return (eligible, statuses, partial);
    }

    public async Task<(BankResult Result, Dispute? Dispute)> SubmitAsync(string userId, DisputeInput input, IFormFile? attachment)
    {
        // 1) Checks that don't need the database.
        if (!Enum.IsDefined(input.Reason)) return (new(false, "Choose why you're disputing this charge."), null);
        var description = input.Description?.Trim() ?? "";
        if (description.Length < IDisputeService.MinDescription)
            return (new(false, $"Tell us what happened in at least {IDisputeService.MinDescription} characters."), null);
        if (description.Length > IDisputeService.MaxDescription)
            return (new(false, $"Keep the description under {IDisputeService.MaxDescription} characters."), null);
        UploadedImage? file = null;
        if (attachment is { Length: > 0 })
        {
            var check = await _files.ReadImageAsync(attachment, "attachment");
            if (!check.Ok) return (new(false, check.Error!), null);
            file = check.Image;
        }

        // 2) Everything else in ONE transaction (on SQLite it takes the write lock straight away, so two submits on the
        //    same transaction run one after the other, and the unique index is the final guard).
        await using var tx = await _db.Database.BeginTransactionAsync();
        var (t, why) = await GetDisputableAsync(userId, input.TransactionId);
        if (t == null) return (new(false, why ?? "Transaction not found."), null);

        var refund = t.Amount;
        decimal? expected = null;
        int? duplicateOf = null;
        switch (input.Reason)
        {
            case DisputeReason.WrongAmount:
                if (input.ExpectedAmount is not decimal e || e <= 0)
                    return (new(false, "Enter the amount you expected to be charged."), null);
                if (decimal.Round(e, 2) != e) return (new(false, "The expected amount can have at most 2 decimal places."), null);
                if (e >= t.Amount)
                    return (new(false, $"The amount you expected has to be less than the {t.Amount:C} you were charged."), null);
                expected = e;
                refund = t.Amount - e;   // approval refunds only the difference
                break;
            case DisputeReason.Duplicate:
                var similar = await GetSimilarChargesAsync(userId, t);
                if (input.DuplicateOfTransactionId is not int dup || similar.All(s => s.Id != dup))
                    return (new(false, "Pick the original charge that this one duplicates."), null);
                duplicateOf = dup;
                break;
        }

        var settings = await _db.GetSettingsAsync();
        var dispute = new Dispute
        {
            UserId = userId, AccountId = t.AccountId, TransactionId = t.Id, Reason = input.Reason, Description = description,
            ExpectedAmount = expected, DuplicateOfTransactionId = duplicateOf, RefundAmount = refund,
            ProvisionalCreditEnabled = settings.ProvisionalCreditOnDisputes,   // captured now; later setting changes don't apply
            SubmittedAt = DateTime.UtcNow
        };
        _db.Disputes.Add(dispute);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            _db.ChangeTracker.Clear();
            return (new(false, "This transaction already has a dispute."), null);
        }
        // The file is written only once the dispute row exists (inside the same transaction), so a submit that loses
        // the race above never leaves a file behind.
        if (file != null) dispute.Attachment = await _files.SaveAsync(file);

        var account = t.Account!;
        _alerts.Add(userId, AlertType.DisputeUpdate, "Dispute submitted",
            $"We got your dispute about {t.Description} ({t.Amount:C}). We'll review it and tell you what we decide.", $"/disputes/{dispute.Id}");
        string provisional = "";
        if (dispute.ProvisionalCreditEnabled)
        {
            var (credited, credit) = await _bank.PostProvisionalCreditInCurrentTransactionAsync(dispute);
            if (credited.Success && credit != null)
            {
                dispute.ProvisionalCreditTransactionId = credit.Id;
                _alerts.Add(userId, AlertType.DisputeUpdate, "Provisional credit",
                    $"We added {refund:C} to {account.DisplayName} ••{account.Last4} while we review your dispute. If the dispute is denied, it will be taken back.",
                    $"/disputes/{dispute.Id}");
                provisional = $" We added a {refund:C} provisional credit while we review it.";
            }
        }
        _audit.Add("Dispute.Submit", $"Disputed transaction #{t.Id} ({t.Description}, {t.Amount:C}): {Dispute.LabelFor(input.Reason)}",
                   "Dispute", dispute.Id.ToString(),
                   after: new { transactionId = t.Id, reason = input.Reason.ToString(), refund, provisional = dispute.ProvisionalCreditEnabled, creditId = dispute.ProvisionalCreditTransactionId });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // 3) "I didn't authorize it": freeze the account's debit card too, if the customer asked (the existing card freeze).
        //    The dispute is already saved, so a problem here must not turn into an error page: say what happened instead.
        var frozen = "";
        if (input.Reason == DisputeReason.NotAuthorized && input.FreezeCard)
        {
            try
            {
                var card = (await _cards.GetCardsAsync(userId, t.AccountId)).FirstOrDefault(c => c.Status == CardStatus.Active);
                if (card == null) frozen = " There was no active debit card to freeze.";
                else frozen = (await _cards.FreezeAsync(userId, card.Id)).Success
                    ? $" Your debit card ••{card.Last4} is frozen."
                    : $" We couldn't freeze your debit card ••{card.Last4}. Please freeze it on the account's Card tab.";
            }
            catch (Exception)
            {
                frozen = " We couldn't freeze your debit card. Please freeze it on the account's Card tab.";
            }
        }
        return (new(true, $"Dispute submitted.{provisional}{frozen}"), dispute);
    }

    public Task<int?> GetAccountIdAsync(string userId, int transactionId) =>
        _db.Transactions.AsNoTracking().Where(t => t.Id == transactionId && t.Account!.UserId == userId)
            .Select(t => (int?)t.AccountId).FirstOrDefaultAsync();

    public Task<Transaction?> GetOwnTransactionAsync(string userId, int transactionId) =>
        _db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId && t.Account!.UserId == userId);

    public Task<List<Dispute>> ListAsync(string userId) =>
        _db.Disputes.AsNoTracking().Include(d => d.Transaction).Include(d => d.Account)
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.SubmittedAt).ThenByDescending(d => d.Id).ToListAsync();

    public Task<Dispute?> GetAsync(string userId, int id) =>
        _db.Disputes.AsNoTracking().Include(d => d.Transaction).Include(d => d.Account)
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

    public async Task<(Stream Stream, string ContentType)?> OpenAttachmentForCustomerAsync(string userId, int id) =>
        await _db.Disputes.AsNoTracking().Where(d => d.Id == id && d.UserId == userId).Select(d => d.Attachment).FirstOrDefaultAsync()
            is string name ? _files.Open(name) : null;

    public async Task<(Stream Stream, string ContentType)?> OpenAttachmentForStaffAsync(int id) =>
        await _db.Disputes.AsNoTracking().Where(d => d.Id == id).Select(d => d.Attachment).FirstOrDefaultAsync()
            is string name ? _files.Open(name) : null;
}

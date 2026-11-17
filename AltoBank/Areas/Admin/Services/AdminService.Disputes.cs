using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.Services;

public partial interface IAdminService
{
    // ----- Disputes: the review queue. Every decision runs in ExecuteAsync (one DB transaction + an AuditLog row);
    //       an admin can't decide a dispute on their own account (the self-service guard in ExecuteAsync). -----
    Task<DisputeListViewModel> GetDisputesPageAsync(DisputeListQuery query, int pageSize = 20);
    Task<DisputeDetailViewModel?> GetDisputeAsync(int id);
    Task<AdminResult> StartDisputeReviewAsync(int id, string? note);
    /// <summary>Refunds through the reversal system (the whole charge, or the difference for "Wrong amount"), or keeps
    /// the provisional credit as the refund. A P2P send is refunded to the sender only.</summary>
    Task<AdminResult> ApproveDisputeAsync(int id, string reason);
    /// <summary>Reason required (shown to the customer). Takes any provisional credit back, even into overdraft (decision 5).</summary>
    Task<AdminResult> DenyDisputeAsync(int id, string reason);
}

public partial class AdminService
{
    public async Task<DisputeListViewModel> GetDisputesPageAsync(DisputeListQuery query, int pageSize = 20)
    {
        var q = _db.Disputes.AsNoTracking().Include(d => d.User).Include(d => d.Account).Include(d => d.Transaction).AsQueryable();
        if (query.Status is DisputeStatus s) q = q.Where(d => d.Status == s);
        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var like = $"%{query.Customer.Trim()}%";
            q = q.Where(d => EF.Functions.Like(d.User!.FirstName + " " + d.User.LastName, like) || EF.Functions.Like(d.User!.Email!, like));
        }
        q = q.OrderBy(d => d.SubmittedAt).ThenBy(d => d.Id);   // oldest first: first come, first served
        var counts = await _db.Disputes.AsNoTracking().GroupBy(d => d.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        return new DisputeListViewModel { Query = query, Disputes = await PageAsync(q, query.Page, pageSize), Counts = counts };
    }

    public async Task<DisputeDetailViewModel?> GetDisputeAsync(int id)
    {
        var d = await _db.Disputes.AsNoTracking().Include(x => x.User).Include(x => x.Account).Include(x => x.Transaction)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return null;
        var t = d.Transaction!;
        var since = t.Date.AddDays(-90);
        async Task<Transaction?> Load(int? txId) => txId is int i ? await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == i) : null;
        return new DisputeDetailViewModel
        {
            Dispute = d,
            // The customer's recent activity with the same merchant / payee / person, on any of their accounts.
            MerchantHistory = await _db.Transactions.AsNoTracking().Include(x => x.Account)
                .Where(x => x.Account!.UserId == d.UserId && x.Date >= since
                            && (x.Description == t.Description || (t.CounterpartyName != null && x.CounterpartyName == t.CounterpartyName)))
                .OrderByDescending(x => x.Date).Take(12).ToListAsync(),
            OtherDisputes = await _db.Disputes.AsNoTracking().Include(x => x.Transaction)
                .Where(x => x.UserId == d.UserId && x.Id != d.Id).OrderByDescending(x => x.SubmittedAt).Take(10).ToListAsync(),
            DuplicateOf = await Load(d.DuplicateOfTransactionId),
            ProvisionalCredit = await Load(d.ProvisionalCreditTransactionId),
            Clawback = await Load(d.ClawbackTransactionId),
            Refund = await Load(d.RefundTransactionId),
            IsP2P = t.Type == TransactionType.PaymentSent
        };
    }

    public Task<AdminResult> StartDisputeReviewAsync(int id, string? note) =>
        ExecuteAsync(note, false, async () =>
        {
            var d = await _db.Disputes.Include(x => x.Transaction).FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return Fail("Dispute not found.");
            if (d.Status != DisputeStatus.Submitted) return Fail($"{d.Reference} is already {Dispute.LabelFor(d.Status).ToLowerInvariant()}.");
            d.Status = DisputeStatus.UnderReview;
            d.ReviewStartedAt = DateTime.UtcNow;
            d.ReviewerEmail = AdminEmail;
            _alerts.Add(d.UserId, AlertType.DisputeUpdate, "Dispute under review",
                $"We're reviewing your dispute about {d.Transaction!.Description} ({d.Transaction.Amount:C}).", $"/disputes/{d.Id}");
            _audit.Add("Dispute.StartReview", $"Started review of {d.Reference}", "Dispute", d.Id.ToString(),
                       new { status = "Submitted" }, new { status = "UnderReview" }, note);
            return Ok($"{d.Reference} is now under review. The customer was told.");
        });

    public Task<AdminResult> ApproveDisputeAsync(int id, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var d = await _db.Disputes.FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return Fail("Dispute not found.");
            if (!d.IsOpen) return Fail($"{d.Reference} was already {Dispute.LabelFor(d.Status).ToLowerInvariant()}. A dispute can only be decided once.");
            var original = await _db.Transactions.Include(t => t.Account).FirstAsync(t => t.Id == d.TransactionId);
            var full = d.RefundAmount == original.Amount;
            int refundId;

            if (d.ProvisionalCreditTransactionId is int creditId)
            {
                // The provisional credit becomes the refund: no second payment.
                refundId = creditId;
                if (full) { original.Status = TransactionStatus.Reversed; original.ReversedByTransactionId = creditId; }
            }
            else
            {
                if (original.Status != TransactionStatus.Posted || original.ReversedByTransactionId != null)
                    return Fail($"Transaction #{original.Id} was already reversed, so there's nothing left to refund.");
                if (original.Account!.Status == AccountStatus.Closed)
                    return Fail($"{original.Account.DisplayName} ••{original.Account.Last4} is closed. Reopen it before refunding.");
                // THE REVERSAL SYSTEM, for this leg only: a P2P send refunds the sender; the recipient's leg is never touched.
                // Described as a refund FROM ALTO, with no counterparty, so a P2P refund doesn't look like money from the recipient.
                var rev = PostReversalLeg(original, d.RefundAmount, $"Dispute {d.Reference}",
                                          description: $"Dispute refund: {original.Description}", keepCounterparty: false);
                if (full) original.Status = TransactionStatus.Reversed;
                await _db.SaveChangesAsync();
                if (full) original.ReversedByTransactionId = rev.Id;
                refundId = rev.Id;
            }

            var before = d.Status.ToString();
            d.Status = DisputeStatus.Approved;
            d.ReviewStartedAt ??= DateTime.UtcNow;
            d.DecidedAt = DateTime.UtcNow;
            d.ReviewerEmail = AdminEmail;
            d.DecisionReason = reason.Trim();
            d.RefundTransactionId = refundId;
            _alerts.Add(d.UserId, AlertType.DisputeUpdate, "Dispute approved",
                d.ProvisionalCreditTransactionId != null
                    ? $"Your dispute about {original.Description} was approved. The {d.RefundAmount:C} we credited earlier is yours to keep."
                    : $"Your dispute about {original.Description} was approved. {d.RefundAmount:C} was refunded to {original.Account!.DisplayName} ••{original.Account.Last4}.",
                $"/disputes/{d.Id}");
            _audit.Add("Dispute.Approve", $"Approved {d.Reference}: {d.RefundAmount:C} refunded" + (d.ProvisionalCreditTransactionId != null ? " (provisional credit made final)" : $" (reversal #{refundId})"),
                       "Dispute", d.Id.ToString(), new { status = before }, new { status = "Approved", refund = d.RefundAmount, refundTransactionId = refundId }, reason);
            return Ok($"{d.Reference} approved. {d.RefundAmount:C} refunded" + (d.ProvisionalCreditTransactionId != null ? " (the provisional credit is now final)." : "."));
        });

    public Task<AdminResult> DenyDisputeAsync(int id, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var d = await _db.Disputes.Include(x => x.Transaction).Include(x => x.Account).FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return Fail("Dispute not found.");
            if (!d.IsOpen) return Fail($"{d.Reference} was already {Dispute.LabelFor(d.Status).ToLowerInvariant()}. A dispute can only be decided once.");
            var before = d.Status.ToString();
            var reasonText = reason.Trim();
            var sentence = reasonText.EndsWith('.') || reasonText.EndsWith('!') || reasonText.EndsWith('?') ? reasonText : reasonText + ".";

            string clawbackNote = "";
            if (d.ProvisionalCreditTransactionId != null)
            {
                var (taken, clawback, overdrew) = await _bank.PostProvisionalClawbackInCurrentTransactionAsync(d);
                if (!taken.Success || clawback == null) return Fail($"Can't deny {d.Reference}: {taken.Message}");
                d.ClawbackTransactionId = clawback.Id;
                d.ClawbackOverdrew = overdrew;
                clawbackNote = $" The {d.RefundAmount:C} provisional credit was taken back" + (overdrew ? $", leaving the account overdrawn by {-clawback.BalanceAfter:C}." : ".");
                if (overdrew)
                    _audit.Add("Dispute.ClawbackOverdraft", $"{d.Reference}: taking back the provisional credit overdrew ••{d.Account!.Last4} by {-clawback.BalanceAfter:C}",
                               "Account", d.AccountId.ToString(), after: new { dispute = d.Id, balance = clawback.BalanceAfter }, reason: reason);
            }

            d.Status = DisputeStatus.Denied;
            d.ReviewStartedAt ??= DateTime.UtcNow;
            d.DecidedAt = DateTime.UtcNow;
            d.ReviewerEmail = AdminEmail;
            d.DecisionReason = reasonText;
            _alerts.Add(d.UserId, AlertType.DisputeUpdate, "Dispute denied",
                $"Your dispute about {d.Transaction!.Description} was denied: {sentence}" +
                (d.ClawbackTransactionId != null ? $" The {d.RefundAmount:C} provisional credit was taken back." : ""), $"/disputes/{d.Id}");
            if (d.ClawbackOverdrew)
                _alerts.Add(d.UserId, AlertType.DisputeUpdate, "Account overdrawn",
                    $"{d.Account!.DisplayName} ••{d.Account.Last4} is below $0 after the provisional credit was taken back. Please add money to bring it back up.",
                    $"/accounts/{d.AccountId}");
            _audit.Add("Dispute.Deny", $"Denied {d.Reference}" + (d.ClawbackTransactionId != null ? $" and took back the {d.RefundAmount:C} provisional credit" : ""),
                       "Dispute", d.Id.ToString(), new { status = before }, new { status = "Denied", clawback = d.ClawbackTransactionId, overdrew = d.ClawbackOverdrew }, reason);
            return Ok($"{d.Reference} denied. The customer will see your reason.{clawbackNote}");
        });
}

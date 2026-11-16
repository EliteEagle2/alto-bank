using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.Services;

public partial interface IAdminService
{
    // ----- Mobile check deposits: the review queue. Approve posts the money through IBankService; reject keeps the
    //       reason for the customer. Both run in ExecuteAsync (one DB transaction + an AuditLog row + a required reason). -----
    Task<CheckDepositListViewModel> GetCheckDepositsPageAsync(CheckDepositListQuery query, int pageSize = 20);
    Task<CheckDepositDetailViewModel?> GetCheckDepositAsync(int id);
    Task<AdminResult> ApproveCheckDepositAsync(int id, string reason);
    Task<AdminResult> RejectCheckDepositAsync(int id, string reason);
}

public partial class AdminService
{
    public async Task<CheckDepositListViewModel> GetCheckDepositsPageAsync(CheckDepositListQuery query, int pageSize = 20)
    {
        var q = _db.CheckDeposits.AsNoTracking().Include(d => d.User).Include(d => d.Account).AsQueryable();
        if (query.Status is CheckDepositStatus s) q = q.Where(d => d.Status == s);
        if (!string.IsNullOrWhiteSpace(query.Customer))
        {
            var like = $"%{query.Customer.Trim()}%";
            q = q.Where(d => EF.Functions.Like(d.User!.FirstName + " " + d.User.LastName, like) || EF.Functions.Like(d.User!.Email!, like));
        }
        // Pending: oldest first (first come, first served). Decided ones: newest first.
        q = query.Status == CheckDepositStatus.Pending
            ? q.OrderBy(d => d.SubmittedAt).ThenBy(d => d.Id)
            : q.OrderByDescending(d => d.SubmittedAt).ThenByDescending(d => d.Id);

        var counts = await _db.CheckDeposits.AsNoTracking().GroupBy(d => d.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        return new CheckDepositListViewModel
        {
            Query = query,
            Deposits = await PageAsync(q, query.Page, pageSize),
            Counts = counts
        };
    }

    public async Task<CheckDepositDetailViewModel?> GetCheckDepositAsync(int id)
    {
        var d = await _db.CheckDeposits.AsNoTracking().Include(x => x.User).Include(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return null;
        return new CheckDepositDetailViewModel
        {
            Deposit = d,
            // Same front image elsewhere (any customer, any status): a possible duplicate or re-used photo.
            Duplicates = await _db.CheckDeposits.AsNoTracking().Include(x => x.User)
                .Where(x => x.FrontHash == d.FrontHash && x.Id != d.Id)
                .OrderBy(x => x.SubmittedAt).ToListAsync(),
            History = await _db.CheckDeposits.AsNoTracking().Include(x => x.Account)
                .Where(x => x.UserId == d.UserId && x.Id != d.Id)
                .OrderByDescending(x => x.SubmittedAt).Take(10).ToListAsync(),
            Transaction = d.TransactionId is int tid ? await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid) : null
        };
    }

    public Task<AdminResult> ApproveCheckDepositAsync(int id, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var d = await _db.CheckDeposits.Include(x => x.Account).FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return Fail("Check deposit not found.");
            if (d.Status != CheckDepositStatus.Pending)
                return Fail($"{d.Reference} was already {d.Status.ToString().ToLowerInvariant()}. A check can only be decided once.");

            // The money goes through IBankService, inside THIS transaction (frozen/closed accounts are refused there).
            var (posted, t) = await _bank.PostCheckDepositInCurrentTransactionAsync(d);
            if (!posted.Success || t == null) return Fail($"Can't approve {d.Reference}: {posted.Message}");

            d.Status = CheckDepositStatus.Approved;
            d.ReviewedAt = DateTime.UtcNow;
            d.ReviewerEmail = AdminEmail;
            d.TransactionId = t.Id;
            _alerts.Add(d.UserId, AlertType.CheckApproved, "Check deposited",
                $"Your {d.Amount:C} check was approved and added to {d.Account!.DisplayName} ••{d.Account.Last4}.", $"/checks/{d.Id}");
            _audit.Add("CheckDeposit.Approve", $"Approved {d.Reference}: {d.Amount:C} posted to ••{d.Account.Last4}", "CheckDeposit", d.Id.ToString(),
                       new { status = "Pending" }, new { status = "Approved", transactionId = t.Id, amount = d.Amount }, reason);
            return Ok($"{d.Reference} approved. {d.Amount:C} posted to {d.Account.DisplayName} ••{d.Account.Last4}.");
        });

    public Task<AdminResult> RejectCheckDepositAsync(int id, string reason) =>
        ExecuteAsync(reason, true, async () =>
        {
            var d = await _db.CheckDeposits.Include(x => x.Account).FirstOrDefaultAsync(x => x.Id == id);
            if (d == null) return Fail("Check deposit not found.");
            if (d.Status != CheckDepositStatus.Pending)
                return Fail($"{d.Reference} was already {d.Status.ToString().ToLowerInvariant()}. A check can only be decided once.");

            d.Status = CheckDepositStatus.Rejected;
            d.ReviewedAt = DateTime.UtcNow;
            d.ReviewerEmail = AdminEmail;
            d.RejectReason = reason.Trim();   // shown to the customer, so the modal asks for it in their words
            _alerts.Add(d.UserId, AlertType.CheckRejected, "Check not accepted",
                $"Your {d.Amount:C} check for {d.Account!.DisplayName} wasn't accepted: {d.RejectReason} No money was added.", $"/checks/{d.Id}");
            _audit.Add("CheckDeposit.Reject", $"Rejected {d.Reference} ({d.Amount:C})", "CheckDeposit", d.Id.ToString(),
                       new { status = "Pending" }, new { status = "Rejected" }, reason);
            return Ok($"{d.Reference} rejected. The customer will see your reason.");
        });
}

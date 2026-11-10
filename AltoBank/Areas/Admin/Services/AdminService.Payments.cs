using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.EntityFrameworkCore;

namespace AltoBank.Areas.Admin.Services;

public partial interface IAdminService
{
    // ----- P2P payments (Send & Request): read-only for admins. A payment is undone with the normal
    //       transaction reversal (Transactions → Reverse), which also marks the payment Reversed. -----
    Task<PaymentAdminViewModel> GetPaymentsPageAsync(PaymentListQuery query, int pageSize = 25);
}

public partial class AdminService
{
    public async Task<PaymentAdminViewModel> GetPaymentsPageAsync(PaymentListQuery query, int pageSize = 25)
    {
        var vm = new PaymentAdminViewModel { Query = query };
        DateTime? start = query.From is DateTime f ? BankTime.StartOfDayUtc(f) : null;
        DateTime? end = query.To is DateTime t ? BankTime.StartOfDayUtc(t.AddDays(1)) : null;
        var like = string.IsNullOrWhiteSpace(query.Customer) ? null : $"%{query.Customer.Trim()}%";

        if (query.IsRequests)
        {
            var q = _db.MoneyRequests.AsNoTracking().Include(r => r.Requester).Include(r => r.Payer).AsQueryable();
            // A Pending request past its ExpiresAt counts as Expired even if the scheduler hasn't marked it yet.
            var now0 = DateTime.UtcNow;
            if (query.RequestStatus is MoneyRequestStatus rs)
                q = rs switch
                {
                    MoneyRequestStatus.Expired => q.Where(r => r.Status == MoneyRequestStatus.Expired || (r.Status == MoneyRequestStatus.Pending && r.ExpiresAt <= now0)),
                    MoneyRequestStatus.Pending => q.Where(r => r.Status == MoneyRequestStatus.Pending && r.ExpiresAt > now0),
                    _ => q.Where(r => r.Status == rs)
                };
            if (start != null) q = q.Where(r => r.CreatedAt >= start);
            if (end != null) q = q.Where(r => r.CreatedAt < end);
            if (like != null)
                q = q.Where(r => EF.Functions.Like(r.Requester!.FirstName + " " + r.Requester.LastName, like) || EF.Functions.Like(r.Requester!.Email!, like)
                              || EF.Functions.Like(r.Payer!.FirstName + " " + r.Payer.LastName, like) || EF.Functions.Like(r.Payer!.Email!, like));
            vm.Requests = await PageAsync(q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id), query.Page, pageSize);
        }
        else
        {
            var q = _db.Payments.AsNoTracking().Include(p => p.Sender).Include(p => p.Recipient)
                .Include(p => p.FromAccount).Include(p => p.ToAccount).AsQueryable();
            if (query.PaymentStatus is PaymentStatus ps) q = q.Where(p => p.Status == ps);
            if (start != null) q = q.Where(p => p.CreatedAt >= start);
            if (end != null) q = q.Where(p => p.CreatedAt < end);
            if (like != null)
                q = q.Where(p => EF.Functions.Like(p.Sender!.FirstName + " " + p.Sender.LastName, like) || EF.Functions.Like(p.Sender!.Email!, like)
                              || EF.Functions.Like(p.Recipient!.FirstName + " " + p.Recipient.LastName, like) || EF.Functions.Like(p.Recipient!.Email!, like));
            vm.Payments = await PageAsync(q.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id), query.Page, pageSize);
        }

        var since30 = BankTime.StartOfDayUtc(BankTime.Today.AddDays(-29));
        vm.PaymentsToday = await _db.Payments.CountAsync(p => p.CreatedAt >= BankTime.StartOfTodayUtc);
        vm.VolumeLast30Days = (await _db.Payments.Where(p => p.CreatedAt >= since30 && p.Status == PaymentStatus.Completed)
                                                .Select(p => p.Amount).ToListAsync()).Sum();   // SQLite can't SUM decimals
        var now = DateTime.UtcNow;
        vm.PendingRequests = await _db.MoneyRequests.CountAsync(r => r.Status == MoneyRequestStatus.Pending && r.ExpiresAt > now);
        vm.ExpiredRequests = await _db.MoneyRequests.CountAsync(r => r.Status == MoneyRequestStatus.Expired || (r.Status == MoneyRequestStatus.Pending && r.ExpiresAt <= now));
        return vm;
    }
}

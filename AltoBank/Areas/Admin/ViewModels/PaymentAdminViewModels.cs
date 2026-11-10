using System.ComponentModel.DataAnnotations;
using AltoBank.Models;

namespace AltoBank.Areas.Admin.ViewModels;

/// <summary>Filters for /admin/payments. View = "payments" (money sent) or "requests".</summary>
public class PaymentListQuery
{
    public string View { get; set; } = "payments";
    public string? Customer { get; set; }                 // name or email, sender OR recipient
    public PaymentStatus? PaymentStatus { get; set; }
    public MoneyRequestStatus? RequestStatus { get; set; }
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public bool IsRequests => string.Equals(View, "requests", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The P2P page: one list is filled depending on the View filter, plus totals for the tiles.</summary>
public class PaymentAdminViewModel
{
    public PaymentListQuery Query { get; set; } = new();
    public PagedList<Payment> Payments { get; set; } = new();
    public PagedList<MoneyRequest> Requests { get; set; } = new();
    public int PaymentsToday { get; set; }
    public decimal VolumeLast30Days { get; set; }
    public int PendingRequests { get; set; }
    public int ExpiredRequests { get; set; }
}

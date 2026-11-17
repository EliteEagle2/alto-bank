using AltoBank.Models;

namespace AltoBank.Areas.Admin.ViewModels;

public class DisputeListQuery
{
    public DisputeStatus? Status { get; set; }
    public string? Customer { get; set; }
    public int Page { get; set; } = 1;
}

/// <summary>/admin/disputes: one page of the queue (oldest first) + a count per status for the filter tabs.</summary>
public class DisputeListViewModel
{
    public DisputeListQuery Query { get; set; } = new();
    public PagedList<Dispute> Disputes { get; set; } = new();
    public Dictionary<DisputeStatus, int> Counts { get; set; } = new();
}

/// <summary>/admin/disputes/details/{id}: the dispute, the money around it, and the customer's history.</summary>
public class DisputeDetailViewModel
{
    public Dispute Dispute { get; set; } = null!;
    public List<Transaction> MerchantHistory { get; set; } = new();   // the customer's activity with the same merchant / payee / person
    public List<Dispute> OtherDisputes { get; set; } = new();
    public Transaction? DuplicateOf { get; set; }
    public Transaction? ProvisionalCredit { get; set; }
    public Transaction? Clawback { get; set; }
    public Transaction? Refund { get; set; }
    public bool IsP2P { get; set; }
}

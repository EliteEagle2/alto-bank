using AltoBank.Models;

namespace AltoBank.Areas.Admin.ViewModels;

public class CheckDepositListQuery
{
    public CheckDepositStatus? Status { get; set; }
    public string? Customer { get; set; }
    public int Page { get; set; } = 1;
}

/// <summary>/admin/checkdeposits: one page of the queue + a count per status for the filter tabs.</summary>
public class CheckDepositListViewModel
{
    public CheckDepositListQuery Query { get; set; } = new();
    public PagedList<CheckDeposit> Deposits { get; set; } = new();
    public Dictionary<CheckDepositStatus, int> Counts { get; set; } = new();
}

/// <summary>/admin/checkdeposits/details/{id}: the check, possible duplicates, and the customer's recent deposits.</summary>
public class CheckDepositDetailViewModel
{
    public CheckDeposit Deposit { get; set; } = null!;
    public List<CheckDeposit> Duplicates { get; set; } = new();   // same front image (SHA-256), other deposits
    public List<CheckDeposit> History { get; set; } = new();      // this customer's other check deposits
    public Transaction? Transaction { get; set; }                 // the posted deposit, once approved

    /// <summary>Why it can't be approved right now (frozen/closed account), or null.</summary>
    public string? CannotApproveReason => Deposit.Account?.Status switch
    {
        AccountStatus.Frozen => $"{Deposit.Account.DisplayName} ••{Deposit.Account.Last4} is frozen. Unfreeze it first, or reject the check.",
        AccountStatus.Closed => $"{Deposit.Account.DisplayName} ••{Deposit.Account.Last4} is closed. Reject the check instead.",
        _ => null
    };
}

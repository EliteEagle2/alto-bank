using AltoBank.Models;
using AltoBank.Services;

namespace AltoBank.Areas.Admin.ViewModels;

/// <summary>/admin/loans filters. Status: "active", "overdue", "paidoff" (empty = all).</summary>
public class LoanListQuery
{
    public string? Status { get; set; }
    public string? Customer { get; set; }
    public int Page { get; set; } = 1;
}

/// <summary>/admin/loans: every loan with where it stands.</summary>
public class LoanListViewModel
{
    public LoanListQuery Query { get; set; } = new();
    public PagedList<LoanView> Loans { get; set; } = new();
    public Dictionary<string, int> Counts { get; set; } = new();   // active / overdue / paidoff
    public int PendingApplications { get; set; }
    public int OpenOffers { get; set; }
}

/// <summary>/admin/loans/details/{id}: the loan, its schedule, payments, fees and recent activity.</summary>
public class LoanDetailViewModel
{
    public LoanView View { get; set; } = null!;
    public ApplicationUser Owner { get; set; } = null!;
    public AccountApplication? Application { get; set; }
    public Account? DisbursedTo { get; set; }
    public Account? AutopayFrom { get; set; }
    public List<Transaction> LateFees { get; set; } = new();
    public List<Transaction> Recent { get; set; } = new();
}

using AltoBank.Models;

namespace AltoBank.Areas.Admin.ViewModels;

public class CreditCardListQuery
{
    public AccountStatus? Status { get; set; }
    public string? Customer { get; set; }
    public int Page { get; set; } = 1;
}

/// <summary>/admin/creditcards: every Alto Peak card with its latest statement.</summary>
public class CreditCardListViewModel
{
    public CreditCardListQuery Query { get; set; } = new();
    public PagedList<Account> Cards { get; set; } = new();
    public Dictionary<int, CreditCardStatement> LatestStatements { get; set; } = new();
    public int PendingApplications { get; set; }
}

/// <summary>/admin/creditcards/details/{id}: the card, its statements and recent activity.</summary>
public class CreditCardDetailViewModel
{
    public Account Card { get; set; } = null!;
    public List<CreditCardStatement> Statements { get; set; } = new();
    public List<Transaction> Recent { get; set; } = new();
    public AccountApplication? Application { get; set; }
    public Account? AutopayFrom { get; set; }
}

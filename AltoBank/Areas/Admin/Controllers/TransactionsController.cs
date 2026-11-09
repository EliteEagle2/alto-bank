using System.Text;
using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>/admin/transactions — search/filter every transaction, export CSV, view details, reverse.</summary>
public class TransactionsController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public TransactionsController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] TransactionListQuery query)
    {
        ViewData["Title"] = "Transactions";
        ViewBag.Query = query;
        return View(await _admin.SearchTransactionsAsync(query));
    }

    // GET /admin/transactions/export?...same filters... → CSV download
    public async Task<IActionResult> Export([FromQuery] TransactionListQuery query)
    {
        var rows = await _admin.ExportTransactionsAsync(query);
        var csv = Csv.Build(
            new[] { "Id", "Date (UTC)", "Customer", "Email", "Account", "Type", "Status", "Description", "Memo", "Amount", "Signed amount", "Balance after", "Reversal of", "Reversed by" },
            rows.Select(t => new object?[]
            {
                t.Id, t.Date, $"{t.Account?.User?.FirstName} {t.Account?.User?.LastName}", t.Account?.User?.Email,
                $"{t.Account?.DisplayName} ••{t.Account?.Last4}", t.TypeLabel, t.Status.ToString(), t.Description, t.Memo,
                t.Amount, t.SignedAmount, t.BalanceAfter, t.ReversalOfTransactionId, t.ReversedByTransactionId
            }));
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"alto-transactions-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    public async Task<IActionResult> Details(int id)
    {
        var vm = await _admin.GetTransactionAsync(id);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"Transaction #{id}";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Reverse(int id, string reason)
    {
        Flash(await _admin.ReverseTransactionAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }
}

using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin/creditcards — every Alto Peak card (list + detail with statements), and the staff actions through IAdminService:
/// change the credit limit, waive a late fee, close a card at $0. Each needs a reason and writes an audit row.
/// Pending card applications are reviewed in the existing Applications queue.
/// </summary>
public class CreditCardsController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public CreditCardsController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] CreditCardListQuery query)
    {
        ViewData["Title"] = "Credit Cards";
        return View(await _admin.GetCreditCardsPageAsync(query));
    }

    public async Task<IActionResult> Details(int id)
    {
        var vm = await _admin.GetCreditCardAsync(id);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"{vm.Card.DisplayName} ••{vm.Card.Last4}";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> ChangeLimit(int id, decimal? limit, string reason)
    {
        Flash(limit is decimal l ? await _admin.ChangeCreditLimitAsync(id, l, reason) : new AdminResult(false, "Enter the new credit limit."));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> WaiveFee(int id, int statementId, string reason)
    {
        Flash(await _admin.WaiveLateFeeAsync(statementId, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RefundCredit(int id, int? moveTo, string reason)
    {
        Flash(moveTo is int to ? await _admin.RefundCreditBalanceAsync(id, to, reason) : new AdminResult(false, "Choose the account to refund to."));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Close(int id, string reason)
    {
        Flash(await _admin.CloseCreditCardAsync(id, reason));
        return RedirectToAction(nameof(Details), new { id });
    }
}

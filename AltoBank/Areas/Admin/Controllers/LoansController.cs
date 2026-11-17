using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin/loans — every personal and auto loan (filters: Active, Overdue, Paid off) and a detail page with the schedule,
/// payment history and late fees. Staff actions go through IAdminService (a reason + an audit row each): waive a late
/// fee, post an adjustment to the principal. Pending loan applications are decided in Applications (approve as offered,
/// counter-offer, deny).
/// </summary>
public class LoansController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public LoansController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] LoanListQuery query)
    {
        ViewData["Title"] = "Loans";
        return View(await _admin.GetLoansPageAsync(query));
    }

    public async Task<IActionResult> Details(int id)
    {
        var vm = await _admin.GetLoanAsync(id);
        if (vm == null) return NotFound();
        ViewData["Title"] = $"{vm.View.Account.DisplayName} ••{vm.View.Account.Last4}";
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> WaiveFee(int id, int feeTransactionId, string reason)
    {
        Flash(await _admin.WaiveLoanLateFeeAsync(id, feeTransactionId, reason));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Adjust(int id, string? direction, decimal? amount, string reason)
    {
        Flash(amount is not decimal a ? new AdminResult(false, "Enter the adjustment amount.")
            : direction is not ("credit" or "debit") ? new AdminResult(false, "Choose whether the adjustment lowers or raises the principal.")
            : await _admin.PostLoanAdjustmentAsync(id, direction == "credit", a, reason));
        return RedirectToAction(nameof(Details), new { id });
    }
}

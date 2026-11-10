using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>/admin/payments — every P2P payment and money request (Send &amp; Request), with filters. Read-only.</summary>
public class PaymentsController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public PaymentsController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] PaymentListQuery query)
    {
        ViewData["Title"] = "P2P Payments";
        return View(await _admin.GetPaymentsPageAsync(query));
    }
}

using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>
/// /admin/scheduled — every scheduled bill payment and transfer (with a failures view), the scheduler's last run,
/// and "Run scheduler now" (reason required, audited through IAdminService).
/// </summary>
public class ScheduledController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public ScheduledController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] ScheduledListQuery query)
    {
        ViewData["Title"] = "Scheduled Payments";
        return View(await _admin.GetScheduledPageAsync(query));
    }

    [HttpPost]
    public async Task<IActionResult> RunNow(string reason)
    {
        var (result, _) = await _admin.RunSchedulerNowAsync(reason);
        Flash(result);
        return RedirectToAction(nameof(Index));
    }
}

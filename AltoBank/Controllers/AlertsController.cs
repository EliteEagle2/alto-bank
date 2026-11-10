using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// ALERTS CENTER — /alerts (the bell in the top bar links here).
///   GET  /alerts               all alerts (or ?show=unread)
///   POST /alerts/{id}/read     mark one read, then go to its link (if any)
///   POST /alerts/read-all      mark every alert read
/// IAlertService filters by the signed-in user, so another customer's alert id is simply "not found".
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("alerts")]
public class AlertsController : Controller
{
    private readonly IAlertService _alerts;
    private readonly UserManager<ApplicationUser> _userManager;

    public AlertsController(IAlertService alerts, UserManager<ApplicationUser> userManager)
    {
        _alerts = alerts;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? show)
    {
        ViewData["Title"] = "Alerts";
        ViewBag.UnreadOnly = show == "unread";
        ViewBag.UnreadCount = await _alerts.UnreadCountAsync(UserId);
        return View(await _alerts.ListAsync(UserId, unreadOnly: show == "unread"));
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> Read(int id, bool open = false)
    {
        var alert = await _alerts.MarkReadAsync(UserId, id);
        if (alert == null) return NotFound();
        if (open && alert.Link is string link && Url.IsLocalUrl(link)) return LocalRedirect(link);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll()
    {
        var n = await _alerts.MarkAllReadAsync(UserId);
        TempData["Success"] = n == 0 ? "You're all caught up." : $"Marked {n} alert{(n == 1 ? "" : "s")} as read.";
        return RedirectToAction(nameof(Index));
    }
}

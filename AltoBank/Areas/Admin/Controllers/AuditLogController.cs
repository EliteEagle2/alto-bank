using System.Text;
using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers;

/// <summary>/admin/auditlog — read-only, filterable history of admin actions and key customer events.</summary>
public class AuditLogController : AdminControllerBase
{
    private readonly IAdminService _admin;

    public AuditLogController(IAdminService admin) => _admin = admin;

    public async Task<IActionResult> Index([FromQuery] AuditListQuery query)
    {
        ViewData["Title"] = "Audit Log";
        ViewBag.Query = query;
        ViewBag.Actions = await _admin.GetAuditActionsAsync();
        return View(await _admin.SearchAuditAsync(query));
    }

    public async Task<IActionResult> Export([FromQuery] AuditListQuery query)
    {
        var rows = await _admin.ExportAuditAsync(query);
        var csv = Csv.Build(
            new[] { "Id", "Time (UTC)", "Actor", "Role", "Action", "Target", "Summary", "Reason", "Before", "After", "IP" },
            rows.Select(l => new object?[]
            {
                l.Id, l.Timestamp, l.ActorEmail, l.ActorRole, l.Action, $"{l.TargetType} {l.TargetId}".Trim(),
                l.Summary, l.Reason, l.BeforeJson, l.AfterJson, l.IpAddress
            }));
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"alto-audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}

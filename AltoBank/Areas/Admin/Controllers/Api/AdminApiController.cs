using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers.Api;

/// <summary>
/// ADMIN JSON API (/api/admin/...). Admins only: 401 when logged out, 403 for customers.
/// Same Identity cookie as the website; responses are DTOs. Listed in Swagger.
/// </summary>
[ApiController]
[Area("Admin")]
[Route("api/admin")]
[Authorize(Roles = SeedData.AdminRole)]
[Produces("application/json")]
public class AdminApiController : ControllerBase
{
    private readonly IBankService _bank;

    public AdminApiController(IBankService bank) => _bank = bank;

    /// <summary>GET /api/admin/stats?days=30 — totals and chart data for the admin overview.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(StatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StatsDto>> Stats([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 7, 90);
        return await _bank.GetStatsAsync(days);
    }
}

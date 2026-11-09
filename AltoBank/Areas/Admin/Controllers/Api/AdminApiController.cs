using AltoBank.Areas.Admin.Services;
using AltoBank.Areas.Admin.ViewModels;
using AltoBank.Data;
using AltoBank.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Areas.Admin.Controllers.Api;

/// <summary>
/// ADMIN JSON API (/api/admin/...). Admins only: 401 when logged out, 403 for customers.
/// Same Identity cookie as the website; responses are DTOs (never EF entities). Listed in Swagger.
/// Freeze/unfreeze go through IAdminService, exactly like the admin pages (transaction + audit log).
/// </summary>
[ApiController]
[Area("Admin")]
[Route("api/admin")]
[Authorize(Roles = SeedData.AdminRole)]
[Produces("application/json")]
public class AdminApiController : ControllerBase
{
    private readonly IAdminService _admin;

    public AdminApiController(IAdminService admin) => _admin = admin;

    /// <summary>GET /api/admin/stats?days=30 — KPIs and chart series for the admin overview.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(AdminStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminStatsDto>> Stats([FromQuery] int days = 30) =>
        await _admin.GetStatsAsync(Math.Clamp(days, 7, 90));

    /// <summary>GET /api/admin/customers?search=jordan — customers matching a name or email (max 50).</summary>
    [HttpGet("customers")]
    [ProducesResponseType(typeof(List<AdminCustomerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AdminCustomerDto>>> Customers([FromQuery] string? search)
    {
        var page = await _admin.SearchCustomersAsync(new CustomerListQuery { Search = search, Sort = "name", Desc = false }, pageSize: 50);
        return page.Items.Select(c => new AdminCustomerDto(c.Id, c.Name, c.Email, c.AccountCount, c.TotalBalance, c.IsLocked, c.Joined)).ToList();
    }

    /// <summary>GET /api/admin/accounts/5 — one account with owner, status and limits.</summary>
    [HttpGet("accounts/{id:int}")]
    [ProducesResponseType(typeof(AdminAccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminAccountDto>> Account(int id)
    {
        var dto = await LoadDtoAsync(id);
        return dto == null ? NotFound(new ProblemDetails { Title = "Account not found." }) : dto;
    }

    /// <summary>POST /api/admin/accounts/5/freeze  body: { "reason": "Suspicious activity reported" }</summary>
    [HttpPost("accounts/{id:int}/freeze")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AdminActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminActionResultDto>> Freeze(int id, AdminReasonDto body) =>
        await Respond(id, await _admin.FreezeAccountAsync(id, body.Reason));

    /// <summary>POST /api/admin/accounts/5/unfreeze  body: { "reason": "Verified with customer" }</summary>
    [HttpPost("accounts/{id:int}/unfreeze")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AdminActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminActionResultDto>> Unfreeze(int id, AdminReasonDto body) =>
        await Respond(id, await _admin.UnfreezeAccountAsync(id, body.Reason));

    private async Task<ActionResult<AdminActionResultDto>> Respond(int id, AdminResult result)
    {
        var dto = new AdminActionResultDto(result.Success, result.Message, await LoadDtoAsync(id));
        return result.Success ? Ok(dto) : BadRequest(dto);
    }

    private async Task<AdminAccountDto?> LoadDtoAsync(int id)
    {
        var vm = await _admin.GetAccountAsync(id);
        if (vm == null) return null;
        var a = vm.Account;
        return new AdminAccountDto(a.Id, a.AccountNumber, a.AccountType.ToString(), a.Nickname, a.Status.ToString(), a.StatusReason,
            a.Balance, vm.DailyLimit, DateTime.SpecifyKind(a.OpenedAt, DateTimeKind.Utc),
            a.ClosedAt == null ? null : DateTime.SpecifyKind(a.ClosedAt.Value, DateTimeKind.Utc),
            a.UserId, $"{a.User?.FirstName} {a.User?.LastName}", a.User?.Email ?? "", vm.Transactions.Count);
    }
}

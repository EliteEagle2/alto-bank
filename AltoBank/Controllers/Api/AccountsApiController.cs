using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers.Api;

/// <summary>
/// Small JSON API inside the same MVC app.
///
///  • [ApiController] turns on API behaviors: attribute routing is required, invalid models
///    automatically return 400 with the validation errors, and [FromBody] is inferred.
///  • ControllerBase (not Controller) because an API returns data, never views.
///  • [Authorize] uses the SAME Identity login cookie as the website. Program.cs makes
///    unauthenticated /api requests get 401 instead of a redirect to the login page.
///  • Responses are DTOs (Dtos/ApiDtos.cs), never EF entities.
///
/// Route note: [Route("api/[controller]")] would produce "/api/accountsapi" (the class name
/// minus "Controller"), so the route is spelled out as "api/accounts". The class keeps the
/// "Api" suffix so it isn't confused with the MVC AccountsController.
///
/// CSRF note: these endpoints accept JSON only. Browsers can't send cross-site JSON with
/// cookies without a CORS preflight, and the Identity cookie is SameSite=Lax.
/// </summary>
[ApiController]
[Route("api/accounts")]
[Authorize(Roles = SeedData.CustomerRole)] // customers only; admins use /admin
[Produces("application/json")]
public class AccountsApiController : ControllerBase
{
    private const int PageSize = 10;

    private readonly IBankService _bank;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountsApiController(IBankService bank, UserManager<ApplicationUser> userManager)
    {
        _bank = bank;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    /// <summary>GET /api/accounts — the signed-in user's accounts.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<AccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<AccountDto>>> GetAccounts()
    {
        var accounts = await _bank.GetAccountsAsync(UserId);
        return accounts.Select(ToDto).ToList();
    }

    /// <summary>GET /api/accounts/5 — one account with its balance.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountDto>> GetAccount(int id)
    {
        var account = await _bank.GetAccountAsync(id, UserId);
        if (account == null) return NotFound(new ProblemDetails { Title = "Account not found." });
        return ToDto(account);
    }

    /// <summary>GET /api/accounts/5/transactions?page=1 — newest first, 10 per page.</summary>
    [HttpGet("{id:int}/transactions")]
    [ProducesResponseType(typeof(PagedResult<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<TransactionDto>>> GetTransactions(int id, [FromQuery] int page = 1)
    {
        if (page < 1) return BadRequest(new ProblemDetails { Title = "page must be 1 or greater." });

        var account = await _bank.GetAccountAsync(id, UserId);
        if (account == null) return NotFound(new ProblemDetails { Title = "Account not found." });

        var (items, total) = await _bank.GetTransactionsPageAsync(id, page, PageSize);
        return new PagedResult<TransactionDto>(
            items.Select(ToDto).ToList(), page, PageSize, total,
            TotalPages: (int)Math.Ceiling(total / (double)PageSize));
    }

    /// <summary>POST /api/transfers — move money between two of your accounts. 400 on any error.</summary>
    [HttpPost("/api/transfers")]   // leading "/" = absolute route, ignores the controller's "api/accounts" prefix
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TransferResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TransferResultDto>> Transfer(TransferRequestDto request)
    {
        // Invalid bodies (e.g. negative amount) never get here: [ApiController] returns 400 automatically.
        var result = await _bank.TransferAsync(UserId, request.FromAccountId, request.ToAccountId, request.Amount, request.Memo);
        if (!result.Success) return BadRequest(new ProblemDetails { Title = result.Message, Status = 400 });

        var from = await _bank.GetAccountAsync(request.FromAccountId, UserId);
        var to = await _bank.GetAccountAsync(request.ToAccountId, UserId);
        return new TransferResultDto(result.Message, ToDto(from!), ToDto(to!));
    }

    // ---------- Entity -> DTO mapping ----------

    private static AccountDto ToDto(Account a) =>
        new(a.Id, a.AccountType.ToString(), a.DisplayName, a.Nickname, $"••••{a.Last4}", a.Balance,
            DateTime.SpecifyKind(a.OpenedAt, DateTimeKind.Utc));

    private static TransactionDto ToDto(Transaction t) =>
        new(t.Id, DateTime.SpecifyKind(t.Date, DateTimeKind.Utc), t.Description, t.TypeLabel,
            t.Amount, t.SignedAmount, t.BalanceAfter);
}

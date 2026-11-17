using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers.Api;

/// <summary>
/// Small JSON API inside the same MVC app (customer side).
///
///  • [ApiController] turns on API behaviors: attribute routing is required, invalid models
///    automatically return 400 with the validation errors, and [FromBody] is inferred.
///  • ControllerBase (not Controller) because an API returns data, never views.
///  • [Authorize] uses the SAME Identity login cookie as the website. Program.cs makes
///    unauthenticated /api requests get 401 instead of a redirect to the login page.
///  • Responses are DTOs (Dtos/ApiDtos.cs), never EF entities.
///  • IDOR protection: every endpoint with an account/card id loads it WITH the caller's userId;
///    someone else's id returns 404.
///
/// Route note: [Route("api/[controller]")] would produce "/api/accountsapi" (the class name
/// minus "Controller"), so the route is spelled out as "api/accounts". The class keeps the
/// "Api" suffix so it isn't confused with the MVC AccountsController.
///
/// CSRF note: the POST endpoints accept JSON only. Browsers can't send cross-site JSON with
/// cookies without a CORS preflight, and the Identity cookie is SameSite=Lax.
/// </summary>
[ApiController]
[Route("api/accounts")]
[Authorize(Roles = SeedData.CustomerRole)] // customers only; admins use /admin
[Produces("application/json")]
public class AccountsApiController : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 50;

    private readonly IBankService _bank;
    private readonly ICardService _cards;
    private readonly IAuditLogger _audit;
    private readonly UserManager<ApplicationUser> _userManager;

    private readonly IDisputeService _disputes;

    public AccountsApiController(IBankService bank, ICardService cards, IAuditLogger audit, UserManager<ApplicationUser> userManager,
                                 IDisputeService disputes)
    {
        _disputes = disputes;
        _bank = bank;
        _cards = cards;
        _audit = audit;
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

    /// <summary>
    /// GET /api/accounts/5/transactions?page=1&amp;pageSize=20&amp;search=coffee&amp;kind=out&amp;from=2026-10-01&amp;to=2026-10-31
    /// Newest first. kind: in, out, deposit, withdrawal, transfer, interest, adjustment, reversal.
    /// Also returns money in / out for the WHOLE filtered range. Used by "Load more" on the Activity tab.
    /// </summary>
    [HttpGet("{id:int}/transactions")]
    [ProducesResponseType(typeof(ActivityPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityPageDto>> GetTransactions(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? search = null, [FromQuery] string? kind = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        if (page < 1) return BadRequest(new ProblemDetails { Title = "page must be 1 or greater." });
        if (pageSize is < 1 or > MaxPageSize) return BadRequest(new ProblemDetails { Title = $"pageSize must be between 1 and {MaxPageSize}." });

        var account = await _bank.GetAccountAsync(id, UserId);
        if (account == null) return NotFound(new ProblemDetails { Title = "Account not found." });

        var result = await _bank.GetActivityAsync(UserId, id, new ActivityFilter(search, kind, from, to), page, pageSize);
        var labels = await _bank.GetAccountLabelsAsync(UserId);
        var (disputable, statuses, partly) = await _disputes.GetRowInfoAsync(UserId, result.Items);
        return new ActivityPageDto(
            result.Items.Select(t => ToDto(t, labels) with
            {
                CanDispute = disputable.Contains(t.Id),
                DisputeStatus = statuses.TryGetValue(t.Id, out var st) ? st.ToString() : null,
                PartlyRefunded = partly.Contains(t.Id)
            }).ToList(), page, pageSize, result.TotalCount,
            HasMore: page * pageSize < result.TotalCount,
            result.MoneyIn, result.MoneyOut, result.MoneyIn - result.MoneyOut);
    }

    /// <summary>GET /api/accounts/5/number — the FULL account number (for the "Show" button). Owner only; audited.</summary>
    [HttpGet("{id:int}/number")]
    [ProducesResponseType(typeof(AccountNumberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountNumberDto>> GetAccountNumber(int id)
    {
        var account = await _bank.GetAccountAsync(id, UserId);
        if (account == null) return NotFound(new ProblemDetails { Title = "Account not found." });
        await _audit.WriteAsync("Account.RevealNumber", $"Viewed full account number for ••{account.Last4}", "Account", account.Id.ToString());
        return new AccountNumberDto(account.AccountNumber, BankService.RoutingNumber);
    }

    /// <summary>POST /api/accounts/5/nickname  body: { "nickname": "Rainy Day Fund" } — inline edit. Owner only; audited.</summary>
    [HttpPost("{id:int}/nickname")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountDto>> SetNickname(int id, NicknameDto body)
    {
        var result = await _bank.UpdateNicknameAsync(UserId, id, body.Nickname);
        if (!result.Success)
            return result.Message == "Account not found." ? NotFound(new ProblemDetails { Title = result.Message })
                                                          : BadRequest(new ProblemDetails { Title = result.Message, Status = 400 });
        return ToDto((await _bank.GetAccountAsync(id, UserId))!);
    }

    /// <summary>
    /// GET /api/accounts/5/cards/9/reveal — full card number, expiry and CVV (for the Card tab's "Show details").
    /// Owner only (the card must belong to this user AND this account). The CVV is computed, never stored. Audited.
    /// </summary>
    [HttpGet("{id:int}/cards/{cardId:int}/reveal")]
    [ProducesResponseType(typeof(CardDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CardDetailsDto>> RevealCard(int id, int cardId)
    {
        var card = await _cards.GetCardAsync(UserId, cardId);
        if (card == null || card.AccountId != id) return NotFound(new ProblemDetails { Title = "Card not found." });
        if (card.Status == CardStatus.Cancelled) return BadRequest(new ProblemDetails { Title = "This card is cancelled.", Status = 400 });
        await _audit.WriteAsync("Card.Reveal", $"Viewed card details for ••{card.Last4}", "Card", card.Id.ToString());
        return new CardDetailsDto(card.FormattedNumber, card.Expiry, _cards.ComputeCvv(card), card.NameOnCard);
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
            DateTime.SpecifyKind(a.OpenedAt, DateTimeKind.Utc), a.Status.ToString(),
            a.IsCreditCard ? a.CreditLimit : null, a.IsCreditCard ? a.AvailableCredit : null,
            a.IsCreditCard ? a.AmountOwed : null, a.IsCreditCard ? a.RewardsBalance : null);

    private static ActivityItemDto ToDto(Transaction t, IReadOnlyDictionary<int, string> labels) =>
        new(t.Id, DateTime.SpecifyKind(t.Date, DateTimeKind.Utc), t.Description, t.TypeLabel, t.Type.ToString(), t.IsCredit,
            t.Amount, t.SignedAmount, t.BalanceAfter, t.Status.ToString(), t.Memo,
            t.CounterpartyName ?? (t.CounterpartyAccountId is int c ? labels.GetValueOrDefault(c) : null),
            t.ReversalOfTransactionId, t.ReversedByTransactionId, BankTime.DateOf(t.Date),
            t.Category.ToString(), Categories.Label(t.Category), t.CategoryOverridden);
}

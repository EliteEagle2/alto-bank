using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers.Api;

/// <summary>
/// SEND &amp; REQUEST JSON API (customers only; same login cookie as the website).
///   GET  /api/payments/recipient?q=maya@altobank.com   who you'd be paying ("Maya C.")
///   POST /api/payments/send                            send money to another customer
///   POST /api/payments/request                         request money / split a bill
///   GET  /api/payments/requests                        requests asked of you (incoming) and by you (outgoing)
///   POST /api/payments/requests/{id}/pay|decline|cancel
/// Money rules live in IBankService and request rules in IPaymentRequestService, exactly as for the web pages.
/// A request you're not part of returns 404. POST bodies are JSON only (see the CSRF note in AccountsApiController).
/// </summary>
[ApiController]
[Route("api/payments")]
[Authorize(Roles = SeedData.CustomerRole)]
[Produces("application/json")]
public class PaymentsApiController : ControllerBase
{
    private readonly IBankService _bank;
    private readonly IPaymentRequestService _requests;
    private readonly UserManager<ApplicationUser> _userManager;

    public PaymentsApiController(IBankService bank, IPaymentRequestService requests, UserManager<ApplicationUser> userManager)
    {
        _bank = bank;
        _requests = requests;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    /// <summary>GET /api/payments/recipient?q= — the recipient preview. 404 if no eligible customer matches; 400 for yourself.</summary>
    [HttpGet("recipient")]
    [ProducesResponseType(typeof(RecipientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecipientDto>> Recipient([FromQuery] string? q)
    {
        var who = await _bank.FindRecipientAsync(UserId, q);
        if (who.Found) return new RecipientDto(who.ShortName!);
        return who.Error!.StartsWith("You can't send money to yourself")
            ? BadRequest(new ProblemDetails { Title = who.Error, Status = 400 })
            : NotFound(new ProblemDetails { Title = who.Error, Status = 404 });
    }

    /// <summary>POST /api/payments/send — 400 on frozen/closed account, daily limit, insufficient funds, unknown recipient.</summary>
    [HttpPost("send")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaymentDto>> Send(SendPaymentDto body)
    {
        var (result, payment) = await _bank.SendPaymentAsync(UserId, body.FromAccountId, body.To, body.Amount, body.Memo);
        if (!result.Success || payment == null) return Problem400(result.Message);
        return ToDto(payment, UserId);
    }

    /// <summary>POST /api/payments/request — creates one request per person (a split shares one group).</summary>
    [HttpPost("request")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(List<MoneyRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<MoneyRequestDto>>> RequestMoney(RequestMoneyDto body)
    {
        var input = new NewRequest(body.Recipients.Select(r => new RequestLine(r.To, r.Amount)).ToList(), body.Memo,
                                   body.SplitTotal, body.SplitTotal != null && body.SplitEvenly, body.IncludeMe);
        var (result, created) = await _requests.CreateAsync(UserId, input);
        if (!result.Success) return Problem400(result.Message);
        var list = new List<MoneyRequestDto>();
        foreach (var r in created) list.Add(ToDto((await _requests.GetForUserAsync(UserId, r.Id))!, UserId));
        return list;
    }

    /// <summary>GET /api/payments/requests — incoming (asked of you) and outgoing (you asked), newest first.</summary>
    [HttpGet("requests")]
    [ProducesResponseType(typeof(MoneyRequestListDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MoneyRequestListDto>> Requests()
    {
        await _requests.ExpireDueAsync();
        var o = await _requests.GetOverviewAsync(UserId);
        return new MoneyRequestListDto(
            o.Inbox.Concat(o.ClosedInbox).OrderByDescending(r => r.CreatedAt).Select(r => ToDto(r, UserId)).ToList(),
            o.MyRequests.Select(r => ToDto(r, UserId)).ToList());
    }

    /// <summary>POST /api/payments/requests/5/pay  body: { "fromAccountId": 1 }. Only the person asked can pay.</summary>
    [HttpPost("requests/{id:int}/pay")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentDto>> Pay(int id, PayRequestDto body)
    {
        if (await _requests.GetForUserAsync(UserId, id) is not { } r || r.PayerUserId != UserId)
            return NotFound(new ProblemDetails { Title = "Request not found.", Status = 404 });
        var (result, payment) = await _bank.PayRequestAsync(UserId, id, body.FromAccountId);
        if (!result.Success || payment == null) return Problem400(result.Message);
        return ToDto(payment, UserId);
    }

    /// <summary>POST /api/payments/requests/5/decline — only the person asked can decline.</summary>
    [HttpPost("requests/{id:int}/decline")]
    [ProducesResponseType(typeof(ActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ActionResultDto>> Decline(int id) =>
        Close(id, mustBePayer: true, () => _requests.DeclineAsync(UserId, id));

    /// <summary>POST /api/payments/requests/5/cancel — only the person who made the request can cancel.</summary>
    [HttpPost("requests/{id:int}/cancel")]
    [ProducesResponseType(typeof(ActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ActionResultDto>> Cancel(int id) =>
        Close(id, mustBePayer: false, () => _requests.CancelAsync(UserId, id));

    // Decline/cancel send no body. To keep them CSRF-safe they require a JSON content type (a cross-site
    // form can't send that without a CORS preflight), like the other API POSTs.
    private async Task<ActionResult<ActionResultDto>> Close(int id, bool mustBePayer, Func<Task<BankResult>> run)
    {
        if (!(Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new ProblemDetails { Title = "Send Content-Type: application/json.", Status = 415 });
        var r = await _requests.GetForUserAsync(UserId, id);
        if (r == null || (mustBePayer ? r.PayerUserId : r.RequesterUserId) != UserId)
            return NotFound(new ProblemDetails { Title = "Request not found.", Status = 404 });
        var result = await run();
        return result.Success ? new ActionResultDto(true, result.Message) : Problem400(result.Message);
    }

    private ActionResult Problem400(string message) => BadRequest(new ProblemDetails { Title = message, Status = 400 });

    // ---------- Entity -> DTO (only the other person's short name is ever exposed) ----------

    private static PaymentDto ToDto(Payment p, string me)
    {
        var sent = p.SenderUserId == me;
        var other = sent ? p.Recipient : p.Sender;
        return new PaymentDto(p.Id, sent ? "sent" : "received", other == null ? "" : BankService.ShortName(other), p.Amount, p.Memo,
            p.Status.ToString(), DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc), p.MoneyRequestId);
    }

    private static MoneyRequestDto ToDto(MoneyRequest r, string me)
    {
        var incoming = r.PayerUserId == me;
        var other = incoming ? r.Requester : r.Payer;
        var status = r.Status == MoneyRequestStatus.Pending && r.ExpiresAt <= DateTime.UtcNow ? "Expired" : r.Status.ToString();
        return new MoneyRequestDto(r.Id, incoming ? "incoming" : "outgoing", other == null ? "" : BankService.ShortName(other), r.Amount, r.Memo,
            status, DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), DateTime.SpecifyKind(r.ExpiresAt, DateTimeKind.Utc), r.SplitTotal, r.PaymentId);
    }
}

using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers.Api;

/// <summary>
/// DISPUTES JSON API.
///   POST /api/disputes        multipart/form-data (transactionId, reason, description, expectedAmount?, duplicateOfTransactionId?,
///                             freezeCard?, attachment?) → 201 with the dispute
///   GET  /api/disputes        your disputes, newest first
///   GET  /api/disputes/{id}   one of yours (another customer's id → 404)
/// Same rules as the website (IDisputeService). Like the check deposit upload, the multipart POST requires the
/// anti-forgery token (form field __RequestVerificationToken or header RequestVerificationToken).
/// </summary>
[ApiController]
[Route("api/disputes")]
[Authorize(Roles = SeedData.CustomerRole)]
[Produces("application/json")]
public class DisputesApiController : ControllerBase
{
    private readonly IDisputeService _disputes;
    private readonly UserManager<ApplicationUser> _userManager;

    public DisputesApiController(IDisputeService disputes, UserManager<ApplicationUser> userManager)
    {
        _disputes = disputes;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ChecksController.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ChecksController.MaxRequestBytes)]
    [ProducesResponseType(typeof(DisputeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DisputeDto>> Create([FromForm] DisputeUploadDto body)
    {
        var (t, why) = await _disputes.GetDisputableAsync(UserId, body.TransactionId);
        if (t == null && why == null) return NotFound(new ProblemDetails { Title = "Transaction not found.", Status = 404 });
        if (!Enum.TryParse<DisputeReason>(body.Reason, ignoreCase: true, out var reason) || !Enum.IsDefined(reason) || int.TryParse(body.Reason, out _))
            return BadRequest(new ProblemDetails { Title = "reason must be NotAuthorized, WrongAmount, NotReceived, Duplicate or Other.", Status = 400 });
        var (result, dispute) = await _disputes.SubmitAsync(UserId,
            new DisputeInput(body.TransactionId, reason, body.Description, body.ExpectedAmount, body.DuplicateOfTransactionId, body.FreezeCard),
            body.Attachment);
        if (!result.Success || dispute == null) return BadRequest(new ProblemDetails { Title = result.Message, Status = 400 });
        return CreatedAtAction(nameof(Get), new { id = dispute.Id }, ToDto((await _disputes.GetAsync(UserId, dispute.Id))!));
    }

    [HttpGet]
    public async Task<ActionResult<List<DisputeDto>>> List() => (await _disputes.ListAsync(UserId)).Select(ToDto).ToList();

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(DisputeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DisputeDto>> Get(int id)
    {
        var d = await _disputes.GetAsync(UserId, id);
        return d == null ? NotFound() : ToDto(d);
    }

    private static DateTime? Utc(DateTime? d) => d is DateTime x ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : null;

    private DisputeDto ToDto(Dispute d) => new(
        d.Id, d.Reference, d.TransactionId, d.Transaction?.Description ?? "", d.Transaction?.Amount ?? 0,
        d.Reason.ToString(), d.Status.ToString(), d.RefundAmount, d.ExpectedAmount, d.ProvisionalCreditEnabled,
        DateTime.SpecifyKind(d.SubmittedAt, DateTimeKind.Utc), Utc(d.ReviewStartedAt), Utc(d.DecidedAt), d.DecisionReason,
        d.RefundTransactionId, d.ProvisionalCreditTransactionId, d.ClawbackTransactionId,
        d.Attachment != null ? Url.Action("Attachment", "MyDisputes", new { id = d.Id }) : null);
}

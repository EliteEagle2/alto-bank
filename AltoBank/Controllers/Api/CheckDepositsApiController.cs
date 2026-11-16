using AltoBank.Data;
using AltoBank.Dtos;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers.Api;

/// <summary>
/// MOBILE CHECK DEPOSIT JSON API.
///   POST /api/checkdeposits        multipart/form-data (accountId, amount, front, back) → 201 with the pending deposit
///   GET  /api/checkdeposits        your check deposits, newest first
///   GET  /api/checkdeposits/{id}   one of yours (another customer's id → 404)
/// The same rules as the website apply (CheckDepositService): JPG/PNG by signature, 5 MB each, per-check and daily
/// limits, duplicate images refused, active accounts only.
/// CSRF: unlike the JSON-only endpoints, a multipart form CAN be posted cross-site, so this POST also requires the
/// anti-forgery token (form field __RequestVerificationToken or header RequestVerificationToken).
/// </summary>
[ApiController]
[Route("api/checkdeposits")]
[Authorize(Roles = SeedData.CustomerRole)]
[Produces("application/json")]
public class CheckDepositsApiController : ControllerBase
{
    private readonly ICheckDepositService _checks;
    private readonly UserManager<ApplicationUser> _userManager;

    public CheckDepositsApiController(ICheckDepositService checks, UserManager<ApplicationUser> userManager)
    {
        _checks = checks;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ChecksController.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ChecksController.MaxRequestBytes)]
    [ProducesResponseType(typeof(CheckDepositDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CheckDepositDto>> Create([FromForm] CheckDepositUploadDto body)
    {
        var (result, deposit) = await _checks.SubmitAsync(UserId, body.AccountId, body.Amount, body.Front, body.Back);
        if (!result.Success || deposit == null) return BadRequest(new ProblemDetails { Title = result.Message, Status = 400 });
        var saved = await _checks.GetAsync(UserId, deposit.Id);
        return CreatedAtAction(nameof(Get), new { id = deposit.Id }, ToDto(saved!));
    }

    [HttpGet]
    public async Task<ActionResult<List<CheckDepositDto>>> List() =>
        (await _checks.ListAsync(UserId, take: 50)).Select(ToDto).ToList();

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CheckDepositDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CheckDepositDto>> Get(int id)
    {
        var d = await _checks.GetAsync(UserId, id);
        return d == null ? NotFound() : ToDto(d);
    }

    private CheckDepositDto ToDto(CheckDeposit d) => new(
        d.Id, d.Reference, d.AccountId, $"{d.Account?.DisplayName} ••{d.Account?.Last4}", d.Amount, d.Status.ToString(),
        DateTime.SpecifyKind(d.SubmittedAt, DateTimeKind.Utc),
        d.ReviewedAt is DateTime r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null,
        d.RejectReason, d.TransactionId,
        Url.Action("Image", "Checks", new { id = d.Id, side = "front" })!,
        Url.Action("Image", "Checks", new { id = d.Id, side = "back" })!);
}

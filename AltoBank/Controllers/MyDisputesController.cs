using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// DISPUTES — /disputes
///   GET  /disputes                              your disputes
///   GET  /disputes/new?transactionId=5          the form (only for an eligible transaction; the server re-checks on POST)
///   POST /disputes                              multipart (optional attachment) → Post/Redirect/Get to the dispute
///   GET  /disputes/{id}                         timeline: Submitted → Under review → Refunded / Denied
///   GET  /disputes/for-transaction/{id}         the dispute on one of your transactions
///   GET  /disputes/{id}/attachment              your attachment (never cached)
/// Another customer's transaction, dispute or attachment returns 404.
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("disputes")]
public class MyDisputesController : Controller
{
    private readonly IDisputeService _disputes;
    private readonly ICardService _cards;
    private readonly IBankService _bank;
    private readonly UserManager<ApplicationUser> _userManager;

    public MyDisputesController(IDisputeService disputes, ICardService cards, IBankService bank, UserManager<ApplicationUser> userManager)
    {
        _disputes = disputes; _cards = cards; _bank = bank; _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Disputes";
        return View(await _disputes.ListAsync(UserId));
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(int transactionId)
    {
        var (t, why) = await _disputes.GetDisputableAsync(UserId, transactionId);
        if (t == null && why == null) return NotFound();   // not this customer's transaction
        if (t == null)
        {
            TempData["Error"] = why;
            return Back();
        }
        return View(await BuildAsync(t, new DisputeForm { TransactionId = t.Id }));
    }

    [HttpPost("")]
    [RequestSizeLimit(ChecksController.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ChecksController.MaxRequestBytes)]
    public async Task<IActionResult> Submit([Bind(Prefix = "Form")] DisputeForm form)
    {
        var (t, why) = await _disputes.GetDisputableAsync(UserId, form.TransactionId);
        if (t == null && why == null) return NotFound();
        if (t == null) { TempData["Error"] = why; return Back(); }

        if (ModelState.IsValid)
        {
            var (result, dispute) = await _disputes.SubmitAsync(UserId,
                new DisputeInput(t.Id, form.Reason!.Value, form.Description, form.ExpectedAmount, form.DuplicateOfTransactionId, form.FreezeCard),
                form.Attachment);
            if (result.Success)
            {
                TempData["Success"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = dispute!.Id });
            }
            ModelState.AddModelError(string.Empty, result.Message);
        }
        ViewData["AttachmentCleared"] = form.Attachment != null;
        return View(nameof(New), await BuildAsync(t, form));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var d = await _disputes.GetAsync(UserId, id);
        if (d == null) return NotFound();
        ViewData["Title"] = $"Dispute {d.Reference}";
        return View(d);
    }

    [HttpGet("for-transaction/{transactionId:int}")]
    public async Task<IActionResult> ForTransaction(int transactionId)
    {
        var d = (await _disputes.ListAsync(UserId)).FirstOrDefault(x => x.TransactionId == transactionId);
        return d == null ? NotFound() : RedirectToAction(nameof(Details), new { id = d.Id });
    }

    [HttpGet("{id:int}/attachment")]
    public async Task<IActionResult> Attachment(int id)
    {
        var file = await _disputes.OpenAttachmentForCustomerAsync(UserId, id);
        if (file == null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Value.Stream, file.Value.ContentType);
    }

    private async Task<DisputePageViewModel> BuildAsync(Transaction t, DisputeForm form)
    {
        ViewData["Title"] = "Dispute a charge";
        var settings = await _bank.GetSettingsAsync();
        return new DisputePageViewModel
        {
            Transaction = t,
            SimilarCharges = await _disputes.GetSimilarChargesAsync(UserId, t),
            ActiveCard = (await _cards.GetCardsAsync(UserId, t.AccountId)).FirstOrDefault(c => c.Status == CardStatus.Active),
            ProvisionalCredit = settings.ProvisionalCreditOnDisputes,
            Form = form
        };
    }

    // Where to send someone whose transaction can't be disputed: back where they came from (if it's our own page).
    private IActionResult Back()
    {
        if (Uri.TryCreate(Request.Headers.Referer.ToString(), UriKind.Absolute, out var from) && from.Host == Request.Host.Host
            && Url.IsLocalUrl(from.PathAndQuery))
            return LocalRedirect(from.PathAndQuery);
        return RedirectToAction("Index", "Accounts");
    }
}

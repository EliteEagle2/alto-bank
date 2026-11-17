using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// ALTO PEAK CREDIT CARD — /credit-card (the card's own page is /accounts/{id}; this controller holds its actions)
///   GET  /credit-card/{id}/pay                  the Pay card page (minimum / statement balance / current balance / other)
///   POST /credit-card/{id}/pay                  → PRG back to the card
///   POST /credit-card/{id}/purchase             demo purchase (there's no real store)
///   POST /credit-card/{id}/redeem               redeem all cash back (to=statement or a checking/savings id)
///   POST /credit-card/{id}/autopay              autopay settings
///   GET  /credit-card/statements/{statementId}  printable statement
/// Another customer's card or statement returns 404. Money moves go through IBankService.
/// (Not named "CreditCards": that's the admin controller, and same-named controllers mix up area links.)
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
[Route("credit-card")]
public class CreditCardController : Controller
{
    private readonly IBankService _bank;
    private readonly ICreditCardService _cards;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreditCardController(IBankService bank, ICreditCardService cards, UserManager<ApplicationUser> userManager)
    {
        _bank = bank; _cards = cards; _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    private async Task<Account?> CardAsync(int id)
    {
        var a = await _bank.GetAccountAsync(id, UserId);   // ownership check
        return a?.IsCreditCard == true ? a : null;
    }

    private IActionResult BackToCard(int id, BankResult r, string? tab = null)
    {
        TempData[r.Success ? "Success" : "Error"] = r.Message;
        return Redirect(Url.Action("Details", "Accounts", new { id })! + (tab == null ? "" : "#" + tab));
    }

    [HttpGet("{id:int}/pay")]
    public async Task<IActionResult> Pay(int id) => await PayPageAsync(id, new PayCardForm());

    [HttpPost("{id:int}/pay")]
    public async Task<IActionResult> Pay(int id, PayCardForm form)
    {
        var card = await CardAsync(id);
        if (card == null) return NotFound();
        if (form.Option == CardPaymentOption.Custom && form.CustomAmount is not > 0)
            ModelState.AddModelError(nameof(form.CustomAmount), "Enter how much you want to pay.");
        if (form.FromAccountId is not int from) { ModelState.AddModelError(nameof(form.FromAccountId), "Choose the account to pay from."); from = 0; }
        if (!ModelState.IsValid) return await PayPageAsync(id, form);

        var r = await _bank.PayCardAsync(UserId, id, from, form.Option, form.CustomAmount);
        if (!r.Success)
        {
            ModelState.AddModelError("", r.Message);
            return await PayPageAsync(id, form);
        }
        return BackToCard(id, r);
    }

    private async Task<IActionResult> PayPageAsync(int id, PayCardForm form)
    {
        var card = await CardAsync(id);
        if (card == null) return NotFound();
        var (statement, paid, minLeft, stmtLeft) = await _bank.GetCardDueAsync(card);
        var from = await _cards.GetPayFromAccountsAsync(UserId);
        form.FromAccountId ??= card.AutopayFromAccountId ?? from.FirstOrDefault()?.Id;
        ViewData["Title"] = "Pay card";
        return View("Pay", new PayCardViewModel
        {
            Card = card, Statement = statement, PaidSinceClose = paid, MinimumLeft = minLeft, StatementLeft = stmtLeft,
            FromAccounts = from, Form = form
        });
    }

    [HttpPost("{id:int}/purchase")]
    public async Task<IActionResult> Purchase(int id, string? merchant, decimal? amount)
    {
        if (await CardAsync(id) == null) return NotFound();
        if (amount is not > 0) return BackToCard(id, new BankResult(false, "Enter the purchase amount."));
        return BackToCard(id, await _bank.CardPurchaseAsync(UserId, id, merchant, amount.Value));
    }

    [HttpPost("{id:int}/redeem")]
    public async Task<IActionResult> Redeem(int id, string? to)
    {
        if (await CardAsync(id) == null) return NotFound();
        int? toAccount = int.TryParse(to, out var t) ? t : null;   // "statement" (or nothing) = a statement credit
        return BackToCard(id, await _bank.RedeemCashBackAsync(UserId, id, toAccount), "rewards");
    }

    [HttpPost("{id:int}/autopay")]
    public async Task<IActionResult> Autopay(int id, CardAutopay mode, int? fromAccountId)
    {
        var r = await _cards.SetAutopayAsync(UserId, id, mode, fromAccountId);
        return r == null ? NotFound() : BackToCard(id, r, "rewards");
    }

    [HttpGet("statements/{statementId:int}")]
    public async Task<IActionResult> Statement(int statementId)
    {
        var s = await _cards.GetStatementAsync(UserId, statementId);
        return s == null ? NotFound() : View(s);
    }
}

using AltoBank.Data;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>
/// Customer debit-card actions from the Card tab on /accounts/{id}: freeze, unfreeze, cancel, request a new card.
/// IDOR protection: CardService only finds the card if its account belongs to the logged-in user,
/// and we also check the card belongs to the account in the URL. Otherwise 404.
/// Every action is a POST with an anti-forgery token, then redirects back to the Card tab (PRG).
/// </summary>
[Authorize(Roles = SeedData.CustomerRole)]
[AutoValidateAntiforgeryToken]
[ApiExplorerSettings(IgnoreApi = true)]
public class CardsController : Controller
{
    private readonly ICardService _cards;
    private readonly UserManager<ApplicationUser> _userManager;

    public CardsController(ICardService cards, UserManager<ApplicationUser> userManager)
    {
        _cards = cards;
        _userManager = userManager;
    }

    private string UserId => _userManager.GetUserId(User)!;

    [HttpPost("accounts/{id:int}/cards/{cardId:int}/freeze")]
    public Task<IActionResult> Freeze(int id, int cardId) => Run(id, cardId, () => _cards.FreezeAsync(UserId, cardId));

    [HttpPost("accounts/{id:int}/cards/{cardId:int}/unfreeze")]
    public Task<IActionResult> Unfreeze(int id, int cardId) => Run(id, cardId, () => _cards.UnfreezeAsync(UserId, cardId));

    [HttpPost("accounts/{id:int}/cards/{cardId:int}/cancel")]
    public Task<IActionResult> Cancel(int id, int cardId, string reason) => Run(id, cardId, () => _cards.CancelAsync(UserId, cardId, reason));

    [HttpPost("accounts/{id:int}/cards/{cardId:int}/replace")]
    public Task<IActionResult> Replace(int id, int cardId, string reason) =>
        Run(id, cardId, async () => (await _cards.ReplaceAsync(UserId, cardId, reason)).Result);

    private async Task<IActionResult> Run(int accountId, int cardId, Func<Task<BankResult>> action)
    {
        var card = await _cards.GetCardAsync(UserId, cardId);
        if (card == null || card.AccountId != accountId) return NotFound();

        var result = await action();
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction("Details", "Accounts", new { id = accountId }, "card");
    }
}

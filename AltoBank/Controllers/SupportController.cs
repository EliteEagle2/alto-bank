using AltoBank.Data;
using AltoBank.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>Public support pages: Help Center (FAQ), Contact Us (form), Security.</summary>
public class SupportController : Controller
{
    public IActionResult Help() => View(SiteContent.Faqs);

    public IActionResult Security() => View();

    // GET /Support/Contact?topic=Careers — the optional topic pre-selects the dropdown.
    [HttpGet]
    public IActionResult Contact(string? topic)
    {
        var model = new ContactViewModel();
        if (topic != null && ContactViewModel.Topics.Contains(topic)) model.Topic = topic;
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Contact(ContactViewModel model)
    {
        if (!ContactViewModel.Topics.Contains(model.Topic))
            ModelState.AddModelError(nameof(model.Topic), "Please choose a topic.");

        if (!ModelState.IsValid) return View(model);

        // Demo only: a real bank would email this or save it to a support ticket system.
        TempData["Success"] = $"Thanks, {model.Name.Trim()}! We got your message and will reply to {model.Email} within one business day.";
        return RedirectToAction(nameof(Contact)); // Post/Redirect/Get prevents double-submits on refresh
    }
}

using AltoBank.Data;
using Microsoft.AspNetCore.Mvc;

namespace AltoBank.Controllers;

/// <summary>Public "Company" pages: About, Careers, Newsroom (+ individual articles).</summary>
public class CompanyController : Controller
{
    public IActionResult About() => View();

    // Passes the list of open roles to the view as its model.
    public IActionResult Careers() => View(SiteContent.Jobs);

    // Newest press releases first.
    public IActionResult Newsroom() => View(SiteContent.News.OrderByDescending(n => n.Date).ToList());

    // GET /Company/Article/instant-transfers — the "id" route value is the article's slug.
    public IActionResult Article(string id)
    {
        var article = SiteContent.News.FirstOrDefault(n => n.Slug == id);
        if (article == null) return NotFound();
        return View(article);
    }
}

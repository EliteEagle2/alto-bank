namespace AltoBank.Models;

// Plain models for the public marketing pages (Careers, Newsroom, Help Center).
// They are NOT database tables — the content is static and lives in Data/SiteContent.cs.

/// <summary>An open position listed on the Careers page.</summary>
public record JobOpening(string Title, string Team, string Location, string Type);

/// <summary>A press release on the Newsroom page. Slug is the URL-friendly id.</summary>
public record NewsArticle(string Slug, DateTime Date, string Category, string Title, string Summary, string[] Body);

/// <summary>A question + answer in the Help Center.</summary>
public record FaqItem(string Category, string Question, string Answer);

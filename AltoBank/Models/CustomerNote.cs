using System.ComponentModel.DataAnnotations;

namespace AltoBank.Models;

/// <summary>Internal note an admin writes about a customer. Only admins can see these.</summary>
public class CustomerNote
{
    public int Id { get; set; }

    [Required] public string CustomerUserId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    [Required] public string AuthorUserId { get; set; } = string.Empty;
    [Required, StringLength(120)] public string AuthorName { get; set; } = string.Empty;

    [Required, StringLength(2000)] public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

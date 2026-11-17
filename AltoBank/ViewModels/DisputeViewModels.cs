using System.ComponentModel.DataAnnotations;
using AltoBank.Models;

namespace AltoBank.ViewModels;

/// <summary>The dispute form (multipart: the optional attachment binds to an IFormFile).</summary>
public class DisputeForm
{
    [Required] public int TransactionId { get; set; }

    [Required(ErrorMessage = "Choose what went wrong.")]
    [Display(Name = "What went wrong?")]
    public DisputeReason? Reason { get; set; }

    [Required(ErrorMessage = "Tell us what happened.")]
    [StringLength(1000, MinimumLength = 20, ErrorMessage = "Tell us what happened in 20 to 1,000 characters.")]
    [Display(Name = "What happened")]
    public string? Description { get; set; }

    // "Wrong amount": what you expected to pay. Checked against the charge on the server.
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Enter the amount you expected, more than $0.")]
    [Display(Name = "Amount you expected")]
    public decimal? ExpectedAmount { get; set; }

    // "Duplicate charge": the original charge.
    [Display(Name = "The original charge")]
    public int? DuplicateOfTransactionId { get; set; }

    // "I didn't authorize it": freeze the account's debit card at the same time.
    public bool FreezeCard { get; set; }

    [Display(Name = "Receipt or screenshot (optional)")]
    public IFormFile? Attachment { get; set; }
}

/// <summary>/disputes/new: the charge being disputed, similar charges (for "Duplicate"), and the debit card (for the freeze offer).</summary>
public class DisputePageViewModel
{
    public Transaction Transaction { get; set; } = null!;
    public List<Transaction> SimilarCharges { get; set; } = new();
    public Card? ActiveCard { get; set; }
    public bool ProvisionalCredit { get; set; }
    public DisputeForm Form { get; set; } = new();
}

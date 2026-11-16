using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using AltoBank.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AltoBank.ViewModels;

/// <summary>
/// The "Deposit a check" form. FILE UPLOAD MODEL BINDING: the two IFormFile properties are filled from the
/// multipart/form-data request (input names Form.Front / Form.Back). The attributes check the simple things;
/// IFileStore checks the files themselves (real JPG/PNG by signature, 5 MB max) and CheckDepositService the limits.
/// </summary>
public class CheckDepositForm
{
    [Required(ErrorMessage = "Enter the amount written on the check.")]
    [Range(typeof(decimal), "0.01", "1000000", ErrorMessage = "Enter an amount between $0.01 and $1,000,000.")]
    [Display(Name = "Check amount")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Choose the account to deposit to.")]
    [Display(Name = "Deposit to")]
    public int? AccountId { get; set; }

    [Display(Name = "Front of the check")] public IFormFile? Front { get; set; }
    [Display(Name = "Back of the check")] public IFormFile? Back { get; set; }

    // "Endorse the back" checklist: all three must be ticked.
    [Range(typeof(bool), "true", "true", ErrorMessage = "Sign the back of the check first.")]
    public bool Signed { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Write \"For mobile deposit only at Alto Bank\" under your signature.")]
    public bool Restricted { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Make sure the whole check is in each photo.")]
    public bool InFrame { get; set; }
}

/// <summary>/checks: the form, today's limits and the customer's recent check deposits.</summary>
public class CheckDepositPageViewModel
{
    public CheckDepositForm Form { get; set; } = new();
    public List<SelectListItem> Accounts { get; set; } = new();
    public CheckDepositLimits Limits { get; set; } = new(0, 0, 0);
    public List<CheckDeposit> Recent { get; set; } = new();
}

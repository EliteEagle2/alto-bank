using System.ComponentModel.DataAnnotations;
using AltoBank.Models;
using AltoBank.Services;
using AltoBank.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AltoBank.ViewModels;

/// <summary>
/// Everything posted by the 7-step account application in ONE form submit.
/// The attributes here are the first line of server-side validation; the cross-field rules
/// (age 18+, e-signature match, deposit vs. balance, ...) are checked in AccountsController.Apply
/// using Services/ApplicationRules.cs. The browser's JavaScript checks are only for convenience.
/// </summary>
public class AccountApplicationViewModel
{
    private const string NamePattern = @"^[A-Za-z][A-Za-z' .\-]*$";
    private const string NameMessage = "Use letters, spaces, apostrophes, periods, or hyphens.";

    // ===== Step 1: choose your account =====
    [Required(ErrorMessage = "Choose an account to open.")]
    [Display(Name = "Account type")]
    public AccountType? AccountType { get; set; }

    [StringLength(30, ErrorMessage = "Nickname can be at most 30 characters.")]
    [Display(Name = "Account nickname")]
    public string? Nickname { get; set; }

    [Display(Name = "Debit card design")]
    public string? CardDesign { get; set; } = CardDesigns.Aurora;

    // ===== Step 2: about you =====
    [Required(ErrorMessage = "Enter your first name."), StringLength(50)]
    [RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [RegularExpression("^[A-Za-z]$", ErrorMessage = "Middle initial must be a single letter.")]
    [Display(Name = "Middle initial")]
    public string? MiddleInitial { get; set; }

    [Required(ErrorMessage = "Enter your last name."), StringLength(50)]
    [RegularExpression(NamePattern, ErrorMessage = NameMessage)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    // Date of birth comes in as three separate boxes (MM / DD / YYYY).
    [Required(ErrorMessage = "Enter your birth month.")] public int? DobMonth { get; set; }
    [Required(ErrorMessage = "Enter your birth day.")] public int? DobDay { get; set; }
    [Required(ErrorMessage = "Enter your birth year.")] public int? DobYear { get; set; }

    // The three boxes combined into a real date (null if it isn't a real date, e.g. 02/30).
    // Read-only, so model binding never sets it — but validation still checks it with our
    // CUSTOM attribute [MinimumAge(18)] (see Validation/MinimumAgeAttribute.cs).
    [MinimumAge(ApplicationRules.MinimumAge)]
    [Display(Name = "Date of birth")]
    public DateOnly? DateOfBirth => ApplicationRules.TryGetDate(DobMonth, DobDay, DobYear);

    [Required(ErrorMessage = "Select your citizenship.")]
    public string? Citizenship { get; set; }

    // ===== Step 3: contact & address =====
    // Email is display-only here; the server always uses the login email on the user's profile.
    [ValidateNever] public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your mobile phone number.")]
    [Display(Name = "Mobile phone")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Enter your street address."), StringLength(100)]
    [Display(Name = "Street address")]
    public string? Street { get; set; }

    [StringLength(50)]
    [Display(Name = "Apt, suite, unit")]
    public string? Apt { get; set; }

    [Required(ErrorMessage = "Enter your city."), StringLength(60)]
    public string? City { get; set; }

    [Required(ErrorMessage = "Select your state.")]
    public string? State { get; set; }

    [Required(ErrorMessage = "Enter your ZIP code.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "ZIP code must be exactly 5 digits.")]
    [Display(Name = "ZIP code")]
    public string? Zip { get; set; }

    [Required(ErrorMessage = "Let us know whether you've lived here for 2+ years.")]
    public bool? LivedTwoYears { get; set; }

    // Previous address (only required when LivedTwoYears == false)
    [StringLength(100)] [Display(Name = "Previous street address")] public string? PrevStreet { get; set; }
    [StringLength(60)] [Display(Name = "Previous city")] public string? PrevCity { get; set; }
    [Display(Name = "Previous state")] public string? PrevState { get; set; }
    [RegularExpression(@"^\d{5}$", ErrorMessage = "ZIP code must be exactly 5 digits.")]
    [Display(Name = "Previous ZIP code")] public string? PrevZip { get; set; }

    // ===== Step 4: employment & income =====
    [Required(ErrorMessage = "Select your employment status.")]
    [Display(Name = "Employment status")]
    public string? EmploymentStatus { get; set; }

    [StringLength(80)] [Display(Name = "Employer name")] public string? Employer { get; set; }
    [StringLength(80)] public string? Occupation { get; set; }

    [Required(ErrorMessage = "Select your annual income range.")]
    [Display(Name = "Annual income")]
    public string? IncomeRange { get; set; }

    public List<string> SourceOfFunds { get; set; } = new();

    // ===== Step 5: identity (simulated) =====
    // Signed token from /Accounts/VerifyIdentity proving the check ran for THIS user.
    public string? IdentityToken { get; set; }
    public string? QuizStreet { get; set; }
    public string? QuizZip { get; set; }

    // ===== Step 6: fund your account =====
    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Opening deposit must be between $0 and $1,000,000.")]
    [Display(Name = "Opening deposit")]
    public decimal? InitialDeposit { get; set; }

    // null = external bank (simulated); otherwise one of the user's Alto accounts.
    [Display(Name = "Funding source")]
    public int? FundingAccountId { get; set; }

    [StringLength(60)] [Display(Name = "Bank name")] public string? ExternalBankName { get; set; }
    [Display(Name = "Account last 4")] public string? ExternalLast4 { get; set; }

    // ===== Step 6 (Alto Peak card instead of funding) =====
    [Range(typeof(decimal), "0", "100000", ErrorMessage = "Enter a monthly amount between $0 and $100,000.")]
    [Display(Name = "Monthly rent or mortgage")]
    public decimal? MonthlyHousingPayment { get; set; }
    public bool AgreeCardmemberAgreement { get; set; }

    // ===== Step 1 + 6 (a loan instead of funding) =====
    public LoanKind? LoanKind { get; set; }
    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Enter a loan amount.")]
    [Display(Name = "Loan amount")]
    public decimal? LoanAmount { get; set; }
    [Display(Name = "Term")] public int? LoanTermMonths { get; set; }
    [StringLength(60)] [Display(Name = "What it's for")] public string? LoanPurpose { get; set; }
    [Display(Name = "Year")] public int? VehicleYear { get; set; }
    [StringLength(30)] [Display(Name = "Make")] public string? VehicleMake { get; set; }
    [StringLength(40)] [Display(Name = "Model")] public string? VehicleModel { get; set; }
    [Display(Name = "Send the money to")] public int? DisburseToAccountId { get; set; }

    // ===== Step 7: review & sign =====
    public bool AgreeAccountAgreement { get; set; }
    public bool AgreePrivacy { get; set; }
    public bool AgreeESign { get; set; }

    [Required(ErrorMessage = "Type your full name to sign.")]
    [StringLength(110)]
    [Display(Name = "Electronic signature")]
    public string? ESignName { get; set; }

    // ===== Data for the view only (never trusted from the form) =====
    [ValidateNever] public List<Account> FundingAccounts { get; set; } = new();
    [ValidateNever] public string? CardUnavailableReason { get; set; }   // already has a Peak card / a pending card application
    [ValidateNever] public decimal CardAprPercent { get; set; }
    [ValidateNever] public decimal CardCashBackPercent { get; set; }
    [ValidateNever] public string? LoanUnavailableReason { get; set; }   // no checking account / a loan application in progress
    [ValidateNever] public List<Account> CheckingAccounts { get; set; } = new();   // where loan money can go
    [ValidateNever] public BankSetting LoanSettings { get; set; } = new();
    [ValidateNever] public bool EstablishedCustomer { get; set; }        // 60+ days and $1,000+: one rate tier better
    [ValidateNever] public bool IsReturning { get; set; }
    [ValidateNever] public string UserKey { get; set; } = string.Empty;   // sessionStorage key suffix
    [ValidateNever] public string? VerifiedLast4 { get; set; }            // decoded from IdentityToken on re-display
}

/// <summary>Data for the decision ("Approved!") screen.</summary>
public class ApplicationDecisionViewModel
{
    public AccountApplication Application { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public decimal OpeningBalance { get; set; }
    public string RoutingNumber { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public bool Animate { get; set; }           // play the "Reviewing..." sequence (first view only)
    public string UserKey { get; set; } = string.Empty;
}

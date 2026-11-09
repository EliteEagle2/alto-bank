using System.ComponentModel.DataAnnotations;

namespace AltoBank.Validation;

/// <summary>
/// CUSTOM VALIDATION ATTRIBUTE: [MinimumAge(18)]
/// Put it on a DateOnly / DateTime property holding a date of birth. Model validation
/// fails if the person is younger than the given age today (or the date is in the future).
/// A null value is left to [Required] to report, just like the built-in attributes do.
///
/// Usage (see ViewModels/ApplicationViewModels.cs):
///     [MinimumAge(18)]
///     public DateOnly? DateOfBirth => ...;
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class MinimumAgeAttribute : ValidationAttribute
{
    public int Years { get; }

    public MinimumAgeAttribute(int years) : base("You must be {0} or older to open an account.")
    {
        Years = years;
    }

    public override string FormatErrorMessage(string name) => string.Format(ErrorMessageString, Years);

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        DateOnly? birth = value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            null => null,
            _ => throw new InvalidOperationException("[MinimumAge] only works on DateOnly or DateTime properties.")
        };
        if (birth == null) return ValidationResult.Success;

        var today = DateOnly.FromDateTime(DateTime.Today);
        // Your Nth birthday must be today or earlier.
        return birth.Value.AddYears(Years) <= today
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(context.DisplayName), new[] { context.MemberName ?? "" });
    }
}

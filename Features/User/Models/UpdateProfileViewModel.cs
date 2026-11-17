using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.User.Models;

public sealed class UpdateProfileViewModel : IValidatableObject
{
    [MaxLength(100)] public string? DisplayName { get; set; }
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Gender { get; set; } = string.Empty;
    [Required] public DateOnly? DateOfBirth { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(PhoneNumber.Trim(), @"^0[35789]\d{8}$"))
            yield return new ValidationResult("Phone number must contain 10 digits with a supported mobile prefix.", [nameof(PhoneNumber)]);
        if (Gender.Trim() is not ("Nam" or "Nữ" or "Khác"))
            yield return new ValidationResult("Gender must be Nam, Nữ or Khác.", [nameof(Gender)]);
        if (DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
            yield return new ValidationResult("Date of birth cannot be in the future.", [nameof(DateOfBirth)]);
    }
}

using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.User.Models;

public sealed class UpdateProfileViewModel
{
    [MaxLength(100)] public string? DisplayName { get; set; }
    [Required, MaxLength(20)] public string PhoneNumber { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string Gender { get; set; } = string.Empty;
    [Required] public DateOnly? DateOfBirth { get; set; }
}

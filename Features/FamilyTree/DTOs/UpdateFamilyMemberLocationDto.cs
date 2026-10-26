using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.FamilyTree.DTOs;

public sealed class UpdateFamilyMemberLocationDto
{
    [Required, Range(-90, 90)]
    public required double Latitude { get; set; }

    [Required, Range(-180, 180)]
    public required double Longitude { get; set; }

    [MaxLength(500)]
    public string? DisplayName { get; set; }
}

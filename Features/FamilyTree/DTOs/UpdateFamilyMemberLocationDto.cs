using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.FamilyTree.DTOs;

public sealed class UpdateFamilyMemberLocationDto
{
    [Required, Range(-90d, 90d)]
    public required double Latitude { get; set; }

    [Required, Range(-180d, 180d)]
    public required double Longitude { get; set; }

    [MaxLength(500)]
    public string? DisplayName { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.Library.DTOs;

public sealed class CreateAlbumDto
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [StringLength(2000)] public string? Description { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.Library.DTOs;

// Shared metadata for a multipart batch and a single photo's JSON update.
// Description reuses the existing caption column rather than duplicating it.
public sealed class LibraryPhotoMetadataDto
{
    [StringLength(200)] public string? Title { get; set; }
    [Required, StringLength(64)]
    public string Category { get; set; } = "photos";
    [StringLength(100)] public string? DisplayDate { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    [StringLength(200)] public string? Author { get; set; }
}

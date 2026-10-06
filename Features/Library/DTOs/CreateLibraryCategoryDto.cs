namespace MyLife.Features.Library.DTOs;

// Ownership, slug and default flag are always assigned by the service.
public sealed class CreateLibraryCategoryDto
{
    public string? Name { get; set; }
}

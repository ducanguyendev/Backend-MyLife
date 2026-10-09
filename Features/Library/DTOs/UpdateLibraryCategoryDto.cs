namespace MyLife.Features.Library.DTOs;

// Rename only: identity, ownership, slug and default flag are never client-controlled.
public sealed class UpdateLibraryCategoryDto
{
    public string? Name { get; set; }
}

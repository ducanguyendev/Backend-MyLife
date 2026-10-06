namespace MyLife.Features.Library.DTOs;

public sealed record AlbumDto(long Id, string Name, string? Description, int PhotoCount,
    string? CoverPhotoUrl, DateTime CreatedAt, DateTime UpdatedAt);

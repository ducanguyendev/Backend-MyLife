namespace MyLife.Features.Library.DTOs;

public sealed record AlbumDetailDto(long Id, string Name, string? Description, int PhotoCount,
    string? CoverPhotoUrl, long? CoverPhotoId, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<PhotoDto> Photos);

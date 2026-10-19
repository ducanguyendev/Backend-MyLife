namespace MyLife.Features.Library.DTOs;

public sealed record LibraryOverviewDto(
    IReadOnlyList<LibraryCategoryDto> Categories,
    IReadOnlyList<AlbumDetailDto> Albums);

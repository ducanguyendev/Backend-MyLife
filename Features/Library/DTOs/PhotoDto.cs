namespace MyLife.Features.Library.DTOs;

public sealed record PhotoDto(long Id, string Url, string FileName, string ContentType, long FileSize,
    string? Caption, int SortOrder, DateTime? TakenAt, DateTime CreatedAt,
    string? Title, string Category, string? DisplayDate, string? Author)
{
    public string? Description => Caption;
}

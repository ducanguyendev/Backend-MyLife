namespace MyLife.Shared.Entities;

public sealed class LibraryPhoto
{
    public long Id { get; set; }
    public long AlbumId { get; set; }
    public LibraryAlbum Album { get; set; } = null!;
    public string DriveFileId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? Caption { get; set; }
    public string? Title { get; set; }
    public string Category { get; set; } = "photos";
    public string? DisplayDate { get; set; }
    public string? Author { get; set; }
    public int SortOrder { get; set; }
    public DateTime? TakenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

namespace MyLife.Shared.Entities;

public sealed class LibraryAlbum
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DriveFolderId { get; set; }
    public long? CoverPhotoId { get; set; }
    public LibraryPhoto? CoverPhoto { get; set; }
    public int CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<LibraryPhoto> Photos { get; set; } = new List<LibraryPhoto>();
}

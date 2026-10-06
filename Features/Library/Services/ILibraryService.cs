using MyLife.Features.Library.DTOs;

namespace MyLife.Features.Library.Services;

public interface ILibraryService
{
    Task<AlbumDto> CreateAlbumAsync(int userId, CreateAlbumDto dto, CancellationToken ct);
    Task<IReadOnlyList<AlbumDto>> ListAlbumsAsync(int userId, CancellationToken ct);
    Task<AlbumDetailDto> GetAlbumAsync(int userId, long albumId, CancellationToken ct);
    Task<AlbumDto> UpdateAlbumAsync(int userId, long albumId, UpdateAlbumDto dto, CancellationToken ct);
    Task<IReadOnlyList<PhotoDto>> UploadPhotosAsync(int userId, long albumId, IReadOnlyList<IFormFile> files, LibraryPhotoMetadataDto metadata, CancellationToken ct);
    Task<PhotoDto> UpdatePhotoAsync(int userId, long photoId, LibraryPhotoMetadataDto metadata, CancellationToken ct);
    Task DeletePhotoAsync(int userId, long photoId, CancellationToken ct);
    Task DeleteAlbumAsync(int userId, long albumId, CancellationToken ct);
}

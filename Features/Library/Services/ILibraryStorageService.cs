namespace MyLife.Features.Library.Services;

public sealed record LibraryFolderResult(bool Success, string? FolderId);
// On failure FileId may be present solely for compensating cleanup.
public sealed record LibraryFileResult(bool Success, string? FileId, string? Url, string? ContentType, long FileSize);
public sealed record LibraryImageReadResult(byte[] Content, string ContentType, string ETag);
public sealed class LibraryImageStorageException : Exception { }

public interface ILibraryStorageService
{
    Task<LibraryFolderResult> CreateAlbumFolderAsync(long albumId, CancellationToken cancellationToken);
    Task<LibraryFileResult> UploadPhotoAsync(string albumDriveFolderId, byte[] content, string contentType,
        string originalFileName, CancellationToken cancellationToken);
    Task<LibraryImageReadResult?> ReadPhotoAsync(long photoId, string albumDriveFolderId, string driveFileId,
        string contentType, long fileSize, DateTime revision, CancellationToken cancellationToken);
    Task<bool> DeletePhotoAsync(string albumDriveFolderId, string driveFileId, CancellationToken cancellationToken);
    Task<bool> DeleteAlbumFolderAsync(string driveFolderId, CancellationToken cancellationToken);
}

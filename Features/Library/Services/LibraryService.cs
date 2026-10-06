using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyLife.Features.Library.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;

namespace MyLife.Features.Library.Services;

public sealed partial class LibraryService(AppDbContext db, ILibraryStorageService storage, ILogger<LibraryService> logger) : ILibraryService
{
    public async Task<AlbumDto> CreateAlbumAsync(int userId, CreateAlbumDto dto, CancellationToken ct)
    {
        ValidateMetadata(dto.Name, dto.Description);
        var now = DateTime.UtcNow;
        var album = new LibraryAlbum { Name = dto.Name.Trim(), Description = NormalizeDescription(dto.Description),
            CreatedByUserId = userId, CreatedAt = now, UpdatedAt = now };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        string? folderId = null;
        try
        {
            db.LibraryAlbums.Add(album);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Library album creating UserId={UserId} AlbumId={AlbumId}", userId, album.Id);
            var result = await storage.CreateAlbumFolderAsync(album.Id, ct);
            folderId = result.FolderId;
            if (!result.Success || string.IsNullOrWhiteSpace(folderId))
                throw new LibraryOperationException(502, "Unable to create the album Drive folder.");
            album.DriveFolderId = folderId;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToAlbumDto(album, 0, null);
        }
        catch (Exception error)
        {
            await RollbackAsync(transaction, album.Id);
            var cleaned = folderId is null || await CleanupFolderAsync(album.Id, folderId);
            logger.LogWarning("Library album creation failed UserId={UserId} AlbumId={AlbumId} FolderId={FolderId} CleanupSuccess={CleanupSuccess} ErrorType={ErrorType}",
                userId, album.Id, folderId, cleaned, error.GetType().Name);
            throw Failure(error, cleaned);
        }
    }

    public async Task<IReadOnlyList<AlbumDto>> ListAlbumsAsync(int userId, CancellationToken ct) =>
        await SummaryQuery(userId).ToListAsync(ct);

    public async Task<AlbumDetailDto> GetAlbumAsync(int userId, long albumId, CancellationToken ct)
    {
        var album = await db.LibraryAlbums.AsNoTracking().Include(a => a.Photos).Include(a => a.CoverPhoto)
            .SingleOrDefaultAsync(a => a.Id == albumId && a.CreatedByUserId == userId, ct) ?? throw NotFound();
        return new(album.Id, album.Name, album.Description, album.Photos.Count, album.CoverPhoto?.Url,
            album.CreatedAt, album.UpdatedAt, album.Photos.OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt)
                .ThenBy(p => p.Id).Select(ToPhotoDto).ToList());
    }

    public async Task<AlbumDto> UpdateAlbumAsync(int userId, long albumId, UpdateAlbumDto dto, CancellationToken ct)
    {
        ValidateMetadata(dto.Name, dto.Description);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var album = await LockAlbumAsync(userId, albumId, ct);
        album.Name = dto.Name.Trim();
        album.Description = NormalizeDescription(dto.Description);
        album.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await SummaryQuery(userId, albumId).SingleAsync(ct);
    }

    public async Task<IReadOnlyList<PhotoDto>> UploadPhotosAsync(int userId, long albumId, IReadOnlyList<IFormFile> files, LibraryPhotoMetadataDto metadata, CancellationToken ct)
    {
        ValidatePhotoMetadata(metadata);
        if (files.Count is < 1 or > LibraryUploadRules.MaximumFiles)
            throw new LibraryOperationException(400, "Upload between 1 and 20 photos per request.", code: "LIBRARY_FILE_COUNT_INVALID");
        foreach (var file in files) LibraryUploadRules.ValidateFile(file);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockCategoryRegistryAsync(userId, ct);
        await EnsureDefaultCategoriesCoreAsync(userId, ct);
        await ValidateCategoryAsync(userId, metadata.Category, ct);
        var album = await LockAlbumAsync(userId, albumId, ct);
        var folderId = RequireFolder(album);
        var uploadedIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var photos = new List<LibraryPhoto>();
            foreach (var file in files)
            {
                await using var stream = new MemoryStream();
                await file.CopyToAsync(stream, ct);
                var content = stream.ToArray();
                var mime = LibraryUploadRules.NormalizeContentType(file.ContentType);
                if (content.Length != file.Length || !LibraryUploadRules.HasMatchingSignature(content, mime))
                    throw new LibraryOperationException(400, "Photo bytes do not match the declared image type.", code: "LIBRARY_INVALID_IMAGE");
                var result = await storage.UploadPhotoAsync(folderId, content, mime, file.FileName, ct);
                if (!string.IsNullOrWhiteSpace(result.FileId)) uploadedIds.Add(result.FileId);
                if (!result.Success || string.IsNullOrWhiteSpace(result.FileId) ||
                    result.ContentType != mime || result.FileSize != content.LongLength)
                    throw new LibraryOperationException(502, "Unable to verify a photo upload. No photos were saved.");
                var photo = new LibraryPhoto { AlbumId = album.Id, DriveFileId = result.FileId,
                    Url = $"https://lh3.googleusercontent.com/d/{result.FileId}", FileName = file.FileName,
                    ContentType = mime, FileSize = content.LongLength, SortOrder = 0, CreatedAt = DateTime.UtcNow };
                ApplyMetadata(photo, metadata);
                photos.Add(photo);
            }
            db.LibraryPhotos.AddRange(photos);
            album.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            logger.LogInformation("Library photos saved UserId={UserId} AlbumId={AlbumId} PhotoCount={PhotoCount}", userId, albumId, photos.Count);
            return photos.Select(ToPhotoDto).ToList();
        }
        catch (Exception error)
        {
            await RollbackAsync(transaction, albumId);
            var cleaned = await CleanupPhotosAsync(albumId, folderId, uploadedIds);
            logger.LogWarning("Library photo batch failed AlbumId={AlbumId} CleanupSuccess={CleanupSuccess} ErrorType={ErrorType}",
                albumId, cleaned, error.GetType().Name);
            throw Failure(error, cleaned);
        }
    }

    public async Task DeletePhotoAsync(int userId, long photoId, CancellationToken ct)
    {
        var albumId = await db.LibraryPhotos.AsNoTracking().Where(p => p.Id == photoId && p.Album.CreatedByUserId == userId)
            .Select(p => (long?)p.AlbumId).SingleOrDefaultAsync(ct) ?? throw NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var album = await LockAlbumAsync(userId, albumId, ct);
        var photo = await db.LibraryPhotos.SingleOrDefaultAsync(p => p.Id == photoId && p.AlbumId == albumId, ct) ?? throw NotFound();
        if (!await storage.DeletePhotoAsync(RequireFolder(album), photo.DriveFileId, ct))
            throw new LibraryOperationException(502, "Unable to delete the Drive photo. Database metadata was kept.");
        if (album.CoverPhotoId == photo.Id)
        {
            album.CoverPhotoId = null;
            await db.SaveChangesAsync(ct);
        }
        db.LibraryPhotos.Remove(photo);
        album.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<PhotoDto> UpdatePhotoAsync(int userId, long photoId, LibraryPhotoMetadataDto metadata, CancellationToken ct)
    {
        ValidatePhotoMetadata(metadata);
        var albumId = await db.LibraryPhotos.AsNoTracking().Where(p => p.Id == photoId && p.Album.CreatedByUserId == userId)
            .Select(p => (long?)p.AlbumId).SingleOrDefaultAsync(ct) ?? throw NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockCategoryRegistryAsync(userId, ct);
        await EnsureDefaultCategoriesCoreAsync(userId, ct);
        await ValidateCategoryAsync(userId, metadata.Category, ct);
        var album = await LockAlbumAsync(userId, albumId, ct);
        var photo = await db.LibraryPhotos.SingleOrDefaultAsync(p => p.Id == photoId && p.AlbumId == albumId, ct) ?? throw NotFound();
        ApplyMetadata(photo, metadata);
        album.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToPhotoDto(photo);
    }

    public async Task DeleteAlbumAsync(int userId, long albumId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var album = await LockAlbumAsync(userId, albumId, ct);
        if (!await storage.DeleteAlbumFolderAsync(RequireFolder(album), ct))
            throw new LibraryOperationException(502, "Unable to delete the Drive album folder. Database metadata was kept.");
        // Break the cover reference before the album cascades its photo rows.
        album.CoverPhotoId = null;
        await db.SaveChangesAsync(ct);
        db.LibraryAlbums.Remove(album);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<LibraryAlbum> LockAlbumAsync(int userId, long albumId, CancellationToken ct)
    {
        // Coordinate concurrent uploads/deletes across API instances. Keep the
        // row lock through storage and DB commit so uploads cannot outlive deletion.
        var rows = await db.LibraryAlbums.FromSqlInterpolated(
            $"SELECT * FROM library_albums WHERE id = {albumId} AND created_by_user_id = {userId} FOR UPDATE").ToListAsync(ct);
        return rows.SingleOrDefault() ?? throw NotFound();
    }

    private IQueryable<AlbumDto> SummaryQuery(int userId, long? albumId = null) => db.LibraryAlbums.AsNoTracking()
        .Where(a => a.CreatedByUserId == userId && (albumId == null || a.Id == albumId))
        .OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id).Select(a => new AlbumDto(a.Id, a.Name, a.Description,
            a.Photos.Count, a.CoverPhoto == null ? null : a.CoverPhoto.Url, a.CreatedAt, a.UpdatedAt));
    private static AlbumDto ToAlbumDto(LibraryAlbum album, int count, string? coverUrl) =>
        new(album.Id, album.Name, album.Description, count, coverUrl, album.CreatedAt, album.UpdatedAt);
    private static PhotoDto ToPhotoDto(LibraryPhoto photo) => new(photo.Id, photo.Url, photo.FileName,
        photo.ContentType, photo.FileSize, photo.Caption, photo.SortOrder, photo.TakenAt, photo.CreatedAt,
        photo.Title, photo.Category, photo.DisplayDate, photo.Author, photo.DriveFileId);
    private static void ValidatePhotoMetadata(LibraryPhotoMetadataDto metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Category) || metadata.Category.Length > 64 || metadata.Category == "all" ||
            metadata.Title?.Length > 200 || metadata.DisplayDate?.Length > 100 ||
            metadata.Description?.Length > 2000 || metadata.Author?.Length > 200)
            throw new LibraryOperationException(400, "Invalid Library photo metadata or category.", code: "LIBRARY_METADATA_INVALID");
    }
    private static void ApplyMetadata(LibraryPhoto photo, LibraryPhotoMetadataDto metadata)
    {
        photo.Title = NormalizeDescription(metadata.Title);
        photo.Category = metadata.Category;
        photo.DisplayDate = NormalizeDescription(metadata.DisplayDate);
        photo.Caption = NormalizeDescription(metadata.Description);
        photo.Author = NormalizeDescription(metadata.Author);
    }
    private static LibraryOperationException NotFound() => new(404, "Library album or photo not found.");
    private static string RequireFolder(LibraryAlbum album) => !string.IsNullOrWhiteSpace(album.DriveFolderId)
        ? album.DriveFolderId : throw new LibraryOperationException(503, "The album Drive folder is unavailable.", code: "LIBRARY_ALBUM_UNAVAILABLE");
    private static string? NormalizeDescription(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateMetadata(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || description?.Length > 2000)
            throw new LibraryOperationException(400, "Album name is required (max 150 characters), description max 2000 characters.");
    }
    private static LibraryOperationException Failure(Exception error, bool cleaned) => new(
        error is LibraryOperationException failure ? failure.StatusCode : 503,
        (error is LibraryOperationException known ? known.Message : "Library persistence failed. No successful result was reported.") +
        (cleaned ? "" : " Drive cleanup is incomplete; retry or contact the administrator."), error,
        !cleaned ? "LIBRARY_CLEANUP_FAILED" : error is LibraryOperationException coded ? coded.Code : "LIBRARY_SAVE_FAILED");

    private async Task RollbackAsync(IDbContextTransaction transaction, long albumId)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch (Exception error)
        {
            // A disconnected DB must not prevent attempting Drive compensation.
            logger.LogError("Library DB rollback failed AlbumId={AlbumId} ErrorType={ErrorType}", albumId, error.GetType().Name);
        }
    }

    private async Task<bool> CleanupPhotosAsync(long albumId, string folderId, IEnumerable<string> ids)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var success = true;
        foreach (var id in ids)
        {
            try
            {
                if (await storage.DeletePhotoAsync(folderId, id, cleanupTimeout.Token)) continue;
            }
            catch (Exception) { /* Each failed compensation is logged with its identity below. */ }
            success = false;
            logger.LogError("Library batch compensation failed AlbumId={AlbumId} FolderId={FolderId} FileId={FileId}", albumId, folderId, id);
        }
        return success;
    }
    private async Task<bool> CleanupFolderAsync(long albumId, string folderId)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (await storage.DeleteAlbumFolderAsync(folderId, cleanupTimeout.Token)) return true;
        }
        catch (Exception) { /* Log only identities, never storage credentials or payloads. */ }
        logger.LogError("Library album compensation failed AlbumId={AlbumId} FolderId={FolderId}", albumId, folderId);
        return false;
    }
}

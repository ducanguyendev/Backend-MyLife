using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace MyLife.Features.Library.Services;

public sealed class GoogleDriveLibraryStorageService(IConfiguration configuration, IHttpClientFactory clients,
    ILogger<GoogleDriveLibraryStorageService> logger, ILibraryPhotoImageCache imageCache) : ILibraryStorageService
{
    private static readonly HashSet<string> AllowedImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    public async Task<LibraryImageReadResult?> ReadPhotoAsync(long photoId, string albumDriveFolderId, string driveFileId,
        string expectedContentType, long expectedFileSize, DateTime revision, CancellationToken cancellationToken)
    {
        albumDriveFolderId = albumDriveFolderId.Trim();
        driveFileId = driveFileId.Trim();
        expectedContentType = LibraryUploadRules.NormalizeContentType(expectedContentType);
        if (photoId <= 0 || !ValidId(albumDriveFolderId) || !ValidId(driveFileId) || !AllowedImageTypes.Contains(expectedContentType) ||
            expectedFileSize is <= 0 or > LibraryUploadRules.MaximumPhotoBytes) throw new LibraryImageStorageException();
        var key = ImageCacheKey(photoId, driveFileId, revision);
        var result = await imageCache.GetOrCreateAsync(photoId, key,
            () => ReadPhotoFromAppsScriptAsync(photoId, albumDriveFolderId, driveFileId,
                expectedContentType, expectedFileSize, revision, cancellationToken), cancellationToken);
        return result ?? throw new LibraryImageStorageException();
    }

    internal static string ImageCacheKey(long photoId, string driveFileId, DateTime revision) =>
        $"library-photo-image:{photoId}:{driveFileId}:{revision.ToUniversalTime().Ticks}";

    private async Task<LibraryImageReadResult?> ReadPhotoFromAppsScriptAsync(long photoId, string albumDriveFolderId,
        string driveFileId, string expectedContentType, long expectedFileSize, DateTime revision, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await SendAsync(new { action = "library_read_photo", folderId = albumDriveFolderId,
                fileId = driveFileId }, timeout.Token);
            if (response is null || !IsSuccess(response.RootElement) || ReadId(response.RootElement, "fileId") != driveFileId)
                return null;
            var contentType = ReadString(response.RootElement, "contentType")?.Trim().ToLowerInvariant();
            var fileSize = ReadSize(response.RootElement, "fileSize");
            var checksum = ReadString(response.RootElement, "md5Checksum");
            var base64 = ReadString(response.RootElement, "fileBase64");
            var maxBase64Length = ((LibraryUploadRules.MaximumPhotoBytes + 2) / 3) * 4;
            if (contentType != expectedContentType || fileSize != expectedFileSize || fileSize is null or <= 0 or > LibraryUploadRules.MaximumPhotoBytes ||
                string.IsNullOrEmpty(checksum) || string.IsNullOrEmpty(base64) || base64.Length > maxBase64Length)
                return null;
            var bytes = Convert.FromBase64String(base64);
            if (bytes.LongLength != expectedFileSize || bytes.LongLength != fileSize ||
                !string.Equals(Convert.ToHexString(MD5.HashData(bytes)), checksum, StringComparison.OrdinalIgnoreCase) ||
                !LibraryUploadRules.HasMatchingSignature(bytes, contentType)) return null;

            var version = revision.ToUniversalTime().Ticks;
            var tagContent = System.Text.Encoding.UTF8.GetBytes($"{photoId}:{driveFileId}:{version}");
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(tagContent)).ToLowerInvariant()}\"";
            return new LibraryImageReadResult(bytes, contentType, etag);
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or IOException or JsonException ||
            ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Library image Apps Script read failed PhotoId={PhotoId} ErrorType={ErrorType}", photoId, ex.GetType().Name);
            return null;
        }
    }

    public async Task<LibraryFolderResult> CreateAlbumFolderAsync(long albumId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new { action = "library_create_album_folder",
            albumId = albumId.ToString(CultureInfo.InvariantCulture) }, cancellationToken);
        if (response is null) return new(false, null);
        var root = response.RootElement;
        var id = ReadId(root, "folderId");
        return new(IsSuccess(root) && id is not null && ReadString(root, "folderName") == $"album_{albumId}", id);
    }

    public async Task<LibraryFileResult> UploadPhotoAsync(string albumDriveFolderId, byte[] content, string contentType,
        string originalFileName, CancellationToken cancellationToken)
    {
        contentType = LibraryUploadRules.NormalizeContentType(contentType);
        if (content.Length is <= 0 or > LibraryUploadRules.MaximumPhotoBytes ||
            !LibraryUploadRules.HasMatchingSignature(content, contentType)) return new(false, null, null, null, 0);
        // Client filenames are only DB display metadata; never Drive identity.
        var internalName = $"photo_{Guid.NewGuid():N}{LibraryUploadRules.ExtensionFor(contentType)}";
        using var response = await SendAsync(new { action = "library_upload_photo", folderId = albumDriveFolderId,
            fileBase64 = Convert.ToBase64String(content), contentType, fileName = internalName }, cancellationToken);
        if (response is null) return new(false, null, null, null, 0);
        var root = response.RootElement;
        var id = ReadId(root, "fileId");
        var expectedChecksum = Convert.ToHexString(MD5.HashData(content));
        var verified = IsSuccess(root) && id is not null &&
            string.Equals(ReadString(root, "md5Checksum"), expectedChecksum, StringComparison.OrdinalIgnoreCase) &&
            ReadString(root, "contentType") == contentType && ReadSize(root) == content.LongLength;
        logger.LogInformation("Library storage upload FolderId={FolderId} FileId={FileId} ContentType={ContentType} FileSize={FileSize} Verified={Verified}",
            albumDriveFolderId, id, contentType, content.Length, verified);
        return verified ? new(true, id, $"https://lh3.googleusercontent.com/d/{id}", contentType, content.LongLength)
            : new(false, id, null, null, 0);
    }

    public Task<bool> DeletePhotoAsync(string albumDriveFolderId, string driveFileId, CancellationToken cancellationToken) =>
        DeleteAsync(new { action = "library_delete_photo", folderId = albumDriveFolderId, fileId = driveFileId }, cancellationToken);

    public Task<bool> DeleteAlbumFolderAsync(string driveFolderId, CancellationToken cancellationToken) =>
        DeleteAsync(new { action = "library_delete_album", folderId = driveFolderId }, cancellationToken);

    private async Task<bool> DeleteAsync(object payload, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(payload, cancellationToken);
        return response is not null && IsSuccess(response.RootElement) &&
            response.RootElement.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True;
    }

    private async Task<JsonDocument?> SendAsync(object payload, CancellationToken cancellationToken)
    {
        var endpoint = configuration["GoogleDrive:WebAppUrl"];
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        try
        {
            using var response = await clients.CreateClient(nameof(GoogleDriveLibraryStorageService))
                .PostAsJsonAsync(endpoint, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Library storage HTTP {StatusCode}", (int)response.StatusCode);
                return null;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                if (body.Length + read > 8L * 1024 * 1024) throw new IOException("Library storage response exceeded the read limit.");
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            body.Position = 0;
            return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            // Never log payloads, base64, URLs or credentials.
            logger.LogWarning("Library storage request failed ({ErrorType})", ex.GetType().Name);
            return null;
        }
    }

    private static bool IsSuccess(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("success", out var value) && value.ValueKind == JsonValueKind.True;
    private static string? ReadString(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? ReadId(JsonElement root, string name)
    {
        var id = ReadString(root, name);
        return !string.IsNullOrEmpty(id) && id.Length <= 255 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;
    }
    private static long? ReadSize(JsonElement root, string property = "fileSize") => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var size) ? size : null;
    private static bool ValidId(string id) => id.Length is > 0 and <= 255 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

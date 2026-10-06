using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace MyLife.Features.Library.Services;

public sealed class GoogleDriveLibraryStorageService(IConfiguration configuration, IHttpClientFactory clients,
    ILogger<GoogleDriveLibraryStorageService> logger) : ILibraryStorageService
{
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
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
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
    private static long? ReadSize(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("fileSize", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var size) ? size : null;
}

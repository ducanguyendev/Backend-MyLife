using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyLife.Features.Avatar.Services;

public sealed record AvatarStorageResult(bool Success, string? DriveUrl, string? FileId);

public interface IGoogleDriveAvatarService
{
    Task<AvatarStorageResult> UploadAvatarAsync(string email, IFormFile file, string? existingFileId, CancellationToken cancellationToken = default);
    Task<AvatarStorageResult> UploadAvatarAsync(string email, byte[] content, string contentType, string? existingFileId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAvatarAsync(string email, string? fileId, CancellationToken cancellationToken = default);
}

public sealed class GoogleDriveAvatarService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleDriveAvatarService> logger) : IGoogleDriveAvatarService
{
    private string WebAppUrl => configuration["GoogleDrive:WebAppUrl"] ?? string.Empty;
    private static string SanitizeEmail(string email) => email.Trim().ToLowerInvariant().Replace("/", "_").Replace("\\", "_");

    public async Task<AvatarStorageResult> UploadAvatarAsync(
        string email, IFormFile file, string? existingFileId, CancellationToken cancellationToken = default)
    {
        if (file.Length == 0) return new(false, null, null);
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return await UploadAvatarAsync(email, stream.ToArray(), file.ContentType, existingFileId, cancellationToken);
    }

    public async Task<AvatarStorageResult> UploadAvatarAsync(
        string email, byte[] content, string contentType, string? existingFileId, CancellationToken cancellationToken = default)
    {
        if (content.Length == 0 || string.IsNullOrWhiteSpace(WebAppUrl)) return new(false, null, null);
        contentType = contentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)
            ? "image/jpeg" : contentType.ToLowerInvariant();
        try
        {
            using var response = await SendAsync(new
            {
                action = "upload",
                email = SanitizeEmail(email),
                fileBase64 = Convert.ToBase64String(content),
                contentType,
                existingFileId = string.IsNullOrWhiteSpace(existingFileId) ? null : existingFileId
            }, cancellationToken);
            if (response is null) return new(false, null, null);
            var root = response.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            {
                logger.LogWarning("Avatar storage rejected upload ExistingFileId={ExistingFileId}", existingFileId);
                return new(false, null, null);
            }

            var fileId = ReadString(root, "fileId") ?? ReadString(root, "id");
            if (string.IsNullOrWhiteSpace(fileId) ||
                (!string.IsNullOrWhiteSpace(existingFileId) && fileId != existingFileId))
            {
                logger.LogWarning("Avatar storage did not preserve the expected Drive file ID");
                return new(false, null, null);
            }
            var driveUrl = GoogleAvatarSyncService.ToDirectDriveUrl(fileId);

            // Require the metadata read back from Drive after writing. This also
            // rejects a stale Web App deployment that only returns a file URL.
            var expectedChecksum = Convert.ToHexString(MD5.HashData(content));
            if (!string.Equals(ReadString(root, "md5Checksum"), expectedChecksum, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ReadString(root, "contentType"), contentType, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Avatar storage did not confirm the uploaded bytes and MIME type; verify the Web App deployment version");
                return new(false, null, null);
            }

            // A stable Drive file ID is the identity used for all later updates/deletes.
            return new(!string.IsNullOrWhiteSpace(fileId), driveUrl, fileId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Avatar upload storage call failed");
            return new(false, null, null);
        }
    }

    public async Task<bool> DeleteAvatarAsync(
        string email, string? fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WebAppUrl)) return false;
        try
        {
            using var response = await SendAsync(new
            {
                action = "delete",
                email = SanitizeEmail(email),
                fileId = string.IsNullOrWhiteSpace(fileId) ? null : fileId
            }, cancellationToken);
            if (response is null) return false;
            var root = response.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True &&
                   (string.IsNullOrWhiteSpace(fileId) ||
                    (root.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Avatar deletion storage call failed");
            return false;
        }
    }

    private async Task<JsonDocument?> SendAsync(object payload, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, WebAppUrl) { Content = content };
        using var response = await httpClientFactory.CreateClient(nameof(GoogleDriveAvatarService))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Avatar storage returned HTTP {StatusCode}", (int)response.StatusCode);
            return null;
        }

        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonDocument.Parse(rawContent);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse avatar storage JSON. ResponseLength={ResponseLength}", rawContent.Length);
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace MyLife.Features.FamilyTree.Services;

public sealed record MemberAvatarStorageResult(bool Success, string? FileId, string? Url);

public interface IGoogleDriveMemberAvatarService
{
    Task<MemberAvatarImageResult?> GetAvatarAsync(int memberId, string fileId, DateTime version, CancellationToken cancellationToken = default);
    Task<MemberAvatarStorageResult> UploadAvatarAsync(int memberId, byte[] content, string contentType,
        string? existingFileId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAvatarAsync(int memberId, string? fileId, CancellationToken cancellationToken = default);
}

// Storage contract only. FamilyTree's existing URL CRUD is unchanged; a future
// member upload endpoint must authorize the member and persist the returned ID.
public sealed class GoogleDriveMemberAvatarService(IConfiguration configuration, IHttpClientFactory clients,
    ILogger<GoogleDriveMemberAvatarService> logger, IMemberAvatarImageCache imageCache) : IGoogleDriveMemberAvatarService
{
    private const int MaxAvatarBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    public async Task<MemberAvatarImageResult?> GetAvatarAsync(int memberId, string fileId, DateTime version, CancellationToken cancellationToken = default)
    {
        fileId = fileId.Trim();
        if (memberId <= 0 || !ValidId(fileId)) return null;
        var key = ImageCacheKey(memberId, fileId, version);
        return await imageCache.GetOrCreateAsync(memberId, key,
            () => DownloadAvatarAsync(memberId, fileId, version, cancellationToken), cancellationToken);
    }

    internal static string ImageCacheKey(int memberId, string fileId, DateTime version) =>
        $"family-member-avatar:{memberId}:{fileId}:{version.ToUniversalTime().Ticks}";

    private async Task<MemberAvatarImageResult?> DownloadAvatarAsync(int memberId, string fileId, DateTime version, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var client = clients.CreateClient(nameof(GoogleDriveMemberAvatarService));
            // A Drive overwrite keeps the file ID stable. Include the persisted member
            // revision so Google's CDN sees a distinct URL after replacement.
            var revision = version.ToUniversalTime().Ticks;
            response = await client.GetAsync($"https://lh3.googleusercontent.com/d/{Uri.EscapeDataString(fileId)}?v={revision}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Member avatar image upstream failed MemberId={MemberId} StatusCode={StatusCode} ErrorType={ErrorType}", memberId, (int)response.StatusCode, "HttpStatus");
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mediaType is null || !AllowedAvatarTypes.Contains(mediaType) ||
                (response.Content.Headers.ContentLength is long length && (length <= 0 || length > MaxAvatarBytes)))
            {
                logger.LogWarning("Member avatar image upstream rejected MemberId={MemberId} StatusCode={StatusCode} ErrorType={ErrorType}", memberId, (int)response.StatusCode, "InvalidContentTypeOrSize");
                return null;
            }

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                if (output.Length + read > MaxAvatarBytes)
                {
                    logger.LogWarning("Member avatar image upstream rejected MemberId={MemberId} StatusCode={StatusCode} ErrorType={ErrorType}", memberId, (int)response.StatusCode, "ResponseTooLarge");
                    return null;
                }
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            var imageBytes = output.ToArray();
            if (imageBytes.Length == 0 || !HasMatchingSignature(imageBytes, mediaType))
            {
                logger.LogWarning("Member avatar image upstream rejected MemberId={MemberId} StatusCode={StatusCode} ErrorType={ErrorType}", memberId, (int)response.StatusCode, "InvalidImageBytes");
                return null;
            }

            var etagInput = System.Text.Encoding.UTF8.GetBytes($"{memberId}:{fileId}:{revision}");
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(etagInput)).ToLowerInvariant()}\"";
            return new MemberAvatarImageResult(imageBytes, mediaType, etag);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Member avatar image upstream failed MemberId={MemberId} StatusCode={StatusCode} ErrorType={ErrorType}", memberId, response is null ? 0 : (int)response.StatusCode, ex.GetType().Name);
            return null;
        }
        finally
        {
            response?.Dispose();
        }
    }

    public async Task<MemberAvatarStorageResult> UploadAvatarAsync(int memberId, byte[] content, string contentType,
        string? existingFileId, CancellationToken cancellationToken = default)
    {
        contentType = contentType.Trim().ToLowerInvariant();
        if (contentType == "image/jpg") contentType = "image/jpeg";
        existingFileId = string.IsNullOrWhiteSpace(existingFileId) ? null : existingFileId.Trim();
        if (memberId <= 0 || content.Length is <= 0 or > 5 * 1024 * 1024 ||
            !HasMatchingSignature(content, contentType) || (existingFileId is not null && !ValidId(existingFileId)))
            return new(false, null, null);
        using var response = await SendAsync(new { action = "member_avatar_upload", memberId,
            fileBase64 = Convert.ToBase64String(content), contentType, existingFileId }, cancellationToken);
        if (response is null) return new(false, null, null);
        var root = response.RootElement;
        var id = ReadString(root, "fileId");
        id = id is not null && ValidId(id) ? id : null;
        var verified = IsSuccess(root) && id is not null && (existingFileId is null || existingFileId == id) &&
            string.Equals(ReadString(root, "md5Checksum"), Convert.ToHexString(MD5.HashData(content)), StringComparison.OrdinalIgnoreCase) &&
            ReadString(root, "contentType") == contentType && ReadSize(root) == content.LongLength;
        // Only a failed first create may need compensation. Never expose an
        // existing/mismatched overwrite ID as a candidate for cleanup.
        return verified ? new(true, id, $"https://lh3.googleusercontent.com/d/{id}")
            : new(false, existingFileId is null ? id : null, null);
    }

    public async Task<bool> DeleteAvatarAsync(int memberId, string? fileId, CancellationToken cancellationToken = default)
    {
        fileId = string.IsNullOrWhiteSpace(fileId) ? null : fileId.Trim();
        if (memberId <= 0 || (fileId is not null && !ValidId(fileId))) return false;
        using var response = await SendAsync(new { action = "member_avatar_delete", memberId, fileId }, cancellationToken);
        return response is not null && IsSuccess(response.RootElement) &&
            (fileId is null || (response.RootElement.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True));
    }

    private async Task<JsonDocument?> SendAsync(object payload, CancellationToken cancellationToken)
    {
        var endpoint = configuration["GoogleDrive:WebAppUrl"];
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        try
        {
            using var response = await clients.CreateClient(nameof(GoogleDriveMemberAvatarService))
                .PostAsJsonAsync(endpoint, payload, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            logger.LogWarning("Member avatar storage request failed ({ErrorType})", ex.GetType().Name);
            return null;
        }
    }

    private static bool ValidId(string id) => id.Length is > 0 and <= 255 &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool IsSuccess(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("success", out var value) && value.ValueKind == JsonValueKind.True;
    private static string? ReadString(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long? ReadSize(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("fileSize", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var size) ? size : null;
    private static bool HasMatchingSignature(byte[] bytes, string mime) => mime switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
        "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        "image/gif" => bytes.AsSpan().StartsWith("GIF87a"u8) || bytes.AsSpan().StartsWith("GIF89a"u8),
        _ => false
    };
}

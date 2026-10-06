using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace MyLife.Features.FamilyTree.Services;

public sealed record MemberAvatarStorageResult(bool Success, string? FileId, string? Url);

public interface IGoogleDriveMemberAvatarService
{
    Task<MemberAvatarStorageResult> UploadAvatarAsync(int memberId, byte[] content, string contentType,
        string? existingFileId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAvatarAsync(int memberId, string? fileId, CancellationToken cancellationToken = default);
}

// Storage contract only. FamilyTree's existing URL CRUD is unchanged; a future
// member upload endpoint must authorize the member and persist the returned ID.
public sealed class GoogleDriveMemberAvatarService(IConfiguration configuration, IHttpClientFactory clients,
    ILogger<GoogleDriveMemberAvatarService> logger) : IGoogleDriveMemberAvatarService
{
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

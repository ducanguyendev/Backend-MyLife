using AppUser = MyLife.Shared.Entities.User;

namespace MyLife.Features.Avatar.Services;

public static class AvatarSources
{
    public const string Manual = "MANUAL";
    public const string Google = "GOOGLE";
}

public interface IGoogleAvatarSyncService
{
    Task<bool> SyncIfNeededAsync(AppUser user, string? googlePictureUrl, CancellationToken cancellationToken = default);
}

public sealed class GoogleAvatarSyncService(
    IHttpClientFactory httpClientFactory,
    IGoogleDriveAvatarService avatarStorage,
    ILogger<GoogleAvatarSyncService> logger) : IGoogleAvatarSyncService
{
    internal const int MaximumAvatarBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    public async Task<bool> SyncIfNeededAsync(AppUser user, string? googlePictureUrl, CancellationToken cancellationToken = default)
    {
        if (user.AvatarSource == AvatarSources.Manual || string.IsNullOrWhiteSpace(googlePictureUrl)) return false;
        // A legacy avatar with no trustworthy source marker is preserved. Only
        // an explicitly GOOGLE avatar or an account with no avatar is eligible.
        if (user.AvatarSource is null && !string.IsNullOrWhiteSpace(user.AvatarUrl)) return false;
        if (user.AvatarSource is not null and not AvatarSources.Google) return false;
        if (user.AvatarSource == AvatarSources.Google &&
            string.Equals(user.GoogleAvatarSourceUrl, googlePictureUrl, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(user.AvatarDriveFileId) && !string.IsNullOrWhiteSpace(user.AvatarUrl))
            return false;

        if (!TryCreateAllowedGoogleUri(googlePictureUrl, out var pictureUri))
        {
            logger.LogWarning("Rejected Google profile image URL for {UserId}", user.Id);
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pictureUri);
            using var response = await httpClientFactory.CreateClient(nameof(GoogleAvatarSyncService))
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google avatar download returned HTTP {StatusCode} for {UserId}", (int)response.StatusCode, user.Id);
                return false;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
            {
                logger.LogWarning("Google avatar returned an unsupported content type for {UserId}", user.Id);
                return false;
            }
            if (response.Content.Headers.ContentLength is > MaximumAvatarBytes)
            {
                logger.LogWarning("Google avatar exceeded the download limit for {UserId}", user.Id);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadWithLimitAsync(stream, MaximumAvatarBytes, cancellationToken);
            if (bytes is null)
            {
                logger.LogWarning("Google avatar exceeded the streaming limit for {UserId}", user.Id);
                return false;
            }

            var stored = await avatarStorage.UploadAvatarAsync(
                user.Email, bytes, contentType, user.AvatarDriveFileId, cancellationToken);
            if (!stored.Success || string.IsNullOrWhiteSpace(stored.FileId))
            {
                logger.LogWarning("Google avatar storage failed for {UserId}", user.Id);
                return false;
            }

            user.AvatarDriveFileId = stored.FileId;
            user.AvatarUrl = ToDirectDriveUrl(stored.FileId);
            user.AvatarSource = AvatarSources.Google;
            user.GoogleAvatarSourceUrl = googlePictureUrl;
            user.UpdatedAt = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Avatar mirroring is best effort and must never turn a valid Google login into a failure.
            logger.LogWarning("Could not mirror Google avatar UserId={UserId} ErrorType={ErrorType}", user.Id, ex.GetType().Name);
            return false;
        }
    }

    public static string ToDirectDriveUrl(string fileId) => $"https://lh3.googleusercontent.com/d/{fileId}";

    private static bool TryCreateAllowedGoogleUri(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri!) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        var host = uri.IdnHost;
        return host.Equals("googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("ggpht.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".ggpht.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> ReadWithLimitAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > limit) return null;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

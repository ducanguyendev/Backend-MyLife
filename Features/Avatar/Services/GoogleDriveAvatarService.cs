using System.Text;
using System.Text.Json;

namespace MyLife.Features.Avatar.Services;

public interface IGoogleDriveAvatarService
{
    Task<(bool Success, string? DriveUrl, string? FileId)> UploadAvatarAsync(string email, IFormFile file);
    Task<bool> DeleteAvatarAsync(string email);
}

public sealed class GoogleDriveAvatarService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<GoogleDriveAvatarService> logger) : IGoogleDriveAvatarService
{
    private string WebAppUrl => configuration["GoogleDrive:WebAppUrl"] ?? string.Empty;
    private static string SanitizeEmail(string email) => email.Trim().ToLowerInvariant().Replace("/", "_").Replace("\\", "_");

    public async Task<(bool Success, string? DriveUrl, string? FileId)> UploadAvatarAsync(string email, IFormFile file)
    {
        if (file.Length == 0 || string.IsNullOrWhiteSpace(WebAppUrl)) return (false, null, null);
        try
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var response = await SendAsync(new
            {
                action = "upload",
                email = SanitizeEmail(email),
                fileBase64 = Convert.ToBase64String(stream.ToArray()),
                contentType = file.ContentType
            });
            if (response is null) return (false, null, null);
            var root = response.RootElement;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False) return (false, null, null);
            var fileId = ReadString(root, "fileId") ?? ReadString(root, "id");
            var driveUrl = ReadString(root, "viewUrl") ?? ReadString(root, "downloadUrl") ??
                           ReadString(root, "fileUrl") ?? ReadString(root, "url") ?? ReadString(root, "directUrl");
            if (string.IsNullOrWhiteSpace(driveUrl) && !string.IsNullOrWhiteSpace(fileId))
                driveUrl = $"https://drive.google.com/file/d/{fileId}/view";
            return (!string.IsNullOrWhiteSpace(driveUrl) || !string.IsNullOrWhiteSpace(fileId), driveUrl, fileId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Avatar upload storage call failed");
            return (false, null, null);
        }
    }

    public async Task<bool> DeleteAvatarAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(WebAppUrl)) return false;
        try
        {
            using var response = await SendAsync(new { action = "delete", email = SanitizeEmail(email) });
            if (response is null) return false;
            var root = response.RootElement;
            return !root.TryGetProperty("success", out var success) || success.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Avatar deletion storage call failed");
            return false;
        }
    }

    private async Task<JsonDocument?> SendAsync(object payload)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, WebAppUrl) { Content = content };
        using var response = await httpClientFactory.CreateClient(nameof(GoogleDriveAvatarService)).SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Avatar storage returned HTTP {StatusCode}", (int)response.StatusCode);
            return null;
        }
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyLife.Features.Avatar.Services;
using MyLife.Shared.Data;

namespace MyLife.Features.Avatar.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AvatarController(
    IGoogleDriveAvatarService avatarService, AppDbContext db, ILogger<AvatarController> logger) : ControllerBase
{
    [HttpGet("{email}")]
    public async Task<IActionResult> GetAvatar(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return BadRequest("Email is invalid.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user is null || string.IsNullOrWhiteSpace(user.AvatarUrl))
            return NotFound(new { message = "No avatar has been set." });

        if (!Uri.TryCreate(user.AvatarUrl, UriKind.Absolute, out var avatarUri) ||
            avatarUri.Scheme is not ("http" or "https"))
            return NotFound(new { message = "Avatar is unavailable." });

        var redirectUrl = avatarUri.ToString();
        if (!string.IsNullOrWhiteSpace(user.AvatarDriveFileId) && Request.Query.TryGetValue("t", out var timestamp))
            redirectUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
                GoogleAvatarSyncService.ToDirectDriveUrl(user.AvatarDriveFileId), "t", timestamp.ToString());
        Response.Headers.CacheControl = "no-store";
        return Redirect(redirectUrl);
    }

    [Authorize]
    [HttpPost("upload")]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(email))
            return Unauthorized(new { message = "The session is invalid." });
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Please select a valid image." });
        if (file.Length > GoogleAvatarSyncService.MaximumAvatarBytes)
            return BadRequest(new { message = "The image must not exceed 5 MB." });

        var allowedMimeTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/gif", "image/jpg" };
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        if (!allowedMimeTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension))
            return BadRequest(new { message = "Only JPG, PNG, WEBP, and GIF images are supported." });

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user is null || !user.IsActive)
            return Unauthorized(new { success = false, message = "The session is invalid." });

        var existingFileId = user.AvatarDriveFileId;
        logger.LogInformation(
            "Avatar upload requested UserId={UserId} ExistingFileId={ExistingFileId} ContentType={ContentType} FileSize={FileSize}",
            user.Id, existingFileId, file.ContentType, file.Length);
        var stored = await avatarService.UploadAvatarAsync(
            normalizedEmail,
            file,
            existingFileId,
            HttpContext.RequestAborted);
        logger.LogInformation(
            "Avatar upload result UserId={UserId} ExistingFileId={ExistingFileId} ResultFileId={ResultFileId} Success={Success} ContentType={ContentType} FileSize={FileSize}",
            user.Id, existingFileId, stored.FileId, stored.Success, file.ContentType, file.Length);
        if (!stored.Success || string.IsNullOrWhiteSpace(stored.FileId) ||
            (!string.IsNullOrWhiteSpace(existingFileId) && stored.FileId != existingFileId))
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Unable to store the avatar. Please try again." });

        var stableAvatarUrl = GoogleAvatarSyncService.ToDirectDriveUrl(stored.FileId);
        user.AvatarUrl = stableAvatarUrl;
        user.AvatarDriveFileId = stored.FileId;
        user.AvatarSource = AvatarSources.Manual;
        user.GoogleAvatarSourceUrl = null;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Avatar upload persisted UserId={UserId} AvatarDriveFileId={FileId}", user.Id, user.AvatarDriveFileId);

        var cacheBustedUrl = $"{stableAvatarUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return Ok(new { message = "Avatar updated successfully.", avatarUrl = cacheBustedUrl });
    }

    [Authorize]
    [HttpDelete]
    public async Task<IActionResult> DeleteAvatar()
    {
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(email))
            return Unauthorized(new { message = "The session is invalid." });

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user is null || !user.IsActive)
            return Unauthorized(new { success = false, message = "The session is invalid." });

        var hasStoredAvatar = !string.IsNullOrWhiteSpace(user.AvatarDriveFileId) || !string.IsNullOrWhiteSpace(user.AvatarUrl);
        if (hasStoredAvatar && !await avatarService.DeleteAvatarAsync(
                normalizedEmail,
                user.AvatarDriveFileId,
                HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Avatar storage is temporarily unavailable." });

        user.AvatarUrl = null;
        user.AvatarDriveFileId = null;
        user.AvatarSource = null;
        user.GoogleAvatarSourceUrl = null;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { message = "Avatar deleted successfully." });
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyLife.Features.Library.DTOs;
using MyLife.Features.Library.Services;
using MyLife.Shared.Security;
using MyLife.Shared.Web;
using Microsoft.AspNetCore.RateLimiting;

namespace MyLife.Features.Library.Controllers;

[ApiController, Authorize(Roles = AppRoles.User), Route("api/library")]
public sealed class LibraryController(ILibraryService service, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("categories")]
    public Task<IActionResult> Categories() => Run(async (userId, ct) => Ok(await service.ListCategoriesAsync(userId, ct)));

    [HttpPost("categories")]
    public Task<IActionResult> CreateCategory(CreateLibraryCategoryDto dto) =>
        Run(async (userId, ct) => StatusCode(201, await service.CreateCategoryAsync(userId, dto, ct)));

    [HttpPut("categories/{categoryId:long}")]
    public Task<IActionResult> UpdateCategory(long categoryId, UpdateLibraryCategoryDto dto) =>
        Run(async (userId, ct) => Ok(await service.UpdateCategoryAsync(userId, categoryId, dto, ct)));

    [HttpDelete("categories/{categoryId:long}")]
    public Task<IActionResult> DeleteCategory(long categoryId) => Run(async (userId, ct) =>
    {
        await service.DeleteCategoryAsync(userId, categoryId, ct);
        return NoContent();
    });
    [HttpPost("albums")]
    public Task<IActionResult> Create(CreateAlbumDto dto) => Run(async (userId, ct) =>
    {
        var album = await service.CreateAlbumAsync(userId, dto, ct);
        return CreatedAtAction(nameof(Get), new { albumId = album.Id }, album);
    });

    [HttpGet("albums")]
    public Task<IActionResult> List() => Run(async (userId, ct) => Ok(await service.ListAlbumsAsync(userId, ct)));

    [HttpGet("overview")]
    public Task<IActionResult> Overview() => Run(async (userId, ct) => Ok(await service.GetOverviewAsync(userId, ct)));

    [HttpGet("albums/{albumId:long}")]
    public Task<IActionResult> Get(long albumId) => Run(async (userId, ct) => Ok(await service.GetAlbumAsync(userId, albumId, ct)));

    [HttpGet("photos/{photoId:long}/image")]
    public Task<IActionResult> GetPhotoImage(long photoId) => Run(async (userId, ct) =>
    {
        LibraryImageReadResult? image;
        try { image = await service.GetPhotoImageAsync(userId, photoId, ct); }
        catch (LibraryImageStorageException)
        {
            return StatusCode(502, new { success = false, code = "LIBRARY_IMAGE_UPSTREAM_FAILED", message = "The photo image is temporarily unavailable." });
        }
        if (image is null) return NotFound(new { success = false, code = "LIBRARY_PHOTO_NOT_FOUND", message = "Library photo not found." });
        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.ETag = image.ETag;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var matches = Request.Headers.IfNoneMatch.SelectMany(value => (value ?? string.Empty).Split(','))
            .Any(value => value.Trim() is "*" || string.Equals(value.Trim(), image.ETag, StringComparison.Ordinal));
        return matches ? StatusCode(StatusCodes.Status304NotModified) : File(image.Content, image.ContentType);
    });

    [HttpPut("albums/{albumId:long}")]
    public Task<IActionResult> Update(long albumId, UpdateAlbumDto dto) => Run(async (userId, ct) => Ok(await service.UpdateAlbumAsync(userId, albumId, dto, ct)));

    [EnableRateLimiting(WebRateLimits.Upload)]
    [HttpPost("albums/{albumId:long}/photos")]
    [RequestSizeLimit(LibraryUploadRules.MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = LibraryUploadRules.MaximumRequestBytes)]
    public Task<IActionResult> Upload(long albumId, [FromForm] List<IFormFile> files, [FromForm] LibraryPhotoMetadataDto metadata) => Run(async (userId, ct) =>
        StatusCode(201, await service.UploadPhotosAsync(userId, albumId, files, metadata, ct)));

    [HttpPut("photos/{photoId:long}")]
    public Task<IActionResult> UpdatePhoto(long photoId, LibraryPhotoMetadataDto metadata) =>
        Run(async (userId, ct) => Ok(await service.UpdatePhotoAsync(userId, photoId, metadata, ct)));

    [HttpDelete("photos/{photoId:long}")]
    public Task<IActionResult> DeletePhoto(long photoId) => Run(async (userId, ct) =>
    {
        await service.DeletePhotoAsync(userId, photoId, ct);
        return NoContent();
    });

    [HttpDelete("albums/{albumId:long}")]
    public Task<IActionResult> DeleteAlbum(long albumId) => Run(async (userId, ct) =>
    {
        await service.DeleteAlbumAsync(userId, albumId, ct);
        return NoContent();
    });

    private async Task<IActionResult> Run(Func<int, CancellationToken, Task<IActionResult>> action)
    {
        var id = await currentUser.GetUserIdAsync(HttpContext.RequestAborted);
        if (id is null) return Unauthorized(new { success = false, code = "AUTH_SESSION_INVALID", message = "The session is invalid." });
        try { return await action(id.Value, HttpContext.RequestAborted); }
        catch (LibraryOperationException ex) { return StatusCode(ex.StatusCode, new { success = false, code = ex.Code, message = ex.Message }); }
    }
}

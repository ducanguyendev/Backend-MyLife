using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Features.FamilyTree.Services;
using MyLife.Shared.Security;
using MyLife.Shared.Web;
using Microsoft.AspNetCore.RateLimiting;

namespace MyLife.Features.FamilyTree.Controllers;

[ApiController]
[Route("api/family-tree")]
[Authorize(Roles = AppRoles.User)]
public sealed class FamilyTreeController(IFamilyTreeService service, ICurrentUserService currentUser, FamilyMapService? mapService = null,
    IFamilyRelationshipService? relationshipService = null, IFamilyTimelineService? timelineService = null,
    IFamilyMemberProfileService? profileService = null) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll() => Run(async (userId, ct) => Ok(new { success = true, data = await service.GetAllMembersAsync(userId, ct) }));
    [HttpGet("generations")]
    public Task<IActionResult> Generations() => Run(async (_, ct) => Ok(new { success = true, data = await service.GetAllGenerationsAsync(ct) }));
    [HttpGet("map")]
    public Task<IActionResult> Map() => Run(async (userId, ct) => Ok(new { success = true, data = await mapService!.GetMapAsync(userId, ct) }));
    [HttpPost("map/retry-geocoding")]
    public Task<IActionResult> RetryMapGeocoding() => Run(async (userId, ct) =>
        Ok(new { success = true, data = await mapService!.RetryFailedGeocodingAsync(userId, ct) }));
    [HttpGet("relationship")]
    public Task<IActionResult> Relationship([FromQuery] int fromMemberId, [FromQuery] int toMemberId) => Run(async (userId, ct) =>
    {
        if (fromMemberId <= 0 || toMemberId <= 0) return BadRequest(Error("Member IDs must be positive.", "FAMILY_RELATIONSHIP_INVALID_MEMBER_ID"));
        var result = await relationshipService!.FindAsync(userId, fromMemberId, toMemberId, ct);
        return result is null ? NotFound(Error("One or both members were not found in this family tree.", "FAMILY_RELATIONSHIP_MEMBER_NOT_FOUND")) : Ok(new { success = true, data = result });
    });
    [HttpGet("{id:int}/profile")]
    public Task<IActionResult> Profile(int id) => Run(async (userId, ct) =>
    {
        var result = await profileService!.GetAsync(userId, id, ct);
        return result is null ? NotFound(Error("Family member not found.")) : Ok(new { success = true, data = result });
    });
    [HttpGet("timeline")]
    public Task<IActionResult> Timeline() => Run(async (userId, ct) => Ok(new { success = true, data = await timelineService!.ListAsync(userId, null, ct) }));
    [HttpGet("{memberId:int}/timeline")]
    public Task<IActionResult> MemberTimeline(int memberId) => Run(async (userId, ct) => Ok(new { success = true, data = await timelineService!.ListAsync(userId, memberId, ct) }));
    [HttpPost("timeline")]
    public Task<IActionResult> CreateTimelineEvent(FamilyTimelineWriteDto dto) => Run(async (userId, ct) => StatusCode(201, new { success = true, data = await timelineService!.CreateAsync(userId, dto, ct) }));
    [HttpPut("timeline/{id:long}")]
    public Task<IActionResult> UpdateTimelineEvent(long id, FamilyTimelineWriteDto dto) => Run(async (userId, ct) =>
    {
        var item = await timelineService!.UpdateAsync(userId, id, dto, ct);
        return item is null ? NotFound(Error("Timeline event not found.", "FAMILY_TIMELINE_EVENT_NOT_FOUND")) : Ok(new { success = true, data = item });
    });
    [HttpDelete("timeline/{id:long}")]
    public Task<IActionResult> DeleteTimelineEvent(long id) => Run(async (userId, ct) =>
        await timelineService!.DeleteAsync(userId, id, ct) ? NoContent() : NotFound(Error("Timeline event not found.", "FAMILY_TIMELINE_EVENT_NOT_FOUND")));
    [HttpGet("{id:int}")]
    public Task<IActionResult> Get(int id) => Run(async (userId, ct) =>
    {
        var member = await service.GetMemberByIdAsync(userId, id, ct);
        return member is null ? NotFound(Error("Family member not found.")) : Ok(new { success = true, data = member });
    });
    [HttpPost]
    public Task<IActionResult> Create(CreateFamilyMemberDto dto) => Run(async (userId, ct) => StatusCode(201, new { success = true, data = await service.CreateMemberAsync(userId, dto, ct) }));
    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, UpdateFamilyMemberDto dto) => Run(async (userId, ct) => Ok(new { success = true, data = await service.UpdateMemberAsync(userId, id, dto, ct) }));
    [HttpPut("{id:int}/location")]
    public Task<IActionResult> UpdateLocation(int id, UpdateFamilyMemberLocationDto dto) => Run(async (userId, ct) =>
        Ok(new { success = true, data = await service.UpdateMemberLocationAsync(userId, id, dto, ct) }));
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id) => Run(async (userId, ct) => await service.DeleteMemberAsync(userId, id, ct)
        ? Ok(new { success = true, message = "Family member deleted." }) : NotFound(Error("Family member not found.")));

    [HttpPost("{id:int}/avatar"), EnableRateLimiting(WebRateLimits.Upload)]
    [RequestSizeLimit(6L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6L * 1024 * 1024)]
    public Task<IActionResult> UploadAvatar(int id, [FromForm] IFormFile? file) => Run(async (userId, ct) =>
    {
        // Ownership is checked before reading the supplied multipart body or calling storage.
        if (await service.GetMemberByIdAsync(userId, id, ct) is null) return NotFound(Error("Family member not found."));
        if (file is null) return BadRequest(Error("An image file is required.", "FAMILY_AVATAR_INVALID"));
        if (file.Length is <= 0 or > 5 * 1024 * 1024) return BadRequest(Error("The avatar file must be between 1 byte and 5 MiB.", "FAMILY_AVATAR_INVALID"));
        var mime = file.ContentType.Trim().ToLowerInvariant() is "image/jpg" ? "image/jpeg" : file.ContentType.Trim().ToLowerInvariant();
        if (mime is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif"))
            return BadRequest(Error("Only JPG, PNG, WebP and GIF images are supported.", "FAMILY_AVATAR_INVALID"));
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, ct);
        if (buffer.Length != file.Length || buffer.Length > 5 * 1024 * 1024)
            return BadRequest(Error("The avatar file must be between 1 byte and 5 MiB.", "FAMILY_AVATAR_INVALID"));
        return Ok(new { success = true, data = await service.UploadMemberAvatarAsync(userId, id, buffer.ToArray(), mime, ct) });
    });

    [HttpDelete("{id:int}/avatar")]
    public Task<IActionResult> DeleteAvatar(int id) => Run(async (userId, ct) =>
        Ok(new { success = true, data = await service.DeleteMemberAvatarAsync(userId, id, ct) }));

    [HttpGet("{id:int}/avatar/image")]
    public Task<IActionResult> GetAvatarImage(int id) => Run(async (userId, ct) =>
    {
        MemberAvatarImageResult? image;
        try { image = await service.GetMemberAvatarImageAsync(userId, id, ct); }
        catch (FamilyTreeStorageException)
        {
            return StatusCode(502, Error("Member avatar image is temporarily unavailable.", "MEMBER_AVATAR_UPSTREAM_FAILED"));
        }
        if (image is null) return NotFound(Error("Member avatar not found.", "MEMBER_AVATAR_NOT_FOUND"));

        Response.Headers.CacheControl = "private, no-cache";
        Response.Headers.ETag = image.ETag;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var matches = Request.Headers.IfNoneMatch
            .SelectMany(value => (value ?? string.Empty).Split(','))
            .Any(value => value.Trim() is "*" || string.Equals(value.Trim(), image.ETag, StringComparison.Ordinal));
        return matches ? StatusCode(StatusCodes.Status304NotModified) : File(image.Content, image.ContentType);
    });

    private async Task<IActionResult> Run(Func<int, CancellationToken, Task<IActionResult>> operation)
    {
        var ct = HttpContext.RequestAborted;
        var userId = await currentUser.GetUserIdAsync(ct);
        if (userId is null) return Unauthorized(Error("The session is invalid.", "AUTH_SESSION_INVALID"));
        try { return await operation(userId.Value, ct); }
        catch (KeyNotFoundException) { return NotFound(Error("Family member not found.")); }
        catch (ArgumentException ex) { return BadRequest(Error(ex.Message, ex is FamilyTreeValidationException validation ? validation.Code : "FAMILY_VALIDATION_FAILED")); }
        catch (FamilyTreeStorageException) { return StatusCode(502, Error("Member avatar storage is unavailable.", "FAMILY_AVATAR_STORAGE_FAILED")); }
        catch (Exception ex) when (HttpContext.Request.Path.Value?.EndsWith("/avatar", StringComparison.OrdinalIgnoreCase) == true && ex is not OperationCanceledException)
        {
            return StatusCode(502, Error("Member avatar storage is unavailable.", "FAMILY_AVATAR_STORAGE_FAILED"));
        }
    }
    private static object Error(string message, string code = "FAMILY_MEMBER_NOT_FOUND") => new { success = false, code, message };
}

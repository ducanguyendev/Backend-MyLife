using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Features.FamilyTree.Services;
using MyLife.Shared.Security;

namespace MyLife.Features.FamilyTree.Controllers;

[ApiController]
[Route("api/family-tree/anniversaries")]
[Authorize(Roles = AppRoles.User)]
public sealed class FamilyAnniversaryController(IFamilyAnniversaryService service, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => Run(async userId =>
        Ok(new { success = true, data = await service.ListAsync(userId, ct) }));

    [HttpGet("lunar-to-solar")]
    public Task<IActionResult> LunarToSolar([FromQuery] int day, [FromQuery] int month, [FromQuery] int year, [FromQuery] bool isLeapMonth = false) => Run(_ =>
    {
        try { return Task.FromResult<IActionResult>(Ok(new { success = true, data = service.ConvertLunarToSolar(day, month, year, isLeapMonth) })); }
        catch (FamilyAnniversaryValidationException ex) { return Task.FromResult<IActionResult>(BadRequest(Error(ex.Code, ex.Message))); }
    });

    [HttpGet("next-occurrence")]
    public Task<IActionResult> NextOccurrence([FromQuery] int day, [FromQuery] int month, [FromQuery] bool isLeapMonth = false) => Run(_ =>
    {
        try { return Task.FromResult<IActionResult>(Ok(new { success = true, data = service.GetNextOccurrence(day, month, isLeapMonth) })); }
        catch (FamilyAnniversaryValidationException ex) { return Task.FromResult<IActionResult>(BadRequest(Error(ex.Code, ex.Message))); }
    });

    [HttpGet("{id:long}")]
    public Task<IActionResult> Get(long id, CancellationToken ct) => Run(async userId =>
    {
        var item = await service.GetAsync(userId, id, ct);
        return item is null ? NotFound(Error("FAMILY_ANNIVERSARY_NOT_FOUND", "Anniversary not found.")) : Ok(new { success = true, data = item });
    });

    [HttpPost]
    public Task<IActionResult> Create(FamilyAnniversaryWriteDto dto, CancellationToken ct) => Run(async userId =>
        StatusCode(201, new { success = true, data = await service.CreateAsync(userId, dto, ct) }));

    [HttpPut("{id:long}")]
    public Task<IActionResult> Update(long id, FamilyAnniversaryWriteDto dto, CancellationToken ct) => Run(async userId =>
    {
        var item = await service.UpdateAsync(userId, id, dto, ct);
        return item is null ? NotFound(Error("FAMILY_ANNIVERSARY_NOT_FOUND", "Anniversary not found.")) : Ok(new { success = true, data = item });
    });

    [HttpDelete("{id:long}")]
    public Task<IActionResult> Delete(long id, CancellationToken ct) => Run(async userId =>
        await service.DeleteAsync(userId, id, ct) ? NoContent() : NotFound(Error("FAMILY_ANNIVERSARY_NOT_FOUND", "Anniversary not found.")));

    private async Task<IActionResult> Run(Func<int, Task<IActionResult>> action)
    {
        var userId = await currentUser.GetUserIdAsync(HttpContext.RequestAborted);
        if (userId is null) return Unauthorized(Error("AUTH_SESSION_INVALID", "The session is invalid."));
        try { return await action(userId.Value); }
        catch (KeyNotFoundException) { return NotFound(Error("FAMILY_MEMBER_NOT_FOUND", "Related family member not found.")); }
        catch (FamilyAnniversaryValidationException ex) { return BadRequest(Error(ex.Code, ex.Message)); }
    }

    private static object Error(string code, string message) => new { success = false, code, message };
}

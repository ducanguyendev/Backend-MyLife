using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Features.FamilyTree.Services;
using MyLife.Shared.Security;

namespace MyLife.Features.FamilyTree.Controllers;

[ApiController]
[Route("api/family-tree")]
[Authorize(Roles = AppRoles.User)]
public sealed class FamilyTreeController(IFamilyTreeService service, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetAll() => Run(async (userId, ct) => Ok(new { success = true, data = await service.GetAllMembersAsync(userId, ct) }));
    [HttpGet("generations")]
    public Task<IActionResult> Generations() => Run(async (_, ct) => Ok(new { success = true, data = await service.GetAllGenerationsAsync(ct) }));
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
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id) => Run(async (userId, ct) => await service.DeleteMemberAsync(userId, id, ct)
        ? Ok(new { success = true, message = "Family member deleted." }) : NotFound(Error("Family member not found.")));

    private async Task<IActionResult> Run(Func<int, CancellationToken, Task<IActionResult>> operation)
    {
        var ct = HttpContext.RequestAborted;
        var userId = await currentUser.GetUserIdAsync(ct);
        if (userId is null) return Unauthorized(Error("The session is invalid.", "AUTH_SESSION_INVALID"));
        try { return await operation(userId.Value, ct); }
        catch (KeyNotFoundException) { return NotFound(Error("Family member not found.")); }
        catch (ArgumentException ex) { return BadRequest(Error(ex.Message, ex is FamilyTreeValidationException validation ? validation.Code : "FAMILY_VALIDATION_FAILED")); }
    }
    private static object Error(string message, string code = "FAMILY_MEMBER_NOT_FOUND") => new { success = false, code, message };
}

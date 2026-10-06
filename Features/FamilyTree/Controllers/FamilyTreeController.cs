using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Features.FamilyTree.Services;
using MyLife.Shared.Security;

namespace MyLife.Features.FamilyTree.Controllers;

[ApiController]
[Route("api/family-tree")]
[Authorize]
public sealed class FamilyTreeController(IFamilyTreeService service) : FamilyTreeControllerBase(service);

[ApiController]
[Route("api/admin/family-tree")]
[Authorize(Roles = AppRoles.Admin)]
public sealed class AdminFamilyTreeController(IFamilyTreeService service) : FamilyTreeControllerBase(service);

// Preserve both route families without applying a weaker policy to the admin alias.
public abstract class FamilyTreeControllerBase(IFamilyTreeService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> GetAll() => Ok(new { success = true, data = await service.GetAllMembersAsync() });
    [HttpGet("generations")] public async Task<IActionResult> Generations() => Ok(new { success = true, data = await service.GetAllGenerationsAsync() });
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id) { var member = await service.GetMemberByIdAsync(id); return member is null ? NotFound(Error("Family member not found.")) : Ok(new { success = true, data = member }); }
    [HttpPost] public async Task<IActionResult> Create(CreateFamilyMemberDto dto) => await Run(() => service.CreateMemberAsync(dto), created: true);
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, UpdateFamilyMemberDto dto) => await Run(() => service.UpdateMemberAsync(id, dto));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id) { var deleted = await service.DeleteMemberAsync(id); return deleted ? Ok(new { success = true, message = "Family member deleted." }) : NotFound(Error("Family member not found.")); }
    private async Task<IActionResult> Run<T>(Func<Task<T>> operation, bool created = false)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var value = await operation(); return created ? StatusCode(201, new { success = true, data = value }) : Ok(new { success = true, data = value }); }
        catch (KeyNotFoundException) { return NotFound(Error("Family member not found.")); }
        catch (ArgumentException ex) { return BadRequest(Error(ex.Message, ex is FamilyTreeValidationException validation ? validation.Code : "FAMILY_VALIDATION_FAILED")); }
    }
    private static object Error(string message, string code = "FAMILY_MEMBER_NOT_FOUND") => new { success = false, code, message };
}

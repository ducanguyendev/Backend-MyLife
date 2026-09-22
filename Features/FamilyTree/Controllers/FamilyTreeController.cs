using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Features.FamilyTree.Services;

namespace MyLife.Features.FamilyTree.Controllers
{
    [ApiController]
    [Route("api/family-tree")]
    [Route("api/admin/family-tree")]
    [Authorize]
    public class FamilyTreeController : ControllerBase
    {
        private readonly IFamilyTreeService _familyTreeService;

        public FamilyTreeController(IFamilyTreeService familyTreeService)
        {
            _familyTreeService = familyTreeService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<FamilyMemberDto>>> GetAllMembers()
        {
            try
            {
                var members = await _familyTreeService.GetAllMembersAsync();
                return Ok(new { success = true, data = members });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("generations")]
        public async Task<ActionResult<IEnumerable<GenerationDto>>> GetGenerations()
        {
            try
            {
                var generations = await _familyTreeService.GetAllGenerationsAsync();
                return Ok(new { success = true, data = generations });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<FamilyMemberDto>> GetMemberById(int id)
        {
            try
            {
                var member = await _familyTreeService.GetMemberByIdAsync(id);
                if (member == null)
                    return NotFound(new { success = false, message = "Không tìm thấy thành viên" });

                return Ok(new { success = true, data = member });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult<FamilyMemberDto>> CreateMember([FromBody] CreateFamilyMemberDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var createdMember = await _familyTreeService.CreateMemberAsync(dto);
                return CreatedAtAction(nameof(GetMemberById), new { id = createdMember.Id }, new { success = true, data = createdMember });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<FamilyMemberDto>> UpdateMember(int id, [FromBody] UpdateFamilyMemberDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                var updatedMember = await _familyTreeService.UpdateMemberAsync(id, dto);
                return Ok(new { success = true, data = updatedMember });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { success = false, message = "Không tìm thấy thành viên" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteMember(int id)
        {
            try
            {
                var deleted = await _familyTreeService.DeleteMemberAsync(id);
                if (!deleted)
                    return NotFound(new { success = false, message = "Không tìm thấy thành viên" });

                return Ok(new { success = true, message = "Đã xóa thành viên thành công" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}

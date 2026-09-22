using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyLife.Features.FamilyTree.DTOs;

namespace MyLife.Features.FamilyTree.Services
{
    public interface IFamilyTreeService
    {
        Task<IEnumerable<FamilyMemberDto>> GetAllMembersAsync();
        Task<FamilyMemberDto?> GetMemberByIdAsync(int id);
        Task<FamilyMemberDto> CreateMemberAsync(CreateFamilyMemberDto dto);
        Task<FamilyMemberDto> UpdateMemberAsync(int id, UpdateFamilyMemberDto dto);
        Task<bool> DeleteMemberAsync(int id);
        Task<IEnumerable<GenerationDto>> GetAllGenerationsAsync();
    }
}

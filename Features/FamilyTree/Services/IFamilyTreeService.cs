using MyLife.Features.FamilyTree.DTOs;
using UserFamilyTree = MyLife.Shared.Entities.FamilyTree;

namespace MyLife.Features.FamilyTree.Services;

public interface IFamilyTreeService
{
    Task<UserFamilyTree> EnsureUserFamilyTreeAsync(int userId, CancellationToken ct);
    Task<IEnumerable<FamilyMemberDto>> GetAllMembersAsync(int userId, CancellationToken ct);
    Task<FamilyMemberDto?> GetMemberByIdAsync(int userId, int memberId, CancellationToken ct);
    Task<FamilyMemberDto> CreateMemberAsync(int userId, CreateFamilyMemberDto dto, CancellationToken ct);
    Task<FamilyMemberDto> UpdateMemberAsync(int userId, int memberId, UpdateFamilyMemberDto dto, CancellationToken ct);
    Task<FamilyMemberDto> UpdateMemberLocationAsync(int userId, int memberId, UpdateFamilyMemberLocationDto dto, CancellationToken ct);
    Task<FamilyMemberDto> UploadMemberAvatarAsync(int userId, int memberId, byte[] content, string contentType, CancellationToken ct);
    Task<FamilyMemberDto> DeleteMemberAvatarAsync(int userId, int memberId, CancellationToken ct);
    Task<MemberAvatarImageResult?> GetMemberAvatarImageAsync(int userId, int memberId, CancellationToken ct);
    Task<bool> DeleteMemberAsync(int userId, int memberId, CancellationToken ct);
    Task<IEnumerable<GenerationDto>> GetAllGenerationsAsync(CancellationToken ct);
}

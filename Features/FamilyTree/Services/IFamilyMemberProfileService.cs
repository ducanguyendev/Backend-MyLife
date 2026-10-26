using MyLife.Features.FamilyTree.DTOs;

namespace MyLife.Features.FamilyTree.Services;

public interface IFamilyMemberProfileService
{
    Task<FamilyMemberProfileDto?> GetAsync(int userId, int memberId, CancellationToken ct);
}

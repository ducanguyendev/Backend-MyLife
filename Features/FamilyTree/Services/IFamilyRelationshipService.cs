using MyLife.Features.FamilyTree.DTOs;

namespace MyLife.Features.FamilyTree.Services;

public interface IFamilyRelationshipService
{
    Task<FamilyRelationshipResultDto?> FindAsync(int userId, int fromMemberId, int toMemberId, CancellationToken ct);
}

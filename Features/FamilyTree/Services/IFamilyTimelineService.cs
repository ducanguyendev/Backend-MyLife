using MyLife.Features.FamilyTree.DTOs;

namespace MyLife.Features.FamilyTree.Services;

public interface IFamilyTimelineService
{
    Task<IReadOnlyList<FamilyTimelineEventDto>> ListAsync(int userId, int? memberId, CancellationToken ct);
    Task<FamilyTimelineEventDto> CreateAsync(int userId, FamilyTimelineWriteDto dto, CancellationToken ct);
    Task<FamilyTimelineEventDto?> UpdateAsync(int userId, long id, FamilyTimelineWriteDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int userId, long id, CancellationToken ct);
}

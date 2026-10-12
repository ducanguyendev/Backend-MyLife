using MyLife.Features.FamilyTree.DTOs;

namespace MyLife.Features.FamilyTree.Services;

public interface IFamilyAnniversaryService
{
    Task<IReadOnlyList<FamilyAnniversaryDto>> ListAsync(int userId, CancellationToken ct);
    Task<FamilyAnniversaryDto?> GetAsync(int userId, long id, CancellationToken ct);
    Task<FamilyAnniversaryDto> CreateAsync(int userId, FamilyAnniversaryWriteDto dto, CancellationToken ct);
    Task<FamilyAnniversaryDto?> UpdateAsync(int userId, long id, FamilyAnniversaryWriteDto dto, CancellationToken ct);
    Task<bool> DeleteAsync(int userId, long id, CancellationToken ct);
    LunarToSolarDto ConvertLunarToSolar(int day, int month, int year, bool isLeapMonth);
}

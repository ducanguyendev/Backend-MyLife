using Microsoft.EntityFrameworkCore;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;

namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyTimelineService(AppDbContext db, IFamilyTreeService trees, IFamilyAnniversaryService anniversaries) : IFamilyTimelineService
{
    private static readonly HashSet<string> EventTypes = ["BIRTH", "DEATH", "MARRIAGE", "CHILD_BIRTH", "ADDRESS_CHANGE", "ANNIVERSARY", "MEMORY", "CUSTOM"];

    public async Task<IReadOnlyList<FamilyTimelineEventDto>> ListAsync(int userId, int? memberId, CancellationToken ct)
    {
        var tree = await trees.EnsureUserFamilyTreeAsync(userId, ct);
        if (memberId.HasValue && !await db.FamilyMembers.AnyAsync(m => m.Id == memberId && m.FamilyTreeId == tree.Id, ct)) throw new KeyNotFoundException();
        var members = await db.FamilyMembers.AsNoTracking().Where(m => m.FamilyTreeId == tree.Id).ToListAsync(ct);
        var custom = await Query(tree.Id).ToListAsync(ct);
        var items = custom.Select(Map).Concat(Derived(members));
        var anniversaryItems = await anniversaries.ListAsync(userId, ct);
        items = items.Concat(anniversaryItems.Where(a => a.OriginalSolarDate.HasValue)
            .Select(a => new FamilyTimelineEventDto(-a.Id, a.RelatedMemberId, a.RelatedMemberName, "ANNIVERSARY", a.Title,
                a.Notes, a.OriginalSolarDate, a.OriginalSolarDate!.Value.Year, false, a.Place, null, null, null, "ANNIVERSARY", a.Id, true)));
        if (memberId.HasValue) items = items.Where(e => e.MemberId == memberId || e.RelatedMemberId == memberId);
        return items.OrderBy(e => e.EventDate.HasValue ? 0 : e.EventYear.HasValue ? 1 : 2)
            .ThenBy(e => e.EventDate?.DayNumber ?? e.EventYear ?? int.MaxValue).ThenBy(e => e.Id).ToArray();
    }

    public async Task<FamilyTimelineEventDto> CreateAsync(int userId, FamilyTimelineWriteDto dto, CancellationToken ct)
    {
        var tree = await trees.EnsureUserFamilyTreeAsync(userId, ct);
        var values = await Validate(tree.Id, dto, ct);
        var now = DateTime.UtcNow;
        var entity = new FamilyTimelineEvent { FamilyTreeId = tree.Id, MemberId = values.MemberId, EventType = values.Type, Title = values.Title,
            Description = values.Description, EventDate = dto.EventDate, EventYear = dto.EventDate?.Year ?? dto.EventYear, IsApproximate = dto.IsApproximate,
            Location = values.Location, Latitude = dto.Latitude, Longitude = dto.Longitude, RelatedMemberId = values.RelatedMemberId, CreatedAt = now, UpdatedAt = now };
        db.FamilyTimelineEvents.Add(entity); await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<FamilyTimelineEventDto?> UpdateAsync(int userId, long id, FamilyTimelineWriteDto dto, CancellationToken ct)
    {
        var tree = await trees.EnsureUserFamilyTreeAsync(userId, ct);
        var entity = await db.FamilyTimelineEvents.SingleOrDefaultAsync(e => e.Id == id && e.FamilyTreeId == tree.Id, ct);
        if (entity is null) return null;
        var values = await Validate(tree.Id, dto, ct);
        entity.MemberId = values.MemberId; entity.EventType = values.Type; entity.Title = values.Title; entity.Description = values.Description;
        entity.EventDate = dto.EventDate; entity.EventYear = dto.EventDate?.Year ?? dto.EventYear; entity.IsApproximate = dto.IsApproximate; entity.Location = values.Location;
        entity.Latitude = dto.Latitude; entity.Longitude = dto.Longitude; entity.RelatedMemberId = values.RelatedMemberId; entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Map(entity);
    }

    public async Task<bool> DeleteAsync(int userId, long id, CancellationToken ct)
    {
        var tree = await trees.EnsureUserFamilyTreeAsync(userId, ct);
        var entity = await db.FamilyTimelineEvents.SingleOrDefaultAsync(e => e.Id == id && e.FamilyTreeId == tree.Id, ct);
        if (entity is null) return false;
        db.FamilyTimelineEvents.Remove(entity); await db.SaveChangesAsync(ct); return true;
    }

    private IQueryable<FamilyTimelineEvent> Query(long treeId) => db.FamilyTimelineEvents.AsNoTracking().Where(e => e.FamilyTreeId == treeId).Include(e => e.Member);
    private static IEnumerable<FamilyTimelineEventDto> Derived(IEnumerable<FamilyMember> members) => members.SelectMany(member =>
    {
        var events = new List<FamilyTimelineEventDto>();
        if (member.DateOfBirth is DateOnly birth) events.Add(new(-member.Id * 2L, member.Id, member.FullName, "BIRTH", $"Birth of {member.FullName}", null, birth, birth.Year, false, null, null, null, null, "FAMILY_MEMBER", member.Id, true));
        if (member.LifeStatus == "DECEASED" && member.DeathDate is DateOnly death) events.Add(new(-member.Id * 2L - 1, member.Id, member.FullName, "DEATH", $"Death of {member.FullName}", null, death, death.Year, false, null, null, null, null, "FAMILY_MEMBER", member.Id, true));
        return events;
    });
    private static FamilyTimelineEventDto Map(FamilyTimelineEvent e) => new(e.Id, e.MemberId, e.Member?.FullName, e.EventType, e.Title, e.Description,
        e.EventDate, e.EventYear, e.IsApproximate, e.Location, e.Latitude, e.Longitude, e.RelatedMemberId, e.SourceType, e.SourceId, false);

    private async Task<(int? MemberId, int? RelatedMemberId, string Type, string Title, string? Description, string? Location)> Validate(long treeId, FamilyTimelineWriteDto dto, CancellationToken ct)
    {
        var type = dto.EventType?.Trim().ToUpperInvariant() ?? "CUSTOM";
        var title = dto.Title?.Trim() ?? "";
        if (!EventTypes.Contains(type) || title.Length is < 2 or > 200) throw new ArgumentException("A supported event type and a 2–200 character title are required.");
        if (dto.EventDate is null && dto.EventYear is null || dto.EventYear is < 1 or > 9999) throw new ArgumentException("Provide a valid event date or year.");
        if ((dto.Latitude is null) != (dto.Longitude is null) || dto.Latitude is double lat && (!double.IsFinite(lat) || lat is < -90 or > 90) || dto.Longitude is double lon && (!double.IsFinite(lon) || lon is < -180 or > 180)) throw new ArgumentException("Coordinates must be supplied together and be valid.");
        if (dto.Description?.Length > 2000 || dto.Location?.Length > 500) throw new ArgumentException("Event description or location exceeds its maximum length.");
        if (dto.MemberId is int memberId && !await db.FamilyMembers.AnyAsync(m => m.Id == memberId && m.FamilyTreeId == treeId, ct)) throw new KeyNotFoundException();
        if (dto.RelatedMemberId is int relatedId && !await db.FamilyMembers.AnyAsync(m => m.Id == relatedId && m.FamilyTreeId == treeId, ct)) throw new KeyNotFoundException();
        return (dto.MemberId, dto.RelatedMemberId, type, title, Normalize(dto.Description), Normalize(dto.Location));
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using Microsoft.EntityFrameworkCore;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;

namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyAnniversaryService(
    AppDbContext db,
    IFamilyTreeService familyTrees,
    VietnameseLunarCalendarService calendar,
    TimeProvider timeProvider) : IFamilyAnniversaryService
{
    private static readonly HashSet<string> EventTypes = ["DEATH_ANNIVERSARY", "FAMILY_EVENT"];

    public async Task<IReadOnlyList<FamilyAnniversaryDto>> ListAsync(int userId, CancellationToken ct)
    {
        var tree = await familyTrees.EnsureUserFamilyTreeAsync(userId, ct);
        var today = calendar.GetVietnamToday();
        var anniversaries = await Query(tree.Id).OrderBy(a => a.Id).ToListAsync(ct);
        return anniversaries.Select(a => Map(a, today)).OrderBy(a => a.DaysUntil).ThenBy(a => a.Id).ToArray();
    }

    public async Task<FamilyAnniversaryDto?> GetAsync(int userId, long id, CancellationToken ct)
    {
        var tree = await familyTrees.EnsureUserFamilyTreeAsync(userId, ct);
        var anniversary = await Query(tree.Id).SingleOrDefaultAsync(a => a.Id == id, ct);
        return anniversary is null ? null : Map(anniversary, calendar.GetVietnamToday());
    }

    public async Task<FamilyAnniversaryDto> CreateAsync(int userId, FamilyAnniversaryWriteDto dto, CancellationToken ct)
    {
        var tree = await familyTrees.EnsureUserFamilyTreeAsync(userId, ct);
        var values = Validate(dto);
        ValidateOriginalDate(values.EventType, dto);
        var member = await ResolveMember(tree.Id, dto.RelatedMemberId, ct);
        ValidateRelatedMember(values.EventType, dto.OriginalSolarDate, member);
        EnsureRecurringDate(values.LunarDay, values.LunarMonth, dto.IsLeapMonth);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entity = new FamilyAnniversary
        {
            FamilyTreeId = tree.Id, RelatedMemberId = member?.Id, EventType = values.EventType,
            Title = values.Title, LunarDay = values.LunarDay, LunarMonth = values.LunarMonth,
            IsLeapMonth = dto.IsLeapMonth, OriginalSolarDate = dto.OriginalSolarDate, LunarYear = dto.LunarYear,
            Place = values.Place, Host = values.Host, Notes = values.Notes,
            CreatedAt = now, UpdatedAt = now,
        };
        db.FamilyAnniversaries.Add(entity);
        await db.SaveChangesAsync(ct);
        return Map(await Query(tree.Id).SingleAsync(a => a.Id == entity.Id, ct), calendar.GetVietnamToday());
    }

    public async Task<FamilyAnniversaryDto?> UpdateAsync(int userId, long id, FamilyAnniversaryWriteDto dto, CancellationToken ct)
    {
        var tree = await familyTrees.EnsureUserFamilyTreeAsync(userId, ct);
        var entity = await db.FamilyAnniversaries.SingleOrDefaultAsync(a => a.Id == id && a.FamilyTreeId == tree.Id, ct);
        if (entity is null) return null;
        var values = Validate(dto);
        ValidateOriginalDate(values.EventType, dto);
        var member = await ResolveMember(tree.Id, dto.RelatedMemberId, ct);
        ValidateRelatedMember(values.EventType, dto.OriginalSolarDate, member);
        EnsureRecurringDate(values.LunarDay, values.LunarMonth, dto.IsLeapMonth);
        entity.EventType = values.EventType; entity.Title = values.Title; entity.RelatedMemberId = member?.Id;
        entity.LunarDay = values.LunarDay; entity.LunarMonth = values.LunarMonth; entity.IsLeapMonth = dto.IsLeapMonth;
        entity.OriginalSolarDate = dto.OriginalSolarDate; entity.LunarYear = dto.LunarYear;
        entity.Place = values.Place; entity.Host = values.Host; entity.Notes = values.Notes; entity.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Map(await Query(tree.Id).SingleAsync(a => a.Id == id, ct), calendar.GetVietnamToday());
    }

    public async Task<bool> DeleteAsync(int userId, long id, CancellationToken ct)
    {
        var tree = await familyTrees.EnsureUserFamilyTreeAsync(userId, ct);
        var entity = await db.FamilyAnniversaries.SingleOrDefaultAsync(a => a.Id == id && a.FamilyTreeId == tree.Id, ct);
        if (entity is null) return false;
        db.FamilyAnniversaries.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public LunarToSolarDto ConvertLunarToSolar(int day, int month, int year, bool isLeapMonth)
    {
        try { return new(day, month, year, isLeapMonth, calendar.ConvertLunarToSolar(day, month, year, isLeapMonth)); }
        catch (ArgumentException ex) { throw InvalidLunarDate(ex); }
    }

    public NextAnniversaryOccurrenceDto GetNextOccurrence(int day, int month, bool isLeapMonth)
    {
        var today = calendar.GetVietnamToday();
        try
        {
            var (lunarYear, solarDate, daysUntil) = calendar.NextOccurrence(day, month, isLeapMonth, today);
            return new(day, month, isLeapMonth, lunarYear, solarDate, daysUntil, solarDate.Year,
                calendar.HasPassedInCurrentSolarYear(day, month, isLeapMonth, today));
        }
        catch (ArgumentException ex) { throw InvalidLunarDate(ex); }
    }

    private IQueryable<FamilyAnniversary> Query(long treeId) => db.FamilyAnniversaries.AsNoTracking()
        .Where(a => a.FamilyTreeId == treeId).Include(a => a.RelatedMember);

    private async Task<FamilyMember?> ResolveMember(long treeId, int? memberId, CancellationToken ct)
    {
        if (memberId is null) return null;
        return await db.FamilyMembers.SingleOrDefaultAsync(m => m.Id == memberId && m.FamilyTreeId == treeId, ct)
            ?? throw new KeyNotFoundException("Related family member not found.");
    }

    private static void ValidateRelatedMember(string eventType, DateOnly? originalSolarDate, FamilyMember? member)
    {
        if (eventType != "DEATH_ANNIVERSARY" || member is null) return;
        if (member.LifeStatus != "DECEASED")
            throw Invalid("FAMILY_ANNIVERSARY_MEMBER_NOT_DECEASED", "A death anniversary can only be linked to a deceased family member.");
        if (member.DeathDate is null)
            throw Invalid("FAMILY_ANNIVERSARY_MEMBER_DEATH_DATE_REQUIRED", "The linked deceased family member must have a death date in their profile.");
        if (originalSolarDate != member.DeathDate)
            throw Invalid("FAMILY_ANNIVERSARY_DEATH_DATE_MISMATCH", "The original solar date must match the linked family member's death date.");
    }

    private (string EventType, string Title, int LunarDay, int LunarMonth, string? Place, string? Host, string? Notes) Validate(FamilyAnniversaryWriteDto dto)
    {
        var eventType = dto.EventType?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!EventTypes.Contains(eventType)) throw Invalid("FAMILY_ANNIVERSARY_INVALID_EVENT_TYPE", "Choose a supported event type.");
        var title = dto.Title?.Trim() ?? string.Empty;
        if (title.Length is < 2 or > 200) throw Invalid("FAMILY_ANNIVERSARY_INVALID_TITLE", "Title must contain 2 to 200 characters.");
        if (dto.LunarDay is < 1 or > 30) throw Invalid("FAMILY_ANNIVERSARY_INVALID_LUNAR_DATE", "Lunar day must be between 1 and 30.");
        if (dto.LunarMonth is < 1 or > 12) throw Invalid("FAMILY_ANNIVERSARY_INVALID_LUNAR_DATE", "Lunar month must be between 1 and 12.");
        var place = Normalize(dto.Place); var host = Normalize(dto.Host); var notes = Normalize(dto.Notes);
        if (place?.Length > 500) throw Invalid("FAMILY_ANNIVERSARY_INVALID_PLACE", "Place cannot exceed 500 characters.");
        if (host?.Length > 300) throw Invalid("FAMILY_ANNIVERSARY_INVALID_HOST", "Host cannot exceed 300 characters.");
        if (notes?.Length > 2000) throw Invalid("FAMILY_ANNIVERSARY_INVALID_NOTES", "Notes cannot exceed 2000 characters.");
        return (eventType, title, dto.LunarDay, dto.LunarMonth, place, host, notes);
    }

    private void ValidateOriginalDate(string eventType, FamilyAnniversaryWriteDto dto)
    {
        if (eventType == "DEATH_ANNIVERSARY" && (dto.OriginalSolarDate is null || dto.LunarYear is null))
            throw Invalid("FAMILY_ANNIVERSARY_ORIGINAL_DATE_REQUIRED", "Original solar date and lunar year are required for a death anniversary.");

        if (dto.OriginalSolarDate is null && dto.LunarYear is null) return;
        if (dto.OriginalSolarDate is null || dto.LunarYear is null)
            throw Invalid("FAMILY_ANNIVERSARY_ORIGINAL_DATE_REQUIRED", "Provide both the original solar date and lunar year.");
        if (dto.LunarYear is < 1800 or > 2200)
            throw Invalid("FAMILY_ANNIVERSARY_INVALID_LUNAR_DATE", "Lunar year must be between 1800 and 2200.");

        DateOnly converted;
        try { converted = calendar.ConvertLunarToSolar(dto.LunarDay, dto.LunarMonth, dto.LunarYear.Value, dto.IsLeapMonth); }
        catch (ArgumentException ex) { throw InvalidLunarDate(ex); }
        if (converted != dto.OriginalSolarDate.Value)
            throw Invalid("FAMILY_ANNIVERSARY_DATE_MISMATCH", "The original solar date does not match the supplied lunar date.");
    }

    private void EnsureRecurringDate(int day, int month, bool leap)
    {
        try { calendar.NextOccurrence(day, month, leap); }
        catch (ArgumentException ex) { throw InvalidLunarDate(ex); }
    }

    private FamilyAnniversaryDto Map(FamilyAnniversary item, DateOnly today)
    {
        (int lunarYear, DateOnly next, int daysUntil) occurrence;
        try { occurrence = calendar.NextOccurrence(item.LunarDay, item.LunarMonth, item.IsLeapMonth, today); }
        catch (ArgumentException ex) { throw InvalidLunarDate(ex); }
        return new(item.Id, item.EventType, item.Title, item.RelatedMemberId,
            item.RelatedMember?.FullName, item.RelatedMember?.Generation, item.RelatedMember?.Role,
            item.LunarDay, item.LunarMonth, item.IsLeapMonth, item.OriginalSolarDate, item.LunarYear, occurrence.lunarYear, occurrence.next, occurrence.daysUntil,
            item.Place, item.Host, item.Notes, item.CreatedAt, item.UpdatedAt);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static FamilyAnniversaryValidationException Invalid(string code, string message) => new(code, message);
    private static FamilyAnniversaryValidationException InvalidLunarDate(Exception _) =>
        Invalid("FAMILY_ANNIVERSARY_INVALID_LUNAR_DATE", "The lunar date is not valid in the Vietnamese lunar calendar.");
}

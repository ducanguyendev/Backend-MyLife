namespace MyLife.Features.FamilyTree.DTOs;

public sealed record FamilyAnniversaryDto(
    long Id,
    string EventType,
    string Title,
    int? RelatedMemberId,
    string? RelatedMemberName,
    int? RelatedMemberGeneration,
    string? RelatedMemberRole,
    int LunarDay,
    int LunarMonth,
    bool IsLeapMonth,
    DateOnly NextSolarDate,
    int DaysUntil,
    string? Place,
    string? Host,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record LunarToSolarDto(int LunarDay, int LunarMonth, int LunarYear, bool IsLeapMonth, DateOnly SolarDate);

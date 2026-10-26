namespace MyLife.Features.FamilyTree.DTOs;

public sealed record FamilyTimelineEventDto(long Id, int? MemberId, string? MemberName, string EventType, string Title,
    string? Description, DateOnly? EventDate, int? EventYear, bool IsApproximate, string? Location,
    double? Latitude, double? Longitude, int? RelatedMemberId, string? SourceType, long? SourceId, bool IsDerived);

public sealed class FamilyTimelineWriteDto
{
    public int? MemberId { get; set; }
    public string? EventType { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateOnly? EventDate { get; set; }
    public int? EventYear { get; set; }
    public bool IsApproximate { get; set; }
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RelatedMemberId { get; set; }
}

public sealed record FamilyMemberProfileDto(
    FamilyMemberDto Member,
    IReadOnlyList<FamilyMemberDto> Relatives,
    IReadOnlyList<FamilyTimelineEventDto> Timeline,
    IReadOnlyList<FamilyAnniversaryDto> Anniversaries,
    bool LibraryMemberLinkAvailable);

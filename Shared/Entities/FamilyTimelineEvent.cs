using System.ComponentModel.DataAnnotations;

namespace MyLife.Shared.Entities;

public sealed class FamilyTimelineEvent
{
    public long Id { get; set; }
    public long FamilyTreeId { get; set; }
    public FamilyTree FamilyTree { get; set; } = null!;
    public int? MemberId { get; set; }
    public FamilyMember? Member { get; set; }
    [MaxLength(32)] public string EventType { get; set; } = "CUSTOM";
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; set; }
    public DateOnly? EventDate { get; set; }
    public int? EventYear { get; set; }
    public bool IsApproximate { get; set; }
    [MaxLength(500)] public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RelatedMemberId { get; set; }
    public FamilyMember? RelatedMember { get; set; }
    public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

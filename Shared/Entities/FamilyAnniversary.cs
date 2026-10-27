namespace MyLife.Shared.Entities;

public sealed class FamilyAnniversary
{
    public long Id { get; set; }
    public long FamilyTreeId { get; set; }
    public FamilyTree FamilyTree { get; set; } = null!;
    public int? RelatedMemberId { get; set; }
    public FamilyMember? RelatedMember { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? LunarDay { get; set; }
    public int? LunarMonth { get; set; }
    public bool IsLeapMonth { get; set; }
    public DateOnly? OriginalSolarDate { get; set; }
    public int? LunarYear { get; set; }
    public string? Place { get; set; }
    public string? Host { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

namespace MyLife.Features.FamilyTree.DTOs;

public sealed class FamilyAnniversaryWriteDto
{
    public string? EventType { get; set; }
    public string? Title { get; set; }
    public int? RelatedMemberId { get; set; }
    public int? LunarDay { get; set; }
    public int? LunarMonth { get; set; }
    public bool IsLeapMonth { get; set; }
    public DateOnly? OriginalSolarDate { get; set; }
    public int? LunarYear { get; set; }
    public string? Place { get; set; }
    public string? Host { get; set; }
    public string? Notes { get; set; }
}

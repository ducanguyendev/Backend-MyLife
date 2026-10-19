namespace MyLife.Features.FamilyTree.DTOs;

public sealed class FamilyMapDto
{
    public int TotalMembers { get; init; }
    public int LocatedMembers { get; init; }
    public int UnlocatedMembers { get; init; }
    public int PendingGeocodingMembers { get; init; }
    public int NoAddressMembers { get; init; }
    public int FailedGeocodingMembers { get; init; }
    public IReadOnlyList<FamilyMapLocationDto> Locations { get; init; } = [];
}

public sealed class FamilyMapLocationDto
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public int MemberCount { get; init; }
    public IReadOnlyList<FamilyMapMemberDto> Members { get; init; } = [];
}

public sealed class FamilyMapMemberDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public int Generation { get; init; }
}

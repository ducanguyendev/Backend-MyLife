namespace MyLife.Features.FamilyTree.DTOs;

public sealed record FamilyRelationshipPathNodeDto(int MemberId, string FullName, string? Edge = null);

public sealed record FamilyRelationshipResultDto(
    int FromMemberId,
    int ToMemberId,
    string RelationshipCode,
    string RelationshipLabel,
    string ReverseRelationshipCode,
    string ReverseRelationshipLabel,
    int Distance,
    IReadOnlyList<FamilyRelationshipPathNodeDto> Path);

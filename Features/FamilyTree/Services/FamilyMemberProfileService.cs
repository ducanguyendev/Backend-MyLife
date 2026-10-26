using MyLife.Features.FamilyTree.DTOs;

namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyMemberProfileService(IFamilyTreeService trees, IFamilyTimelineService timeline, IFamilyAnniversaryService anniversaries) : IFamilyMemberProfileService
{
    public async Task<FamilyMemberProfileDto?> GetAsync(int userId, int memberId, CancellationToken ct)
    {
        var members = (await trees.GetAllMembersAsync(userId, ct)).ToArray();
        var member = members.SingleOrDefault(m => m.Id == memberId);
        if (member is null) return null;
        var relatedIds = new HashSet<int>();
        if (member.FatherId is int fatherId) relatedIds.Add(fatherId);
        if (member.MotherId is int motherId) relatedIds.Add(motherId);
        if (member.SpouseId is int spouseId) relatedIds.Add(spouseId);
        foreach (var relation in member.HorizontalRelations) relatedIds.Add(relation.MemberId);
        foreach (var item in members)
        {
            var sharesKnownParent = member.FatherId.HasValue && member.FatherId == item.FatherId || member.MotherId.HasValue && member.MotherId == item.MotherId;
            if (item.FatherId == memberId || item.MotherId == memberId || sharesKnownParent ||
                member.HorizontalRelations.Any(r => r.MemberId == item.Id) || item.HorizontalRelations.Any(r => r.MemberId == memberId)) relatedIds.Add(item.Id);
        }
        foreach (var id in member.ChildIds) relatedIds.Add(id);
        relatedIds.Remove(memberId);
        var memberTimeline = await timeline.ListAsync(userId, memberId, ct);
        var memberAnniversaries = (await anniversaries.ListAsync(userId, ct)).Where(a => a.RelatedMemberId == memberId).ToArray();
        return new(member, members.Where(m => relatedIds.Contains(m.Id)).ToArray(), memberTimeline, memberAnniversaries, false);
    }
}

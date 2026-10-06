using Microsoft.EntityFrameworkCore;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;

namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyTreeService(AppDbContext context) : IFamilyTreeService
{
    public async Task<IEnumerable<FamilyMemberDto>> GetAllMembersAsync() => (await MembersQuery().OrderBy(x => x.Generation).ThenBy(x => x.DateOfBirth).ToListAsync()).Select(Map);
    public async Task<FamilyMemberDto?> GetMemberByIdAsync(int id) { var member = await MembersQuery().SingleOrDefaultAsync(x => x.Id == id); return member is null ? null : Map(member); }
    public async Task<IEnumerable<GenerationDto>> GetAllGenerationsAsync() => (await context.Generations.OrderBy(x => x.Id).ToListAsync()).Select(x => new GenerationDto { Id = x.Id, Name = x.Name, Title = x.Title, Description = x.Description });

    public async Task<FamilyMemberDto> CreateMemberAsync(CreateFamilyMemberDto dto)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await ValidateReferencesAsync(null, dto.FatherId, dto.MotherId, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations, dto.Gender);
        var member = new FamilyMember { FullName = dto.FullName.Trim(), Generation = dto.Generation, Gender = dto.Gender, DateOfBirth = dto.DateOfBirth, Role = dto.Role,
            Address = dto.Address, PhoneNumber = dto.PhoneNumber, FacebookUrl = dto.FacebookUrl, InstagramUrl = dto.InstagramUrl, AvatarUrl = dto.AvatarUrl, Biography = dto.Biography,
            FatherId = dto.FatherId, MotherId = dto.MotherId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.FamilyMembers.Add(member); await context.SaveChangesAsync();
        await SynchronizeRelationsAsync(member, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations);
        await context.SaveChangesAsync(); await transaction.CommitAsync();
        return (await GetMemberByIdAsync(member.Id))!;
    }

    public async Task<FamilyMemberDto> UpdateMemberAsync(int id, UpdateFamilyMemberDto dto)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var member = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == id) ?? throw new KeyNotFoundException();
        await ValidateReferencesAsync(id, dto.FatherId, dto.MotherId, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations, dto.Gender);
        member.FullName = dto.FullName.Trim(); member.Generation = dto.Generation; member.Gender = dto.Gender; member.DateOfBirth = dto.DateOfBirth; member.Role = dto.Role;
        member.Address = dto.Address; member.PhoneNumber = dto.PhoneNumber; member.FacebookUrl = dto.FacebookUrl; member.InstagramUrl = dto.InstagramUrl; member.AvatarUrl = dto.AvatarUrl; member.Biography = dto.Biography;
        member.FatherId = dto.FatherId; member.MotherId = dto.MotherId; member.UpdatedAt = DateTime.UtcNow;
        await SynchronizeRelationsAsync(member, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations);
        await context.SaveChangesAsync(); await transaction.CommitAsync();
        return (await GetMemberByIdAsync(id))!;
    }

    public async Task<bool> DeleteMemberAsync(int id)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var member = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == id); if (member is null) return false;
        if (member.SpouseId is int spouseId) { var spouse = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == spouseId); if (spouse?.SpouseId == id) spouse.SpouseId = null; }
        var children = await context.FamilyMembers.Where(x => x.FatherId == id || x.MotherId == id || x.SpouseId == id).ToListAsync();
        foreach (var child in children) { if (child.FatherId == id) child.FatherId = null; if (child.MotherId == id) child.MotherId = null; if (child.SpouseId == id) child.SpouseId = null; child.UpdatedAt = DateTime.UtcNow; }
        context.FamilyMembers.Remove(member); await context.SaveChangesAsync(); await transaction.CommitAsync(); return true;
    }

    private async Task SynchronizeRelationsAsync(FamilyMember member, int? spouseId, List<int>? childIds, List<HorizontalRelationDto>? horizontal)
    {
        await SynchronizeSpouseAsync(member, spouseId);
        if (childIds is not null) await SynchronizeChildrenAsync(member, childIds);
        if (horizontal is not null) await SynchronizeHorizontalAsync(member, horizontal);
    }

    private async Task SynchronizeSpouseAsync(FamilyMember member, int? spouseId)
    {
        if (member.SpouseId is int existingId && existingId != spouseId)
        {
            var old = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == existingId);
            if (old?.SpouseId == member.Id) { old.SpouseId = null; old.UpdatedAt = DateTime.UtcNow; }
        }
        member.SpouseId = spouseId;
        if (spouseId is null) return;
        var spouse = await context.FamilyMembers.SingleAsync(x => x.Id == spouseId.Value);
        if (spouse.SpouseId is int otherId && otherId != member.Id)
        {
            throw new FamilyTreeValidationException("FAMILY_SPOUSE_IN_USE", "The selected spouse is already linked to another member.");
        }
        spouse.SpouseId = member.Id; spouse.UpdatedAt = DateTime.UtcNow;
    }

    private async Task SynchronizeChildrenAsync(FamilyMember member, List<int> childIds)
    {
        var targets = childIds.Distinct().ToHashSet();
        var current = await context.FamilyMembers.Where(x => x.FatherId == member.Id || x.MotherId == member.Id).ToListAsync();
        foreach (var child in current.Where(x => !targets.Contains(x.Id))) { if (child.FatherId == member.Id) child.FatherId = null; if (child.MotherId == member.Id) child.MotherId = null; child.UpdatedAt = DateTime.UtcNow; }
        var female = string.Equals(member.Gender, "female", StringComparison.OrdinalIgnoreCase) || string.Equals(member.Gender, "nữ", StringComparison.OrdinalIgnoreCase);
        foreach (var child in await context.FamilyMembers.Where(x => targets.Contains(x.Id)).ToListAsync())
        {
            if (female) { if (child.FatherId == member.Id) child.FatherId = null; child.MotherId = member.Id; }
            else { if (child.MotherId == member.Id) child.MotherId = null; child.FatherId = member.Id; }
            child.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task SynchronizeHorizontalAsync(FamilyMember member, List<HorizontalRelationDto> relations)
    {
        var existing = await context.FamilyRelationships.Where(x => x.Member1Id == member.Id || x.Member2Id == member.Id).ToListAsync();
        var desired = relations.GroupBy(x => new { x.MemberId, Type = x.RelationType.Trim().ToUpperInvariant() }).Select(x => x.First()).ToList();

        foreach (var previous in existing)
        {
            var otherId = previous.Member1Id == member.Id ? previous.Member2Id : previous.Member1Id;
            if (!desired.Any(x => x.MemberId == otherId && string.Equals(x.RelationType.Trim(), previous.RelationType.Trim(), StringComparison.OrdinalIgnoreCase)))
                context.FamilyRelationships.Remove(previous);
        }
        foreach (var relation in desired)
        {
            var first = Math.Min(member.Id, relation.MemberId);
            var second = Math.Max(member.Id, relation.MemberId);
            if (existing.Any(x => ((x.Member1Id == first && x.Member2Id == second) || (x.Member1Id == second && x.Member2Id == first)) &&
                string.Equals(x.RelationType.Trim(), relation.RelationType.Trim(), StringComparison.OrdinalIgnoreCase))) continue;
            context.FamilyRelationships.Add(new FamilyRelationship { Member1Id = first, Member2Id = second, RelationType = relation.RelationType.Trim() });
        }
    }

    private async Task ValidateReferencesAsync(int? memberId, int? fatherId, int? motherId, int? spouseId, List<int>? childIds, List<HorizontalRelationDto>? horizontal, string? gender)
    {
        var ids = new[] { fatherId, motherId, spouseId }.Where(x => x.HasValue).Select(x => x!.Value)
            .Concat(childIds ?? []).Concat(horizontal?.Select(x => x.MemberId) ?? []).ToHashSet();
        if (memberId is int self && ids.Contains(self)) throw new FamilyTreeValidationException("FAMILY_SELF_RELATION", "A family member cannot be related to themselves.");
        if (fatherId.HasValue && fatherId == motherId) throw new FamilyTreeValidationException("FAMILY_PARENTS_MUST_DIFFER", "Father and mother must be different members.");
        var nodes = await context.FamilyMembers.AsNoTracking().Select(x => new { x.Id, x.FatherId, x.MotherId, x.SpouseId }).ToListAsync();
        if (!ids.All(id => nodes.Any(x => x.Id == id))) throw new FamilyTreeValidationException("FAMILY_RELATED_MEMBER_NOT_FOUND", "One or more related family members do not exist.");
        if (spouseId is int spouse && nodes.Single(x => x.Id == spouse).SpouseId is int other && other != memberId)
            throw new FamilyTreeValidationException("FAMILY_SPOUSE_IN_USE", "The selected spouse is already linked to another member.");
        if (horizontal?.Any(x => string.IsNullOrWhiteSpace(x.RelationType) || x.RelationType.Trim().Length > 50) == true)
            throw new FamilyTreeValidationException("FAMILY_RELATIONSHIP_INVALID", "A horizontal relationship type of at most 50 characters is required.");

        // Validate the entire proposed graph, including simultaneous parent and child edits.
        // Checking each ID against the old graph would miss A.parent=B plus A.children=[B].
        var graph = nodes.ToDictionary(x => x.Id, x => (Father: x.FatherId, Mother: x.MotherId));
        var editedId = memberId ?? 0;
        graph[editedId] = (fatherId, motherId);
        var affected = new HashSet<int> { editedId };
        if (childIds is not null)
        {
            var targets = childIds.ToHashSet();
            var female = string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase) || string.Equals(gender, "nữ", StringComparison.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                var parents = graph[node.Id];
                if (!targets.Contains(node.Id))
                {
                    if (parents.Father == editedId) parents.Father = null;
                    if (parents.Mother == editedId) parents.Mother = null;
                }
                else
                {
                    if (female) { if (parents.Father == editedId) parents.Father = null; parents.Mother = editedId; }
                    else { if (parents.Mother == editedId) parents.Mother = null; parents.Father = editedId; }
                    affected.Add(node.Id);
                }
                graph[node.Id] = parents;
            }
        }
        var complete = new HashSet<int>();
        var visiting = new HashSet<int>();
        bool HasCycle(int id)
        {
            if (complete.Contains(id) || !graph.ContainsKey(id)) return false;
            if (!visiting.Add(id)) return true;
            var parents = graph[id];
            if ((parents.Father is int father && HasCycle(father)) || (parents.Mother is int mother && HasCycle(mother))) return true;
            visiting.Remove(id);
            complete.Add(id);
            return false;
        }
        if (affected.Any(HasCycle)) throw new FamilyTreeValidationException("FAMILY_RELATIONSHIP_CYCLE", "A parent-child relationship would create a cycle.");
    }

    private IQueryable<FamilyMember> MembersQuery() => context.FamilyMembers.Include(x => x.Father).Include(x => x.Mother).Include(x => x.Spouse)
        .Include(x => x.ChildrenAsFather).Include(x => x.ChildrenAsMother).Include(x => x.RelationsAsMember1).Include(x => x.RelationsAsMember2);
    private static FamilyMemberDto Map(FamilyMember member)
    {
        var children = member.ChildrenAsFather.Concat(member.ChildrenAsMother).DistinctBy(x => x.Id).ToList();
        return new FamilyMemberDto { Id = member.Id, FullName = member.FullName, Generation = member.Generation, Gender = member.Gender, DateOfBirth = member.DateOfBirth, Role = member.Role, Address = member.Address,
            PhoneNumber = member.PhoneNumber, FacebookUrl = member.FacebookUrl, InstagramUrl = member.InstagramUrl, AvatarUrl = member.AvatarUrl, Biography = member.Biography,
            FatherId = member.FatherId, FatherName = member.Father?.FullName, MotherId = member.MotherId, MotherName = member.Mother?.FullName, SpouseId = member.SpouseId, SpouseName = member.Spouse?.FullName,
            ChildIds = children.Select(x => x.Id).ToList(), ChildNames = children.Select(x => x.FullName).ToList(), HorizontalRelations = member.RelationsAsMember1.Select(x => new HorizontalRelationDto { MemberId = x.Member2Id, RelationType = x.RelationType }).Concat(member.RelationsAsMember2.Select(x => new HorizontalRelationDto { MemberId = x.Member1Id, RelationType = x.RelationType })).ToList(), CreatedAt = member.CreatedAt, UpdatedAt = member.UpdatedAt };
    }
}

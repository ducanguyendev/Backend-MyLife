using Microsoft.EntityFrameworkCore;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;
using UserFamilyTree = MyLife.Shared.Entities.FamilyTree;

namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyTreeService(AppDbContext context, IGoogleDriveMemberAvatarService avatarStorage, IMemberAvatarImageCache avatarImageCache, ILogger<FamilyTreeService> logger) : IFamilyTreeService
{
    public async Task<UserFamilyTree> EnsureUserFamilyTreeAsync(int userId, CancellationToken ct)
    {
        var tree = await context.FamilyTrees.AsNoTracking().SingleOrDefaultAsync(t => t.OwnerUserId == userId, ct);
        if (tree is not null) return tree;
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        // Serialize only concurrent first requests; the unique owner index is the final guard.
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE", ct);
        tree = await context.FamilyTrees.SingleOrDefaultAsync(t => t.OwnerUserId == userId, ct);
        if (tree is null)
        {
            var now = DateTime.UtcNow;
            tree = new UserFamilyTree { OwnerUserId = userId, CreatedAt = now, UpdatedAt = now };
            context.FamilyTrees.Add(tree);
            await context.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return tree;
    }

    public async Task<IEnumerable<FamilyMemberDto>> GetAllMembersAsync(int userId, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        return (await MembersQuery(tree.Id).OrderBy(x => x.Generation).ThenBy(x => x.DateOfBirth).ToListAsync(ct)).Select(Map);
    }
    public async Task<FamilyMemberDto?> GetMemberByIdAsync(int userId, int memberId, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        var member = await MembersQuery(tree.Id).SingleOrDefaultAsync(x => x.Id == memberId, ct);
        return member is null ? null : Map(member);
    }
    // Generations are shared system lookup metadata, not tree-owned members.
    public async Task<IEnumerable<GenerationDto>> GetAllGenerationsAsync(CancellationToken ct) =>
        (await context.Generations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)).Select(x => new GenerationDto { Id = x.Id, Name = x.Name, Title = x.Title, Description = x.Description });

    public async Task<FamilyMemberDto> CreateMemberAsync(int userId, CreateFamilyMemberDto dto, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await ValidateReferencesAsync(tree.Id, null, dto.FatherId, dto.MotherId, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations, dto.Gender, ct);
        var member = new FamilyMember { FamilyTreeId = tree.Id, FullName = dto.FullName.Trim(), Generation = dto.Generation, Gender = dto.Gender, DateOfBirth = dto.DateOfBirth, Role = dto.Role,
            Address = dto.Address, PhoneNumber = dto.PhoneNumber, FacebookUrl = dto.FacebookUrl, InstagramUrl = dto.InstagramUrl, Biography = dto.Biography,
            FatherId = dto.FatherId, MotherId = dto.MotherId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.FamilyMembers.Add(member); await context.SaveChangesAsync(ct);
        await SynchronizeRelationsAsync(member, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations, ct);
        await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return (await GetMemberByIdAsync(userId, member.Id, ct))!;
    }

    public async Task<FamilyMemberDto> UpdateMemberAsync(int userId, int id, UpdateFamilyMemberDto dto, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var member = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == id && x.FamilyTreeId == tree.Id, ct) ?? throw new KeyNotFoundException();
        await ValidateReferencesAsync(tree.Id, id, dto.FatherId, dto.MotherId, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations, dto.Gender, ct);
        member.FullName = dto.FullName.Trim(); member.Generation = dto.Generation; member.Gender = dto.Gender; member.DateOfBirth = dto.DateOfBirth; member.Role = dto.Role;
        var nextAddress = dto.Address?.Trim();
        if (!string.Equals(member.Address?.Trim(), nextAddress, StringComparison.Ordinal))
        {
            member.Latitude = null; member.Longitude = null; member.GeocodedAddress = null;
            member.LocationDisplayName = null; member.GeocodedAt = null;
        }
        member.Address = dto.Address; member.PhoneNumber = dto.PhoneNumber; member.FacebookUrl = dto.FacebookUrl; member.InstagramUrl = dto.InstagramUrl; member.Biography = dto.Biography;
        member.FatherId = dto.FatherId; member.MotherId = dto.MotherId; member.UpdatedAt = DateTime.UtcNow;
        await SynchronizeRelationsAsync(member, dto.SpouseId, dto.ChildIds, dto.HorizontalRelations, ct);
        await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return (await GetMemberByIdAsync(userId, id, ct))!;
    }

    public async Task<bool> DeleteMemberAsync(int userId, int id, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var member = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == id && x.FamilyTreeId == tree.Id, ct); if (member is null) return false;
        var avatarFileId = member.AvatarDriveFileId;
        var avatarVersion = member.UpdatedAt;
        if (member.SpouseId is int spouseId) { var spouse = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == spouseId && x.FamilyTreeId == tree.Id, ct); if (spouse?.SpouseId == id) spouse.SpouseId = null; }
        var children = await context.FamilyMembers.Where(x => x.FamilyTreeId == tree.Id && (x.FatherId == id || x.MotherId == id || x.SpouseId == id)).ToListAsync(ct);
        foreach (var child in children) { if (child.FatherId == id) child.FatherId = null; if (child.MotherId == id) child.MotherId = null; if (child.SpouseId == id) child.SpouseId = null; child.UpdatedAt = DateTime.UtcNow; }
        context.FamilyMembers.Remove(member); await context.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        avatarImageCache.Invalidate(id, avatarFileId, avatarVersion);
        if (!string.IsNullOrWhiteSpace(avatarFileId))
        {
            try
            {
                if (!await avatarStorage.DeleteAvatarAsync(id, avatarFileId, CancellationToken.None))
                    logger.LogWarning("Family member deleted but avatar cleanup failed MemberId={MemberId}", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Family member deleted but avatar cleanup failed MemberId={MemberId} ErrorType={ErrorType}", id, ex.GetType().Name);
            }
        }
        return true;
    }

    public async Task<FamilyMemberDto> UploadMemberAvatarAsync(int userId, int memberId, byte[] content, string contentType, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        var member = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == memberId && x.FamilyTreeId == tree.Id, ct) ?? throw new KeyNotFoundException();
        var mime = contentType.Trim().ToLowerInvariant() is "image/jpg" ? "image/jpeg" : contentType.Trim().ToLowerInvariant();
        if (content.Length is <= 0 or > 5 * 1024 * 1024 || !IsAllowedImage(mime))
            throw new FamilyTreeValidationException("FAMILY_AVATAR_INVALID", "The avatar file must be a JPG, PNG, WebP or GIF up to 5 MiB.");

        var previousId = member.AvatarDriveFileId;
        var previousUpdatedAt = member.UpdatedAt;
        var stored = await avatarStorage.UploadAvatarAsync(member.Id, content, mime, previousId, ct);
        if (!stored.Success || string.IsNullOrWhiteSpace(stored.FileId) || !Uri.TryCreate(stored.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            if (previousId is null && !string.IsNullOrWhiteSpace(stored.FileId))
            {
                try { await avatarStorage.DeleteAvatarAsync(member.Id, stored.FileId, CancellationToken.None); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning("New member avatar compensation failed MemberId={MemberId} ErrorType={ErrorType}", memberId, ex.GetType().Name); }
            }
            throw new FamilyTreeStorageException();
        }

        var previousUrl = member.AvatarUrl;
        member.AvatarDriveFileId = stored.FileId;
        member.AvatarUrl = stored.Url;
        member.UpdatedAt = NextAvatarVersion(previousUpdatedAt);
        try { await context.SaveChangesAsync(ct); }
        catch
        {
            member.AvatarDriveFileId = previousId;
            member.AvatarUrl = previousUrl;
            member.UpdatedAt = previousUpdatedAt;
            if (previousId is null && string.IsNullOrWhiteSpace(stored.FileId) is false)
            {
                try { await avatarStorage.DeleteAvatarAsync(member.Id, stored.FileId, CancellationToken.None); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning("New member avatar compensation failed MemberId={MemberId} ErrorType={ErrorType}", memberId, ex.GetType().Name); }
            }
            throw;
        }
        avatarImageCache.Invalidate(memberId, previousId, previousUpdatedAt);
        return (await GetMemberByIdAsync(userId, memberId, ct))!;
    }

    public async Task<FamilyMemberDto> DeleteMemberAvatarAsync(int userId, int memberId, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        var member = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == memberId && x.FamilyTreeId == tree.Id, ct) ?? throw new KeyNotFoundException();
        if ((member.AvatarDriveFileId is not null || member.AvatarUrl is not null) && !await avatarStorage.DeleteAvatarAsync(member.Id, member.AvatarDriveFileId, ct))
            throw new FamilyTreeStorageException();
        var previousId = member.AvatarDriveFileId; var previousUrl = member.AvatarUrl; var previousUpdatedAt = member.UpdatedAt;
        member.AvatarDriveFileId = null; member.AvatarUrl = null; member.UpdatedAt = NextAvatarVersion(previousUpdatedAt);
        try { await context.SaveChangesAsync(ct); }
        catch
        {
            member.AvatarDriveFileId = previousId; member.AvatarUrl = previousUrl; member.UpdatedAt = previousUpdatedAt;
            throw;
        }
        avatarImageCache.Invalidate(memberId, previousId, previousUpdatedAt);
        return (await GetMemberByIdAsync(userId, memberId, ct))!;
    }

    public async Task<MemberAvatarImageResult?> GetMemberAvatarImageAsync(int userId, int memberId, CancellationToken ct)
    {
        var tree = await EnsureUserFamilyTreeAsync(userId, ct);
        var member = await context.FamilyMembers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == memberId && x.FamilyTreeId == tree.Id, ct);
        if (member is null || string.IsNullOrWhiteSpace(member.AvatarDriveFileId)) return null;
        var image = await avatarStorage.GetAvatarAsync(member.Id, member.AvatarDriveFileId, member.UpdatedAt, ct);
        return image ?? throw new FamilyTreeStorageException();
    }

    private static bool IsAllowedImage(string mime) => mime is "image/jpeg" or "image/png" or "image/webp" or "image/gif";

    internal static DateTime NextAvatarVersion(DateTime previousVersion)
    {
        var now = DateTime.UtcNow;
        // PostgreSQL timestamps have microsecond precision. Add a millisecond when
        // the clock has not moved past the previous revision to guarantee a new key.
        var next = previousVersion.AddMilliseconds(1);
        return now > next ? now : next;
    }

    private async Task SynchronizeRelationsAsync(FamilyMember member, int? spouseId, List<int>? childIds, List<HorizontalRelationDto>? horizontal, CancellationToken ct)
    {
        await SynchronizeSpouseAsync(member, spouseId, ct);
        if (childIds is not null) await SynchronizeChildrenAsync(member, childIds, ct);
        if (horizontal is not null) await SynchronizeHorizontalAsync(member, horizontal, ct);
    }

    private async Task SynchronizeSpouseAsync(FamilyMember member, int? spouseId, CancellationToken ct)
    {
        if (member.SpouseId is int existingId && existingId != spouseId)
        {
            var old = await context.FamilyMembers.SingleOrDefaultAsync(x => x.Id == existingId && x.FamilyTreeId == member.FamilyTreeId, ct);
            if (old?.SpouseId == member.Id) { old.SpouseId = null; old.UpdatedAt = DateTime.UtcNow; }
        }
        member.SpouseId = spouseId;
        if (spouseId is null) return;
        var spouse = await context.FamilyMembers.SingleAsync(x => x.Id == spouseId.Value && x.FamilyTreeId == member.FamilyTreeId, ct);
        if (spouse.SpouseId is int otherId && otherId != member.Id)
        {
            throw new FamilyTreeValidationException("FAMILY_SPOUSE_IN_USE", "The selected spouse is already linked to another member.");
        }
        spouse.SpouseId = member.Id; spouse.UpdatedAt = DateTime.UtcNow;
    }

    private async Task SynchronizeChildrenAsync(FamilyMember member, List<int> childIds, CancellationToken ct)
    {
        var targets = childIds.Distinct().ToHashSet();
        var current = await context.FamilyMembers.Where(x => x.FamilyTreeId == member.FamilyTreeId && (x.FatherId == member.Id || x.MotherId == member.Id)).ToListAsync(ct);
        foreach (var child in current.Where(x => !targets.Contains(x.Id))) { if (child.FatherId == member.Id) child.FatherId = null; if (child.MotherId == member.Id) child.MotherId = null; child.UpdatedAt = DateTime.UtcNow; }
        var female = string.Equals(member.Gender, "female", StringComparison.OrdinalIgnoreCase) || string.Equals(member.Gender, "nữ", StringComparison.OrdinalIgnoreCase);
        foreach (var child in await context.FamilyMembers.Where(x => x.FamilyTreeId == member.FamilyTreeId && targets.Contains(x.Id)).ToListAsync(ct))
        {
            if (female) { if (child.FatherId == member.Id) child.FatherId = null; child.MotherId = member.Id; }
            else { if (child.MotherId == member.Id) child.MotherId = null; child.FatherId = member.Id; }
            child.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task SynchronizeHorizontalAsync(FamilyMember member, List<HorizontalRelationDto> relations, CancellationToken ct)
    {
        var existing = await context.FamilyRelationships.Where(x => x.Member1!.FamilyTreeId == member.FamilyTreeId && x.Member2!.FamilyTreeId == member.FamilyTreeId && (x.Member1Id == member.Id || x.Member2Id == member.Id)).ToListAsync(ct);
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

    private async Task ValidateReferencesAsync(long treeId, int? memberId, int? fatherId, int? motherId, int? spouseId, List<int>? childIds, List<HorizontalRelationDto>? horizontal, string? gender, CancellationToken ct)
    {
        var ids = new[] { fatherId, motherId, spouseId }.Where(x => x.HasValue).Select(x => x!.Value)
            .Concat(childIds ?? []).Concat(horizontal?.Select(x => x.MemberId) ?? []).ToHashSet();
        if (memberId is int self && ids.Contains(self)) throw new FamilyTreeValidationException("FAMILY_SELF_RELATION", "A family member cannot be related to themselves.");
        if (fatherId.HasValue && fatherId == motherId) throw new FamilyTreeValidationException("FAMILY_PARENTS_MUST_DIFFER", "Father and mother must be different members.");
        var nodes = await context.FamilyMembers.AsNoTracking().Where(x => x.FamilyTreeId == treeId).Select(x => new { x.Id, x.FatherId, x.MotherId, x.SpouseId }).ToListAsync(ct);
        var nodeById = nodes.ToDictionary(x => x.Id);
        if (!ids.All(nodeById.ContainsKey)) throw new FamilyTreeValidationException("FAMILY_RELATED_MEMBER_NOT_FOUND", "One or more related family members do not exist.");
        if (spouseId is int spouse && nodeById[spouse].SpouseId is int other && other != memberId)
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

    private IQueryable<FamilyMember> MembersQuery(long treeId) => context.FamilyMembers.AsNoTracking().Where(x => x.FamilyTreeId == treeId).AsSplitQuery().Include(x => x.Father).Include(x => x.Mother).Include(x => x.Spouse)
        .Include(x => x.ChildrenAsFather).Include(x => x.ChildrenAsMother).Include(x => x.RelationsAsMember1).Include(x => x.RelationsAsMember2);
    private static FamilyMemberDto Map(FamilyMember member)
    {
        var children = member.ChildrenAsFather.Concat(member.ChildrenAsMother).DistinctBy(x => x.Id).ToList();
        return new FamilyMemberDto { Id = member.Id, FullName = member.FullName, Generation = member.Generation, Gender = member.Gender, DateOfBirth = member.DateOfBirth, Role = member.Role, Address = member.Address,
            PhoneNumber = member.PhoneNumber, FacebookUrl = member.FacebookUrl, InstagramUrl = member.InstagramUrl,
            AvatarUrl = string.IsNullOrWhiteSpace(member.AvatarDriveFileId) ? member.AvatarUrl : null,
            HasManagedAvatar = !string.IsNullOrWhiteSpace(member.AvatarDriveFileId), Biography = member.Biography,
            FatherId = member.FatherId, FatherName = member.Father?.FullName, MotherId = member.MotherId, MotherName = member.Mother?.FullName, SpouseId = member.SpouseId, SpouseName = member.Spouse?.FullName,
            ChildIds = children.Select(x => x.Id).ToList(), ChildNames = children.Select(x => x.FullName).ToList(), HorizontalRelations = member.RelationsAsMember1.Select(x => new HorizontalRelationDto { MemberId = x.Member2Id, RelationType = x.RelationType }).Concat(member.RelationsAsMember2.Select(x => new HorizontalRelationDto { MemberId = x.Member1Id, RelationType = x.RelationType })).ToList(), CreatedAt = member.CreatedAt, UpdatedAt = member.UpdatedAt };
    }
}

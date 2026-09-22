using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MyLife.Features.FamilyTree.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;

namespace MyLife.Features.FamilyTree.Services
{
    public class FamilyTreeService : IFamilyTreeService
    {
        private readonly AppDbContext _context;

        public FamilyTreeService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<FamilyMemberDto>> GetAllMembersAsync()
        {
            var members = await _context.FamilyMembers
                .Include(m => m.Father)
                .Include(m => m.Mother)
                .Include(m => m.Spouse)
                .Include(m => m.ChildrenAsFather)
                .Include(m => m.ChildrenAsMother)
                .Include(m => m.RelationsAsMember1)
                .Include(m => m.RelationsAsMember2)
                .OrderBy(m => m.Generation)
                .ThenBy(m => m.DateOfBirth)
                .ToListAsync();

            return members.Select(MapToDto);
        }

        public async Task<FamilyMemberDto?> GetMemberByIdAsync(int id)
        {
            var member = await _context.FamilyMembers
                .Include(m => m.Father)
                .Include(m => m.Mother)
                .Include(m => m.Spouse)
                .Include(m => m.ChildrenAsFather)
                .Include(m => m.ChildrenAsMother)
                .Include(m => m.RelationsAsMember1)
                .Include(m => m.RelationsAsMember2)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (member == null) return null;

            return MapToDto(member);
        }

        public async Task<FamilyMemberDto> CreateMemberAsync(CreateFamilyMemberDto dto)
        {
            var member = new FamilyMember
            {
                FullName = dto.FullName,
                Generation = dto.Generation,
                Gender = dto.Gender,
                DateOfBirth = dto.DateOfBirth,
                Role = dto.Role,
                Address = dto.Address,
                PhoneNumber = dto.PhoneNumber,
                FacebookUrl = dto.FacebookUrl,
                InstagramUrl = dto.InstagramUrl,
                AvatarUrl = dto.AvatarUrl,
                Biography = dto.Biography,
                FatherId = dto.FatherId,
                MotherId = dto.MotherId,
                SpouseId = dto.SpouseId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.FamilyMembers.Add(member);
            await _context.SaveChangesAsync();

            if (dto.ChildIds != null && dto.ChildIds.Any())
            {
                await SyncChildrenAsync(member, dto.ChildIds);
                await _context.SaveChangesAsync();
            }

            if (dto.HorizontalRelations != null)
            {
                await SyncHorizontalRelationsAsync(member, dto.HorizontalRelations);
                await _context.SaveChangesAsync();
            }

            // Lấy lại thành viên vừa tạo (để join name nếu cần)
            return await GetMemberByIdAsync(member.Id) ?? MapToDto(member);
        }

        public async Task<FamilyMemberDto> UpdateMemberAsync(int id, UpdateFamilyMemberDto dto)
        {
            var member = await _context.FamilyMembers.FindAsync(id);
            if (member == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thành viên");
            }

            member.FullName = dto.FullName;
            member.Generation = dto.Generation;
            member.Gender = dto.Gender;
            member.DateOfBirth = dto.DateOfBirth;
            member.Role = dto.Role;
            member.Address = dto.Address;
            member.PhoneNumber = dto.PhoneNumber;
            member.FacebookUrl = dto.FacebookUrl;
            member.InstagramUrl = dto.InstagramUrl;
            member.AvatarUrl = dto.AvatarUrl;
            member.Biography = dto.Biography;
            member.FatherId = dto.FatherId;
            member.MotherId = dto.MotherId;
            member.SpouseId = dto.SpouseId;
            member.UpdatedAt = DateTime.UtcNow;

            _context.FamilyMembers.Update(member);

            if (dto.ChildIds != null)
            {
                await SyncChildrenAsync(member, dto.ChildIds);
            }

            if (dto.HorizontalRelations != null)
            {
                await SyncHorizontalRelationsAsync(member, dto.HorizontalRelations);
            }

            await _context.SaveChangesAsync();

            return await GetMemberByIdAsync(member.Id) ?? MapToDto(member);
        }

        public async Task<bool> DeleteMemberAsync(int id)
        {
            var member = await _context.FamilyMembers.FindAsync(id);
            if (member == null) return false;

            _context.FamilyMembers.Remove(member);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<GenerationDto>> GetAllGenerationsAsync()
        {
            var generations = await _context.Generations
                .OrderBy(g => g.Id)
                .ToListAsync();

            return generations.Select(g => new GenerationDto
            {
                Id = g.Id,
                Name = g.Name,
                Title = g.Title,
                Description = g.Description
            });
        }

        private async Task SyncChildrenAsync(FamilyMember member, List<int> childIds)
        {
            var isFemale = string.Equals(member.Gender, "Nữ", StringComparison.OrdinalIgnoreCase) || 
                           string.Equals(member.Gender, "female", StringComparison.OrdinalIgnoreCase);

            var currentChildren = await _context.FamilyMembers
                .Where(m => m.FatherId == member.Id || m.MotherId == member.Id)
                .ToListAsync();

            var targetIds = childIds.Where(cid => cid != member.Id).ToHashSet();

            foreach (var child in currentChildren)
            {
                if (!targetIds.Contains(child.Id))
                {
                    if (child.FatherId == member.Id) child.FatherId = null;
                    if (child.MotherId == member.Id) child.MotherId = null;
                    child.UpdatedAt = DateTime.UtcNow;
                }
            }

            var childrenToSet = await _context.FamilyMembers
                .Where(m => targetIds.Contains(m.Id))
                .ToListAsync();

            foreach (var child in childrenToSet)
            {
                if (isFemale)
                {
                    if (child.FatherId == member.Id) child.FatherId = null;
                    child.MotherId = member.Id;
                }
                else
                {
                    if (child.MotherId == member.Id) child.MotherId = null;
                    child.FatherId = member.Id;
                }
                child.UpdatedAt = DateTime.UtcNow;
            }
        }

        private async Task SyncHorizontalRelationsAsync(FamilyMember member, List<HorizontalRelationDto> relationsDto)
        {
            var existingRelations = await _context.FamilyRelationships
                .Where(r => r.Member1Id == member.Id || r.Member2Id == member.Id)
                .ToListAsync();
            
            _context.FamilyRelationships.RemoveRange(existingRelations);
            
            foreach (var rel in relationsDto)
            {
                if (rel.MemberId == member.Id) continue; // Avoid self relation
                
                // Always store smaller ID first for consistency, though any order works
                var m1 = Math.Min(member.Id, rel.MemberId);
                var m2 = Math.Max(member.Id, rel.MemberId);
                
                var newRel = new FamilyRelationship
                {
                    Member1Id = m1,
                    Member2Id = m2,
                    RelationType = rel.RelationType
                };
                _context.FamilyRelationships.Add(newRel);
            }
        }

        private FamilyMemberDto MapToDto(FamilyMember member)
        {
            var children = (member.ChildrenAsFather ?? Enumerable.Empty<FamilyMember>())
                .Concat(member.ChildrenAsMother ?? Enumerable.Empty<FamilyMember>())
                .DistinctBy(c => c.Id)
                .ToList();

            var horizRelations = (member.RelationsAsMember1 ?? Enumerable.Empty<FamilyRelationship>())
                .Select(r => new HorizontalRelationDto { MemberId = r.Member2Id, RelationType = r.RelationType })
                .Concat((member.RelationsAsMember2 ?? Enumerable.Empty<FamilyRelationship>())
                    .Select(r => new HorizontalRelationDto { MemberId = r.Member1Id, RelationType = r.RelationType }))
                .ToList();

            return new FamilyMemberDto
            {
                Id = member.Id,
                FullName = member.FullName,
                Generation = member.Generation,
                Gender = member.Gender,
                DateOfBirth = member.DateOfBirth,
                Role = member.Role,
                Address = member.Address,
                PhoneNumber = member.PhoneNumber,
                FacebookUrl = member.FacebookUrl,
                InstagramUrl = member.InstagramUrl,
                AvatarUrl = member.AvatarUrl,
                Biography = member.Biography,
                FatherId = member.FatherId,
                FatherName = member.Father?.FullName,
                MotherId = member.MotherId,
                MotherName = member.Mother?.FullName,
                SpouseId = member.SpouseId,
                SpouseName = member.Spouse?.FullName,
                ChildIds = children.Select(c => c.Id).ToList(),
                ChildNames = children.Select(c => c.FullName).ToList(),
                HorizontalRelations = horizRelations,
                CreatedAt = member.CreatedAt,
                UpdatedAt = member.UpdatedAt
            };
        }
    }
}

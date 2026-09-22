using System;
using System.Collections.Generic;

namespace MyLife.Features.FamilyTree.DTOs
{
    public class HorizontalRelationDto
    {
        public int MemberId { get; set; }
        public string RelationType { get; set; } = null!;
    }

    public class FamilyMemberDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = null!;
        public int Generation { get; set; }
        public string? Gender { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Role { get; set; }
        public string? Address { get; set; }
        public string? PhoneNumber { get; set; }
        public string? FacebookUrl { get; set; }
        public string? InstagramUrl { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Biography { get; set; }
        
        public int? FatherId { get; set; }
        public string? FatherName { get; set; }
        
        public int? MotherId { get; set; }
        public string? MotherName { get; set; }
        
        public int? SpouseId { get; set; }
        public string? SpouseName { get; set; }
        
        public List<int> ChildIds { get; set; } = new();
        public List<string> ChildNames { get; set; } = new();
        
        public List<HorizontalRelationDto> HorizontalRelations { get; set; } = new();
        
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

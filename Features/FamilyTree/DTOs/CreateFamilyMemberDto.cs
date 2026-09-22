using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MyLife.Features.FamilyTree.DTOs
{
    public class CreateFamilyMemberDto
    {
        [Required(ErrorMessage = "Họ tên là bắt buộc")]
        [MaxLength(100, ErrorMessage = "Họ tên không vượt quá 100 ký tự")]
        public string FullName { get; set; } = null!;

        [Required(ErrorMessage = "Thế hệ là bắt buộc")]
        public int Generation { get; set; }

        [MaxLength(20)]
        public string? Gender { get; set; }

        public DateOnly? DateOfBirth { get; set; }

        [MaxLength(50)]
        public string? Role { get; set; }

        [MaxLength(255)]
        public string? Address { get; set; }

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(255)]
        public string? FacebookUrl { get; set; }

        [MaxLength(255)]
        public string? InstagramUrl { get; set; }

        public string? AvatarUrl { get; set; }

        public string? Biography { get; set; }

        public int? FatherId { get; set; }
        public int? MotherId { get; set; }
        public int? SpouseId { get; set; }
        public List<int>? ChildIds { get; set; }
        public List<HorizontalRelationDto>? HorizontalRelations { get; set; }
    }
}

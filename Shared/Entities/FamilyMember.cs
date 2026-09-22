using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyLife.Shared.Entities
{
    [Table("family_members")]
    public class FamilyMember
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("full_name")]
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = null!;

        [Column("generation")]
        public int Generation { get; set; }

        [Column("gender")]
        [MaxLength(20)]
        public string? Gender { get; set; }

        [Column("date_of_birth")]
        public DateOnly? DateOfBirth { get; set; }

        [Column("role")]
        [MaxLength(50)]
        public string? Role { get; set; }

        [Column("address")]
        [MaxLength(255)]
        public string? Address { get; set; }

        [Column("phone_number")]
        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [Column("facebook_url")]
        [MaxLength(255)]
        public string? FacebookUrl { get; set; }

        [Column("instagram_url")]
        [MaxLength(255)]
        public string? InstagramUrl { get; set; }

        [Column("avatar_url")]
        public string? AvatarUrl { get; set; }

        [Column("biography")]
        public string? Biography { get; set; }

        [Column("father_id")]
        public int? FatherId { get; set; }

        [Column("mother_id")]
        public int? MotherId { get; set; }

        [Column("spouse_id")]
        public int? SpouseId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("FatherId")]
        public virtual FamilyMember? Father { get; set; }

        [ForeignKey("MotherId")]
        public virtual FamilyMember? Mother { get; set; }

        [ForeignKey("SpouseId")]
        public virtual FamilyMember? Spouse { get; set; }

        [InverseProperty("Father")]
        public virtual ICollection<FamilyMember> ChildrenAsFather { get; set; } = new List<FamilyMember>();

        [InverseProperty("Mother")]
        public virtual ICollection<FamilyMember> ChildrenAsMother { get; set; } = new List<FamilyMember>();

        [InverseProperty("Member1")]
        public virtual ICollection<FamilyRelationship> RelationsAsMember1 { get; set; } = new List<FamilyRelationship>();

        [InverseProperty("Member2")]
        public virtual ICollection<FamilyRelationship> RelationsAsMember2 { get; set; } = new List<FamilyRelationship>();
    }
}

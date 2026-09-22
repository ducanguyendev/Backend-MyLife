using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyLife.Shared.Entities
{
    [Table("family_relationships")]
    public class FamilyRelationship
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("member1_id")]
        public int Member1Id { get; set; }

        [Column("member2_id")]
        public int Member2Id { get; set; }

        [Column("relation_type")]
        [Required]
        [MaxLength(50)]
        public string RelationType { get; set; } = null!;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("Member1Id")]
        public virtual FamilyMember? Member1 { get; set; }

        [ForeignKey("Member2Id")]
        public virtual FamilyMember? Member2 { get; set; }
    }
}

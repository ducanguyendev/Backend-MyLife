namespace MyLife.Shared.Entities;

public sealed class FamilyTree
{
    public long Id { get; set; }
    public int OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<FamilyMember> Members { get; set; } = new List<FamilyMember>();
}

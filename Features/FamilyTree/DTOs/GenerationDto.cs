using System;

namespace MyLife.Features.FamilyTree.DTOs
{
    public class GenerationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyTreeValidationException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

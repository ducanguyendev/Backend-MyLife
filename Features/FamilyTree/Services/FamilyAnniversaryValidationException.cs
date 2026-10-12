namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyAnniversaryValidationException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

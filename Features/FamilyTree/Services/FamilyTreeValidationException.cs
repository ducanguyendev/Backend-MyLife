namespace MyLife.Features.FamilyTree.Services;

public sealed class FamilyTreeValidationException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public sealed class FamilyTreeStorageException() : Exception("Member avatar storage is unavailable.");

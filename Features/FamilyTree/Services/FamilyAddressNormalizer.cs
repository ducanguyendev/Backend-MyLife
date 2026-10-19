namespace MyLife.Features.FamilyTree.Services;

public static class FamilyAddressNormalizer
{
    public static string? Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        return string.Join(' ', address.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

namespace MyLife.Features.Auth.Services;

public static class DisplayNameGenerator
{
    public static string NormalizeWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string Generate(string? fullName)
    {
        var normalized = NormalizeWhitespace(fullName);
        if (normalized.Length == 0) return string.Empty;
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 1 ? words[0] : $"{words[0]} {words[^1]}";
    }
}

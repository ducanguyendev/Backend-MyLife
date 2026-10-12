using System.Globalization;
using System.Text.RegularExpressions;

namespace MyLife.Features.Library.Services;

public static class LibraryDisplayDateRules
{
    public static bool IsValid(string? value) => IsValid(value, DateOnly.FromDateTime(DateTime.UtcNow));

    public static bool IsValid(string? value, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim();
        return Regex.IsMatch(normalized, "^\\d{2}/\\d{2}/\\d{4}$") &&
            DateOnly.TryParseExact(normalized, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var date) && date <= today;
    }
}

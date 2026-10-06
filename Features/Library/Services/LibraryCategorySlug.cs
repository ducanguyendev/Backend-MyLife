using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyLife.Features.Library.Services;

public static class LibraryCategorySlug
{
    public static string FromName(string name)
    {
        var decomposed = name.Replace('Đ', 'D').Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var letters = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var slug = Regex.Replace(letters.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) slug = "category";
        // all is reserved exclusively for the frontend's unfiltered view.
        if (slug == "all") slug = "all-category";
        return slug[..Math.Min(slug.Length, 64)].TrimEnd('-');
    }

    public static string WithSuffix(string slug, int number)
    {
        var suffix = "-" + number.ToString(CultureInfo.InvariantCulture);
        return slug[..Math.Min(slug.Length, 64 - suffix.Length)].TrimEnd('-') + suffix;
    }
}

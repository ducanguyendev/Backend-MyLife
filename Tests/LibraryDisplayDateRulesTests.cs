using MyLife.Features.Library.Services;
using Xunit;

public sealed class LibraryDisplayDateRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("09/10/2026")]
    [InlineData("29/02/2024")]
    public void Accepts_optional_blank_and_real_past_or_current_dates(string? value) =>
        Assert.True(LibraryDisplayDateRules.IsValid(value, Today));

    [Theory]
    [InlineData("31/02/2026")]
    [InlineData("29/02/2025")]
    [InlineData("32/10/2026")]
    [InlineData("09/13/2026")]
    [InlineData("09/10/26")]
    [InlineData("adasdas")]
    [InlineData("31/12/2026")]
    public void Rejects_malformed_impossible_or_future_dates(string value) =>
        Assert.False(LibraryDisplayDateRules.IsValid(value, Today));
}

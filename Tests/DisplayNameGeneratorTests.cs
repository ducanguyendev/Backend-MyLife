using MyLife.Features.Auth.Services;
using Xunit;

public sealed class DisplayNameGeneratorTests
{
    [Theory]
    [InlineData("Nguyễn Huỳnh Minh Đức", "Nguyễn Đức")]
    [InlineData("Trần Ngọc Mai", "Trần Mai")]
    [InlineData("Lê Minh", "Lê Minh")]
    [InlineData("Duca", "Duca")]
    [InlineData("  Nguyễn   Văn   An ", "Nguyễn An")]
    public void Generate_uses_first_and_last_words_after_whitespace_normalization(string fullName, string expected)
    {
        Assert.Equal(expected, DisplayNameGenerator.Generate(fullName));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Generate_handles_empty_provider_names(string? fullName, string expected)
    {
        Assert.Equal(expected, DisplayNameGenerator.Generate(fullName));
    }
}

using MyLife.Features.FamilyTree.Services;
using Xunit;

public sealed class VietnameseLunarCalendarServiceTests
{
    private readonly VietnameseLunarCalendarService calendar = new(TimeProvider.System);

    [Theory]
    [InlineData(1, 1, 2024, false, "2024-02-10")]
    [InlineData(1, 1, 2025, false, "2025-01-29")]
    [InlineData(1, 2, 2023, true, "2023-03-22")]
    [InlineData(30, 2, 2024, false, "2024-04-08")]
    public void Converts_known_vietnamese_lunar_dates(int day, int month, int year, bool leap, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), calendar.ConvertLunarToSolar(day, month, year, leap));
    }

    [Theory]
    [InlineData(30, 1, 2024, false)]
    [InlineData(1, 2, 2024, true)]
    [InlineData(0, 1, 2024, false)]
    public void Rejects_lunar_dates_that_do_not_exist(int day, int month, int year, bool leap) =>
        Assert.Throws<ArgumentException>(() => calendar.ConvertLunarToSolar(day, month, year, leap));

    [Fact]
    public void Next_occurrence_includes_today_and_calculates_countdown()
    {
        var (todayYear, today, todayDays) = calendar.NextOccurrence(1, 1, false, new DateOnly(2024, 2, 10));
        var (nextYear, next, nextDays) = calendar.NextOccurrence(1, 1, false, new DateOnly(2024, 2, 11));
        Assert.Equal(2024, todayYear);
        Assert.Equal(new DateOnly(2024, 2, 10), today);
        Assert.Equal(0, todayDays);
        Assert.Equal(2025, nextYear);
        Assert.Equal(new DateOnly(2025, 1, 29), next);
        Assert.Equal(353, nextDays);
    }

    [Fact]
    public void Next_leap_month_occurrence_skips_years_without_that_leap_month()
    {
        DateOnly? expected = null;
        var expectedLunarYear = 0;
        for (var year = 2024; year <= 2044 && expected is null; year++)
        {
            try { expected = calendar.ConvertLunarToSolar(1, 2, year, true); expectedLunarYear = year; }
            catch (ArgumentException) { }
        }
        Assert.NotNull(expected);
        var (lunarYear, date, daysUntil) = calendar.NextOccurrence(1, 2, true, new DateOnly(2024, 1, 1));
        Assert.Equal(expected, date);
        Assert.Equal(expectedLunarYear, lunarYear);
        Assert.Equal(date.DayNumber - new DateOnly(2024, 1, 1).DayNumber, daysUntil);
    }

    [Theory]
    [InlineData(2026, 1, 31, 2026, 2, 17, 2026)]
    [InlineData(2026, 12, 20, 2027, 2, 6, 2027)]
    public void Next_occurrence_uses_the_nearest_solar_date_across_gregorian_year_boundaries(
        int todayYear, int todayMonth, int todayDay, int expectedYear, int expectedMonth, int expectedDay, int expectedLunarYear)
    {
        var today = new DateOnly(todayYear, todayMonth, todayDay);
        var (lunarYear, date, daysUntil) = calendar.NextOccurrence(1, 1, false, today);
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), date);
        Assert.Equal(expectedLunarYear, lunarYear);
        Assert.Equal(date.DayNumber - today.DayNumber, daysUntil);
    }

    [Fact]
    public void Current_solar_year_passed_flag_distinguishes_before_and_after_occurrence_in_vietnam_date()
    {
        Assert.False(calendar.HasPassedInCurrentSolarYear(1, 1, false, new DateOnly(2026, 2, 16)));
        Assert.False(calendar.HasPassedInCurrentSolarYear(1, 1, false, new DateOnly(2026, 2, 17)));
        Assert.True(calendar.HasPassedInCurrentSolarYear(1, 1, false, new DateOnly(2026, 2, 18)));
    }
}

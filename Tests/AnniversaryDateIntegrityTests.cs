using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyLife.Features.FamilyTree.Services;
using Xunit;

public sealed class AnniversaryDateIntegrityTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    private sealed class FrozenClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Theory]
    [InlineData("2024-02-29", "2024-02-29")]
    [InlineData("2025-02-27", "2025-02-28")]
    [InlineData("2025-03-01", "2026-02-28")]
    [InlineData("2026-12-31", "2027-02-28")]
    public async Task Solar_recurrence_uses_frozen_vietnam_day_and_preserves_historical_timeline_date(string todayText, string expectedText)
    {
        var today = DateOnly.Parse(todayText);
        var clock = new FrozenClock(DateTimeOffset.Parse(todayText + "T05:00:00Z"));
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"m6.date.{Guid.NewGuid():N}@gmail.com";
        var password = "M6_Qa!" + Guid.NewGuid().ToString("N");
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Synthetic Calendar User", phoneNumber = "0912345678", gender = "Nam",
            dateOfBirth = "1995-05-20", email, password, confirmPassword = password
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        loginRequest.Headers.Add("X-Client-Platform", "mobile");
        using var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/family-tree/anniversaries", new
        {
            eventType = "FAMILY_EVENT", title = "Synthetic leap-day event", originalSolarDate = "2000-02-29"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var record = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(expectedText, record.GetProperty("nextSolarDate").GetString());
        Assert.Equal(DateOnly.Parse(expectedText).DayNumber - today.DayNumber, record.GetProperty("daysUntil").GetInt32());
        Assert.Equal(JsonValueKind.Null, record.GetProperty("nextLunarYear").ValueKind);
        Assert.Equal("2000-02-29", record.GetProperty("originalSolarDate").GetString());
        using var timelineResponse = await client.GetAsync("/api/family-tree/timeline");
        Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
        var timeline = (await timelineResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray()
            .Single(row => row.GetProperty("sourceType").GetString() == "ANNIVERSARY");
        Assert.Equal("2000-02-29", timeline.GetProperty("eventDate").GetString());
        Assert.True(timeline.GetProperty("isDerived").GetBoolean());
        Assert.Equal(-record.GetProperty("id").GetInt64(), timeline.GetProperty("id").GetInt64());
    }

    [Theory]
    [InlineData("2026-12-31T16:59:59Z", "2026-12-31")]
    [InlineData("2026-12-31T17:00:00Z", "2027-01-01")]
    public void Vietnam_business_date_uses_utc_plus_seven_at_year_boundary(string utcNow, string expected)
    {
        var calendar = new VietnameseLunarCalendarService(new FrozenClock(DateTimeOffset.Parse(utcNow)));
        Assert.Equal(DateOnly.Parse(expected), calendar.GetVietnamToday());
    }

    [Theory]
    [InlineData(1800)]
    [InlineData(2200)]
    public void Supported_lunar_boundary_years_convert_and_outside_years_fail(int year)
    {
        var calendar = new VietnameseLunarCalendarService(new FrozenClock(DateTimeOffset.Parse("2024-02-10T05:00:00Z")));
        Assert.Equal(year, calendar.ConvertLunarToSolar(1, 1, year, false).Year);
        Assert.Throws<ArgumentException>(() => calendar.ConvertLunarToSolar(1, 1, 1799, false));
        Assert.Throws<ArgumentException>(() => calendar.ConvertLunarToSolar(1, 1, 2201, false));
    }

    [Fact]
    public void Injected_today_includes_lunar_new_year_with_zero_countdown()
    {
        var calendar = new VietnameseLunarCalendarService(new FrozenClock(DateTimeOffset.Parse("2024-02-09T17:00:00Z")));
        var occurrence = calendar.NextOccurrence(1, 1, false);
        Assert.Equal(2024, occurrence.LunarYear);
        Assert.Equal(new DateOnly(2024, 2, 10), occurrence.SolarDate);
        Assert.Equal(0, occurrence.DaysUntil);
    }
}

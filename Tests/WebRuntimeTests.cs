using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyLife.Shared.Data;
using MyLife.Shared.Web;
using Xunit;

public sealed class LimitedFactory : MyLifeFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
            ["RateLimits:Auth:PermitLimit"] = "2", ["RateLimits:Auth:WindowSeconds"] = "3600",
            ["RateLimits:Refresh:PermitLimit"] = "3", ["RateLimits:Refresh:WindowSeconds"] = "3600",
            ["RateLimits:Upload:PermitLimit"] = "2", ["RateLimits:Upload:WindowSeconds"] = "3600"
        }));
    }
}
public sealed class WebRuntimeTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    [Theory]
    [InlineData("/api/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/google")]
    [InlineData("/api/refresh-token")]
    public async Task Named_auth_limits_reject_deterministically_without_sleep_or_trusting_forwarded_headers(string route)
    {
        await using var limited = new LimitedFactory(); using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var permits = route.Contains("refresh") ? 3 : 2;
        for (var i = 0; i < permits + 1; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(new { }) };
            request.Headers.Add("X-Forwarded-For", $"192.0.2.{i + 1}");
            using var response = await client.SendAsync(request);
            if (i < permits) Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            else { Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); Assert.Equal("RATE_LIMITED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()); Assert.NotNull(response.Headers.RetryAfter); }
        }
    }
    [Fact]
    public async Task Upload_limit_is_per_user_and_does_not_limit_library_reads()
    {
        var a = await User(); var b = await User();
        await using var limited = new LimitedFactory(); using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        async Task<HttpResponseMessage> Upload(string token) {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/library/albums/999999/photos") { Content = new MultipartFormDataContent() };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return await client.SendAsync(request);
        }
        for (var i = 0; i < 2; i++) { using var normal = await Upload(a); Assert.Equal(HttpStatusCode.BadRequest, normal.StatusCode); }
        using var rejected = await Upload(a); Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using var other = await Upload(b); Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/library/categories")).StatusCode);
    }
    [Fact]
    public async Task Health_and_request_id_responses_are_private_safe_and_readiness_can_fail()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        foreach (var path in new[] { "/health/live", "/health/ready" }) {
            using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Add("X-Request-ID", "audit-request_123");
            using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("audit-request_123", Assert.Single(response.Headers.GetValues("X-Request-ID")));
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Healthy", body.GetProperty("status").GetString()); Assert.Single(body.EnumerateObject());
        }
        using var malformed = new HttpRequestMessage(HttpMethod.Get, "/health/live"); malformed.Headers.TryAddWithoutValidation("X-Request-ID", "unsafe request id");
        using var replaced = await client.SendAsync(malformed); Assert.Matches("^[a-f0-9]{32}$", Assert.Single(replaced.Headers.GetValues("X-Request-ID")));
        using var failedServices = new ServiceCollection().AddDbContext<AppDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=9;Database=unavailable_test;Username=mylife_test;Timeout=1")).BuildServiceProvider();
        await using var unavailable = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton(new DatabaseHealthCheck(failedServices.GetRequiredService<IServiceScopeFactory>()))));
        using var failedClient = unavailable.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await failedClient.GetAsync("/health/live")).StatusCode);
        using var unhealthy = await failedClient.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, unhealthy.StatusCode);
        Assert.Equal("{\"status\":\"Unhealthy\"}", await unhealthy.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Private_family_json_supports_gzip_without_output_cache()
    {
        var user = await User(); using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/family-tree"); request.Headers.AcceptEncoding.ParseAdd("gzip");
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        await using var compressed = await response.Content.ReadAsStreamAsync(); await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var json = await JsonDocument.ParseAsync(gzip); Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("data").EnumerateArray()); Assert.Null(response.Headers.Age);
    }
    private async Task<string> User()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var email = $"runtime.{Guid.NewGuid():N}@gmail.com"; const string password = "User_Test!234567";
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Runtime User", phoneNumber = "0912345678", gender = "Nam", dateOfBirth = "1995-05-20", email, password, confirmPassword = password }); Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/login") { Content = JsonContent.Create(new { email, password }) }; request.Headers.Add("X-Client-Platform", "mobile");
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }
}

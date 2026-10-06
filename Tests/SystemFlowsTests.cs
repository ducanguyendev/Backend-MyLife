using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyLife.Shared.Data;
using MyLife.Features.Auth.Services;
using MyLife.Features.Avatar.Services;
using MyLife.Features.Library.Services;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class MyLifeFactory : WebApplicationFactory<Program>
{
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable("MYLIFE_TEST_DATABASE")
        ?? "Host=127.0.0.1;Port=55439;Database=mylife_validation;Username=mylife_test;Include Error Detail=false";

    public MyLifeFactory()
    {
        // Minimal-hosting validation runs before ConfigureWebHost callbacks, so
        // test-only settings must exist before Program constructs its builder.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
        Environment.SetEnvironmentVariable("JwtSettings__SecretKey", "test-only-secret-with-at-least-thirty-two-characters");
        Environment.SetEnvironmentVariable("JwtSettings__Issuer", "MyLife.Tests");
        Environment.SetEnvironmentVariable("JwtSettings__Audience", "MyLife.TestClients");
        Environment.SetEnvironmentVariable("JwtSettings__AccessTokenSeconds", "60");
        Environment.SetEnvironmentVariable("JwtSettings__RefreshTokenMinutes", "60");
        Environment.SetEnvironmentVariable("Authentication__Google__ClientId", "test-client.apps.googleusercontent.com");
        Environment.SetEnvironmentVariable("SeedAdmin__Email", "integration.admin@gmail.com");
        Environment.SetEnvironmentVariable("SeedAdmin__Password", "Admin_Test!234567");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost:7000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["JwtSettings:SecretKey"] = "test-only-secret-with-at-least-thirty-two-characters",
            ["JwtSettings:Issuer"] = "MyLife.Tests",
            ["JwtSettings:Audience"] = "MyLife.TestClients",
            ["JwtSettings:AccessTokenSeconds"] = "60",
            ["JwtSettings:RefreshTokenMinutes"] = "60",
            ["Authentication:Google:ClientId"] = "test-client.apps.googleusercontent.com",
            ["SeedAdmin:Email"] = "integration.admin@gmail.com",
            ["SeedAdmin:Password"] = "Admin_Test!234567",
            ["Cors:AllowedOrigins:0"] = "http://localhost:7000"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGoogleDriveAvatarService>();
            services.RemoveAll<IGoogleCredentialVerifier>();
            services.AddSingleton<TestAvatarStorage>();
            services.AddSingleton<IGoogleDriveAvatarService>(provider => provider.GetRequiredService<TestAvatarStorage>());
            services.AddSingleton<IGoogleCredentialVerifier, TestGoogleCredentialVerifier>();
            services.RemoveAll<ILibraryStorageService>();
            services.AddSingleton<TestLibraryStorage>();
            services.AddSingleton<ILibraryStorageService>(provider => provider.GetRequiredService<TestLibraryStorage>());
            services.AddSingleton<LibraryPersistenceFailureInterceptor>();
            services.AddDbContext<AppDbContext>((provider, options) =>
                options.AddInterceptors(provider.GetRequiredService<LibraryPersistenceFailureInterceptor>()));
            services.AddHttpClient(nameof(GoogleAvatarSyncService))
                .ConfigurePrimaryHttpMessageHandler(() => new TestGoogleImageHandler());
        });
    }
}

public sealed class SystemFlowsTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    private readonly HttpClient client = CreateClient(factory);

    [Fact]
    public async Task Auth_refresh_admin_and_family_tree_flows()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"flow.{suffix}@gmail.com";
        const string password = "User_Test!234567";

        Assert.Equal(HttpStatusCode.Created, (await Post("/api/auth/register", new
        {
            fullName = "Flow User", phoneNumber = "0912345678", gender = "Nam",
            dateOfBirth = "1995-05-20", email, password, confirmPassword = password
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginResponse(email, "Wrong_Test!234")).StatusCode);

        var user = await Login(email, password);
        Assert.True(user.AccessExpiresIn > 0 && user.RefreshExpiresIn > 0);
        Assert.Equal("USER", (await Body(await Get("/api/me", user.AccessToken))).GetProperty("role").GetString());

        var refreshResults = await Task.WhenAll(Refresh(user), Refresh(user));
        Assert.Single(refreshResults, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(refreshResults, x => x.StatusCode == HttpStatusCode.Unauthorized);
        var rotated = ParseSession(await Body(refreshResults.Single(x => x.StatusCode == HttpStatusCode.OK)));
        Assert.NotEqual(user.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(user)).StatusCode);

        var admin = await Login("integration.admin@gmail.com", "Admin_Test!234567");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/refresh-token",
            new { accessToken = admin.AccessToken, refreshToken = rotated.RefreshToken })).StatusCode);

        var expiring = await Login(email, password);
        await ExpireRefreshToken(expiring.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(expiring)).StatusCode);

        var users = await Body(await Get($"/api/admin/users?search={Uri.EscapeDataString(email)}", admin.AccessToken));
        var targetId = users.EnumerateArray().Single().GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/admin/stats", rotated.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/admin/users/{targetId}/status", new { isActive = false }, admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/me", rotated.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginResponse(email, password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/admin/users/{targetId}/status", new { isActive = true }, admin.AccessToken)).StatusCode);

        var fresh = await Login(email, password);
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/admin/users/{targetId}/role", new { role = "ADMIN" }, admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/admin/users/{targetId}/role", new { role = "ADMIN" }, admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get("/api/admin/stats", fresh.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/admin/users/{targetId}/role", new { role = "USER" }, admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/admin/stats", fresh.AccessToken)).StatusCode);

        await VerifyFamilyTree(fresh.AccessToken);

        var logout = await Login(email, password);
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/logout", new
            { accessToken = logout.AccessToken, refreshToken = logout.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Delete($"/api/admin/users/{targetId}", admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get("/api/me", fresh.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/auth/google", new
            { idToken = "not.a.valid.google.token" })).StatusCode);
    }

    [Fact]
    public async Task Legacy_schema_is_baselined_only_by_opt_in_and_preserves_data()
    {
        var schema = $"legacy_{Guid.NewGuid():N}";
        var baseBuilder = new NpgsqlConnectionStringBuilder(MyLifeFactory.ConnectionString);
        await using var setup = new NpgsqlConnection(baseBuilder.ConnectionString);
        await setup.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup).ExecuteNonQueryAsync();
        try
        {
            var scopedBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString) { SearchPath = schema };
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(scopedBuilder.ConnectionString).Options;
            await using (var initial = new AppDbContext(options))
            {
                await initial.GetService<IMigrator>().MigrateAsync("20260921072914_AddFamilyRelationships");
                await initial.Database.ExecuteSqlRawAsync("""
                    INSERT INTO roles (id, name) VALUES (10, 'Member');
                    INSERT INTO users (id, email, password_hash, auth_provider, is_active, created_at, updated_at)
                    VALUES (20, 'legacy@gmail.com', 'preserved-local-hash', 1, true, now(), now());
                    INSERT INTO user_roles (user_id, role_id) VALUES (20, 10);
                    INSERT INTO refresh_tokens (user_id, token, expires_at, is_revoked, created_at)
                    VALUES (20, 'legacy-raw-refresh', now() + interval '1 hour', false, now());
                    DROP TABLE "__EFMigrationsHistory";
                    """);
            }

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Database:AllowLegacyBaseline"] = "true" }).Build();
            await using var upgraded = new AppDbContext(options);
            await DatabaseStartup.MigrateAndSeedAsync(upgraded, config);
            var legacy = await upgraded.Users.SingleAsync(x => x.Email == "legacy@gmail.com");
            Assert.True(legacy.HasLocalProvider);
            Assert.True(legacy.HasGoogleProvider);
            Assert.Equal("preserved-local-hash", legacy.PasswordHash);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("legacy-raw-refresh"))),
                await upgraded.RefreshTokens.Where(x => x.UserId == legacy.Id).Select(x => x.Token).SingleAsync());
            Assert.Contains(await upgraded.UserRoles.Where(x => x.UserId == legacy.Id).Select(x => x.Role.Name).ToListAsync(),
                role => role == "USER");
        }
        finally
        {
            if (!schema.StartsWith("legacy_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test schema name.");
            await new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup).ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Web_session_uses_httponly_cookies_and_enforces_csrf_header()
    {
        using var web = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"web.{suffix}@gmail.com";
        const string password = "Web_Test!234567";
        var registration = await web.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Web User", phoneNumber = "0912345678", gender = "Nữ",
            dateOfBirth = "1998-03-12", email, password, confirmPassword = password
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var login = await web.PostAsJsonAsync("/api/login", new { email, password, rememberMe = true });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await Body(login);
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.False(body.TryGetProperty("refreshToken", out _));
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("AccessToken=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("RefreshToken=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await web.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await web.PostAsync("/api/logout", null)).StatusCode);
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/logout");
        logout.Headers.Add("X-Requested-With", "MyLife");
        Assert.Equal(HttpStatusCode.OK, (await web.SendAsync(logout)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await web.GetAsync("/api/me")).StatusCode);
    }

    private async Task VerifyFamilyTree(string token)
    {
        var a = await CreateMember("Alpha", "Nam", token);
        var b = await CreateMember("Beta", "Nữ", token);
        var c = await CreateMember("Gamma", "Nam", token);
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/family-tree/{a}", Member("Alpha", "Nam", spouseId: b), token)).StatusCode);
        Assert.Equal(a, (await Body(await Get($"/api/family-tree/{b}", token))).GetProperty("data").GetProperty("spouseId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/family-tree/{a}", Member("Alpha", "Nam", spouseId: c), token)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Body(await Get($"/api/family-tree/{b}", token))).GetProperty("data").GetProperty("spouseId").ValueKind);
        using var selfRelation = await Put($"/api/family-tree/{a}", Member("Alpha", "Nam", spouseId: a), token);
        Assert.Equal(HttpStatusCode.BadRequest, selfRelation.StatusCode);
        Assert.Equal("FAMILY_SELF_RELATION", (await Body(selfRelation)).GetProperty("code").GetString());
        using var missingRelated = await Put($"/api/family-tree/{a}", Member("Alpha", "Nam", fatherId: 999999), token);
        Assert.Equal(HttpStatusCode.BadRequest, missingRelated.StatusCode);
        Assert.Equal("FAMILY_RELATED_MEMBER_NOT_FOUND", (await Body(missingRelated)).GetProperty("code").GetString());
        using var missingMember = await Get("/api/family-tree/2147483647", token);
        Assert.Equal(HttpStatusCode.NotFound, missingMember.StatusCode);
        Assert.Equal("FAMILY_MEMBER_NOT_FOUND", (await Body(missingMember)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/family-tree/{b}", Member("Beta", "Nữ", fatherId: a), token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put($"/api/family-tree/{a}", new
            { fullName = "Alpha", generation = 1, gender = "Nam", fatherId = b }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put($"/api/family-tree/{a}", Member("Alpha", "Nam", fatherId: b, childIds: [b]), token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put($"/api/family-tree/{c}", new
        {
            fullName = "Gamma", generation = 1, gender = "Nam", childIds = Array.Empty<int>(),
            horizontalRelations = new[] { new { memberId = b, relationType = "Sibling" }, new { memberId = b, relationType = "sibling" } }
        }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get("/api/admin/family-tree", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Delete($"/api/family-tree/{c}", token)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Body(await Get($"/api/family-tree/{a}", token))).GetProperty("data").GetProperty("spouseId").ValueKind);
    }

    private async Task<int> CreateMember(string name, string gender, string token)
    {
        var response = await Post("/api/family-tree", Member(name, gender), token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("data").GetProperty("id").GetInt32();
    }

    private static object Member(string name, string gender, int? fatherId = null, int? spouseId = null, int[]? childIds = null) =>
        new { fullName = name, generation = 1, gender, fatherId, spouseId, childIds = childIds ?? [] };

    private static async Task ExpireRefreshToken(string token)
    {
        await using var connection = new NpgsqlConnection(MyLifeFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE refresh_tokens SET expires_at = now() - interval '1 second' WHERE token = @token", connection);
        command.Parameters.AddWithValue("token", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task<Session> Login(string email, string password)
    {
        var response = await LoginResponse(email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ParseSession(await Body(response));
    }

    private Task<HttpResponseMessage> LoginResponse(string email, string password) =>
        Post("/api/login", new { email, password, rememberMe = true });
    private Task<HttpResponseMessage> Refresh(Session session) =>
        Post("/api/refresh-token", new { accessToken = session.AccessToken, refreshToken = session.RefreshToken });
    private static Session ParseSession(JsonElement body) => new(
        body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!,
        body.GetProperty("accessTokenExpiresIn").GetInt32(), body.GetProperty("refreshTokenExpiresIn").GetInt32());

    private Task<HttpResponseMessage> Get(string path, string? token = null) => Send(HttpMethod.Get, path, null, token);
    private Task<HttpResponseMessage> Post(string path, object body, string? token = null) => Send(HttpMethod.Post, path, body, token);
    private Task<HttpResponseMessage> Put(string path, object body, string? token = null) => Send(HttpMethod.Put, path, body, token);
    private Task<HttpResponseMessage> Delete(string path, string? token = null) => Send(HttpMethod.Delete, path, null, token);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Client-Platform", "mobile");
        request.Headers.Add("X-Requested-With", "MyLife");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static HttpClient CreateClient(MyLifeFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private sealed record Session(string AccessToken, string RefreshToken, int AccessExpiresIn, int RefreshExpiresIn);
}

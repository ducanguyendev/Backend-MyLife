using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyLife.Features.Auth.Models;
using MyLife.Features.Auth.Services;
using MyLife.Features.Avatar.Services;
using Npgsql;
using Xunit;

public sealed class AvatarFlowsTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    private readonly HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    private readonly TestAvatarStorage storage = factory.Services.GetRequiredService<TestAvatarStorage>();

    [Fact]
    public async Task One_hundred_manual_uploads_keep_id_bytes_and_mime_after_logout_and_login()
    {
        var email = $"avatar.repeat.{Guid.NewGuid():N}@gmail.com";
        const string password = "User_Test!234567";
        await Register(email, password);
        var token = await Login(email, password);
        var formats = new[] { ("jpg", "image/jpeg"), ("webp", "image/webp"), ("png", "image/png"), ("webp", "image/webp"), ("jpg", "image/jpeg") };
        string? id = null;
        for (var index = 0; index < 100; index++)
        {
            byte[] bytes = [(byte)index, (byte)(255 - index), 128, 254];
            var (extension, mime) = formats[index % formats.Length];
            await Upload(token, $"avatar-{index}.{extension}", bytes, mime);
            var row = await GetAvatarRow(email);
            id ??= row.FileId;
            Assert.NotNull(id);
            Assert.Equal(id, row.FileId);
            Assert.Equal($"https://lh3.googleusercontent.com/d/{id}", row.Url);
            Assert.Equal("MANUAL", row.Source);
            var call = storage.UploadCalls.Last(c => c.Email == email);
            Assert.Equal(index == 0 ? null : id, call.ExistingFileId);
            Assert.Equal(bytes, call.Content);
            Assert.Equal(mime, call.ContentType);
            Assert.Equal(bytes, storage.ActiveFiles[id].Content);
            Assert.Equal(mime, storage.ActiveFiles[id].ContentType);
            Assert.Single(storage.ActiveFiles.Values, file => file.Email == email);
            if (index is 4 or 99)
            {
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/logout", null)).StatusCode);
                token = await Login(email, password);
                using var me = new HttpRequestMessage(HttpMethod.Get, "/api/me");
                me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await client.SendAsync(me);
                response.EnsureSuccessStatusCode();
                var info = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(row.Url, info.GetProperty("avatarUrl").GetString());
                Assert.Equal(bytes, storage.ActiveFiles[id].Content);
            }
        }
        Assert.Equal(100, storage.UploadCalls.Count(call => call.Email == email));
        // Re-select exactly the last image and retry after a failed overwrite.
        await Upload(token, "same.jpg", [99, 156, 128, 254]);
        storage.FailNextUpload = true;
        using (var failed = await SendUpload(token, "failed.webp", [5], "image/webp"))
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(id, (await GetAvatarRow(email)).FileId);
        Assert.Equal(new byte[] { 99, 156, 128, 254 }, storage.ActiveFiles[id!].Content);
        await Upload(token, "retry.webp", [6, 128], "image/webp");
        Assert.Equal(id, (await GetAvatarRow(email)).FileId);
        Assert.Equal(new byte[] { 6, 128 }, storage.ActiveFiles[id!].Content);
    }

    [Fact]
    public async Task Local_login_keeps_avatar_null_and_manual_upload_reuses_file_id_then_delete_clears_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"avatar.local.{suffix}@gmail.com";
        const string password = "User_Test!234567";
        await Register(email, password);
        var token = await Login(email, password);

        var before = await GetAvatarRow(email);
        Assert.Null(before.Url);
        Assert.Null(before.FileId);

        var callStart = storage.UploadCalls.Count;
        var firstUrl = await Upload(token, "avatar-a.jpg", [1, 2, 3]);
        var first = await GetAvatarRow(email);
        Assert.NotNull(first.FileId);
        Assert.Equal("MANUAL", first.Source);
        Assert.StartsWith("https://lh3.googleusercontent.com/d/", first.Url);
        Assert.Contains("?t=", firstUrl);

        var secondUrl = await Upload(token, "avatar-b.webp", [4, 5, 6], "image/webp");
        var second = await GetAvatarRow(email);
        Assert.Equal(first.FileId, second.FileId);
        Assert.Equal(first.Url, second.Url);
        Assert.Contains("?t=", secondUrl);
        Assert.Equal(first.FileId, storage.UploadCalls.ElementAt(callStart + 1).ExistingFileId);
        Assert.Null(storage.UploadCalls.ElementAt(callStart).ExistingFileId);
        Assert.Equal(new byte[] { 4, 5, 6 }, storage.UploadCalls.ElementAt(callStart + 1).Content);
        Assert.Equal("image/webp", storage.UploadCalls.ElementAt(callStart + 1).ContentType);
        Assert.Equal($"https://lh3.googleusercontent.com/d/{first.FileId}", second.Url);

        using (var redirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        using (var redirect = await redirectClient.GetAsync($"/api/avatar/{Uri.EscapeDataString(email)}?t=123456"))
        {
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            Assert.Equal($"{second.Url}?t=123456", redirect.Headers.Location?.ToString());
            Assert.True(redirect.Headers.CacheControl?.NoStore);
        }

        storage.FailNextUpload = true;
        using (var failed = await SendUpload(token, "failed.webp", [7, 8], "image/webp"))
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(second, await GetAvatarRow(email));

        storage.FailNextDelete = true;
        using (var failedDelete = new HttpRequestMessage(HttpMethod.Delete, "/api/avatar"))
        {
            failedDelete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(failedDelete)).StatusCode);
        }
        Assert.Equal(second, await GetAvatarRow(email));

        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/avatar");
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(delete)).StatusCode);
        var deleted = await GetAvatarRow(email);
        Assert.Null(deleted.Url);
        Assert.Null(deleted.FileId);
        Assert.Null(deleted.Source);
        Assert.Null(deleted.GoogleSourceUrl);
    }

    [Fact]
    public async Task Google_avatar_sync_is_idempotent_updates_same_file_and_never_overwrites_manual_avatar()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"avatar.google.{suffix}@gmail.com";
        var subject = $"subject-{suffix}";
        var start = storage.UploadCalls.Count;

        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(email, subject, "v1")).StatusCode);
        var first = await GetAvatarRow(email);
        Assert.Equal("GOOGLE", first.Source);
        Assert.NotNull(first.FileId);
        Assert.Contains("/v1", first.GoogleSourceUrl);
        Assert.Equal(start + 1, storage.UploadCalls.Count);

        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(email, subject, "v1")).StatusCode);
        Assert.Equal(start + 1, storage.UploadCalls.Count);

        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(email, subject, "v2")).StatusCode);
        var changed = await GetAvatarRow(email);
        Assert.Equal(first.FileId, changed.FileId);
        Assert.Contains("/v2", changed.GoogleSourceUrl);
        Assert.Equal(first.FileId, storage.UploadCalls.ElementAt(start + 1).ExistingFileId);

        for (var version = 3; version <= 5; version++)
        {
            Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(email, subject, $"v{version}")).StatusCode);
            Assert.Equal(first.FileId, (await GetAvatarRow(email)).FileId);
            var call = storage.UploadCalls.Last(c => c.Email == email);
            Assert.Equal(first.FileId, call.ExistingFileId);
            Assert.Equal(Encoding.UTF8.GetBytes($"/avatar/v{version}"), call.Content);
            Assert.Equal(call.Content, storage.ActiveFiles[first.FileId!].Content);
        }

        var token = await LoginWithGoogle(email, subject, "v5");
        await Upload(token, "manual.jpg", [9, 8, 7]);
        var manual = await GetAvatarRow(email);
        Assert.Equal("MANUAL", manual.Source);
        var callsAfterManual = storage.UploadCalls.Count;

        Assert.Equal(HttpStatusCode.OK, (await GoogleLogin(email, subject, "v6")).StatusCode);
        var afterLogin = await GetAvatarRow(email);
        Assert.Equal("MANUAL", afterLogin.Source);
        Assert.Equal(manual.FileId, afterLogin.FileId);
        Assert.Equal(callsAfterManual, storage.UploadCalls.Count);
    }

    [Fact]
    public async Task Google_avatar_storage_failure_does_not_fail_login()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"avatar.failure.{suffix}@gmail.com";
        storage.FailNextUpload = true;

        var response = await GoogleLogin(email, $"subject-{suffix}", "unavailable");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await GetAvatarRow(email);
        Assert.Null(row.Url);
        Assert.Null(row.FileId);
        Assert.Null(row.Source);
    }

    private async Task Register(string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Avatar Test", phoneNumber = "0912345678", gender = "Nam",
            dateOfBirth = "1995-05-20", email, password, confirmPassword = password
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<string> Login(string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/login")
        {
            Content = JsonContent.Create(new { email, password, rememberMe = false })
        };
        request.Headers.Add("X-Client-Platform", "mobile");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> GoogleLogin(string email, string subject, string pictureKey)
    {
        using var request = GoogleRequest(email, subject, pictureKey);
        return await client.SendAsync(request);
    }

    private async Task<string> LoginWithGoogle(string email, string subject, string pictureKey)
    {
        using var request = GoogleRequest(email, subject, pictureKey);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static HttpRequestMessage GoogleRequest(string email, string subject, string pictureKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/google")
        {
            Content = JsonContent.Create(new { idToken = $"test|{email}|{subject}|{pictureKey}" })
        };
        request.Headers.Add("X-Client-Platform", "mobile");
        return request;
    }

    private async Task<string> Upload(string accessToken, string fileName, byte[] bytes, string contentType = "image/jpeg")
    {
        using var response = await SendUpload(accessToken, fileName, bytes, contentType);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("avatarUrl").GetString()!;
    }

    private async Task<HttpResponseMessage> SendUpload(string accessToken, string fileName, byte[] bytes, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(image, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/avatar/upload") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static async Task<(string? Url, string? FileId, string? Source, string? GoogleSourceUrl)> GetAvatarRow(string email)
    {
        await using var connection = new NpgsqlConnection(MyLifeFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT avatar_url, avatar_drive_file_id, avatar_source, google_avatar_source_url FROM users WHERE email = @email",
            connection);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }
}

public sealed class TestAvatarStorage : IGoogleDriveAvatarService
{
    public ConcurrentQueue<UploadCall> UploadCalls { get; } = [];
    public ConcurrentDictionary<string, StoredAvatar> ActiveFiles { get; } = [];
    public bool FailNextUpload { get; set; }
    public bool FailNextDelete { get; set; }

    public async Task<AvatarStorageResult> UploadAvatarAsync(
        string email, IFormFile file, string? existingFileId, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return await UploadAvatarAsync(email, stream.ToArray(), file.ContentType, existingFileId, cancellationToken);
    }

    public Task<AvatarStorageResult> UploadAvatarAsync(
        string email, byte[] content, string contentType, string? existingFileId, CancellationToken cancellationToken = default)
    {
        UploadCalls.Enqueue(new(email, existingFileId, content.ToArray(), contentType));
        if (FailNextUpload)
        {
            FailNextUpload = false;
            return Task.FromResult(new AvatarStorageResult(false, null, null));
        }
        var fileId = existingFileId ?? $"test-drive-{Guid.NewGuid():N}";
        ActiveFiles[fileId] = new(email, content.ToArray(), contentType);
        return Task.FromResult(new AvatarStorageResult(true, $"https://lh3.googleusercontent.com/d/{fileId}", fileId));
    }

    public Task<bool> DeleteAvatarAsync(string email, string? fileId, CancellationToken cancellationToken = default)
    {
        var success = !FailNextDelete;
        FailNextDelete = false;
        if (success && fileId is not null) ActiveFiles.TryRemove(fileId, out _);
        return Task.FromResult(success);
    }

    public sealed record UploadCall(string Email, string? ExistingFileId, byte[] Content, string ContentType);
    public sealed record StoredAvatar(string Email, byte[] Content, string ContentType);
}

public sealed class TestGoogleCredentialVerifier : IGoogleCredentialVerifier
{
    public Task<GoogleIdentity?> VerifyAsync(GoogleAuthRequest request, CancellationToken cancellationToken = default)
    {
        var parts = request.IdToken?.Split('|');
        if (parts is not { Length: 4 } || parts[0] != "test") return Task.FromResult<GoogleIdentity?>(null);
        return Task.FromResult<GoogleIdentity?>(new(
            parts[1], parts[2], "Google Test", $"https://lh3.googleusercontent.com/avatar/{parts[3]}", true));
    }
}

public sealed class TestGoogleImageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(request.RequestUri!.AbsolutePath))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return Task.FromResult(response);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyLife.Features.Avatar.Services;
using Xunit;

public sealed class GoogleDriveAvatarServiceTests
{
    [Fact]
    public async Task Upload_and_delete_send_the_existing_drive_file_id()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler);

        var result = await service.UploadAvatarAsync(
            "User@Gmail.com",
            [1, 2, 3],
            "image/jpeg",
            "stable-id");
        Assert.True(result.Success);
        Assert.Equal("stable-id", result.FileId);

        Assert.True(await service.DeleteAvatarAsync("User@Gmail.com", "stable-id"));
        Assert.Equal(2, handler.Payloads.Count);
        Assert.Equal("stable-id", handler.Payloads[0].GetProperty("existingFileId").GetString());
        Assert.Equal("stable-id", handler.Payloads[1].GetProperty("fileId").GetString());
        Assert.Equal("user@gmail.com", handler.Payloads[0].GetProperty("email").GetString());
        Assert.Equal(new byte[] { 1, 2, 3 }, Convert.FromBase64String(handler.Payloads[0].GetProperty("fileBase64").GetString()!));
    }

    [Fact]
    public async Task First_upload_then_webp_replacement_preserves_id_and_sends_new_bytes_and_mime()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler);
        var first = await service.UploadAvatarAsync("user@gmail.com", [1, 2, 3], "image/jpeg", null);
        var second = await service.UploadAvatarAsync("user@gmail.com", [9, 8, 7, 6], "image/webp", first.FileId);
        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.FileId, second.FileId);
        Assert.Equal(JsonValueKind.Null, handler.Payloads[0].GetProperty("existingFileId").ValueKind);
        Assert.Equal(first.FileId, handler.Payloads[1].GetProperty("existingFileId").GetString());
        Assert.Equal("image/webp", handler.Payloads[1].GetProperty("contentType").GetString());
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, Convert.FromBase64String(handler.Payloads[1].GetProperty("fileBase64").GetString()!));
        Assert.Equal("https://lh3.googleusercontent.com/d/stable-id", second.DriveUrl);
    }

    [Theory]
    [InlineData("{\"success\":false,\"fileId\":\"stable-id\"}")]
    [InlineData("{\"fileId\":\"stable-id\"}")]
    [InlineData("{\"success\":true,\"fileId\":\"different-id\"}")]
    [InlineData("{\"success\":true}")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Unconfirmed_or_changed_identity_is_failure(string response)
    {
        var handler = new RecordingHandler { ResponseJson = response };
        var result = await CreateService(handler).UploadAvatarAsync("user@gmail.com", [4], "image/webp", "stable-id");
        Assert.False(result.Success);
        Assert.Null(result.FileId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"success\":false}")]
    [InlineData("{\"success\":true,\"deleted\":false}")]
    [InlineData("{\"success\":true}")]
    [InlineData("[]")]
    public async Task Delete_requires_explicit_storage_success(string response)
    {
        Assert.False(await CreateService(new RecordingHandler { ResponseJson = response })
            .DeleteAvatarAsync("user@gmail.com", "stable-id"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("old-checksum", "image/webp")]
    [InlineData("55A54008AD1BA589AA210D2629C1DF41", "image/jpeg")]
    public async Task Unverified_bytes_or_mime_are_failure(string? checksum, string? contentType)
    {
        var handler = new RecordingHandler { ResponseJson = JsonSerializer.Serialize(new
        {
            success = true, fileId = "stable-id", md5Checksum = checksum, contentType
        }) };
        var result = await CreateService(handler).UploadAvatarAsync("user@gmail.com", [1], "image/webp", "stable-id");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Invalid_storage_json_is_a_safe_failure()
    {
        var handler = new RecordingHandler { ReturnInvalidJson = true };
        var result = await CreateService(handler).UploadAvatarAsync(
            "user@gmail.com",
            [1],
            "image/png",
            null);
        Assert.False(result.Success);
        Assert.Null(result.FileId);
    }

    private static GoogleDriveAvatarService CreateService(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleDrive:WebAppUrl"] = "https://script.google.com/test"
        }).Build();
        return new GoogleDriveAvatarService(
            configuration,
            new SingleClientFactory(new HttpClient(handler)),
            NullLogger<GoogleDriveAvatarService>.Instance);
    }

    [Fact]
    public async Task One_hundred_uploads_send_current_id_and_fresh_bytes_every_time()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler);
        string? fileId = null;
        var formats = new[] { "image/jpeg", "image/webp", "image/png", "image/webp", "image/jpeg" };
        for (var index = 0; index < 100; index++)
        {
            byte[] bytes = [(byte)index, (byte)(255 - index), 128, 254];
            var result = await service.UploadAvatarAsync("user@gmail.com", bytes, formats[index % 5], fileId);
            Assert.True(result.Success);
            Assert.Equal("stable-id", result.FileId);
            var payload = handler.Payloads[index];
            Assert.Equal(fileId, payload.GetProperty("existingFileId").GetString());
            Assert.Equal(bytes, Convert.FromBase64String(payload.GetProperty("fileBase64").GetString()!));
            Assert.Equal(formats[index % 5], payload.GetProperty("contentType").GetString());
            fileId = result.FileId;
        }
        Assert.Equal(100, handler.Payloads.Count);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<JsonElement> Payloads { get; } = [];
        public bool ReturnInvalidJson { get; init; }
        public string? ResponseJson { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            Payloads.Add(JsonDocument.Parse(json).RootElement.Clone());
            var payload = Payloads[^1];
            var defaultResponse = payload.GetProperty("action").GetString() == "upload"
                ? JsonSerializer.Serialize(new
                {
                    success = true, fileId = "stable-id", url = "https://lh3.googleusercontent.com/d/stable-id?t=old",
                    md5Checksum = Convert.ToHexString(MD5.HashData(Convert.FromBase64String(payload.GetProperty("fileBase64").GetString()!))),
                    contentType = payload.GetProperty("contentType").GetString()
                }) : "{\"success\":true,\"deleted\":true}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    ReturnInvalidJson
                        ? "not-json"
                        : ResponseJson ?? defaultResponse,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}

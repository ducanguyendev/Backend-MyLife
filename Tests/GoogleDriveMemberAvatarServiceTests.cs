using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyLife.Features.FamilyTree.Services;
using Xunit;

public sealed class GoogleDriveMemberAvatarServiceTests
{
    [Fact]
    public async Task Five_formats_and_delete_use_member_contract_and_preserve_X()
    {
        var handler = new RecordingHandler(); var service = Create(handler);
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "avatar-replacements.json")));
        string? id = null;
        foreach (var fixture in fixtures.RootElement.EnumerateArray())
        {
            var bytes = Convert.FromBase64String(fixture.GetProperty("base64").GetString()!);
            var mime = fixture.GetProperty("contentType").GetString()!;
            var result = await service.UploadAvatarAsync(15, bytes, mime, id);
            Assert.True(result.Success); Assert.Equal("X", result.FileId);
            Assert.Equal("https://lh3.googleusercontent.com/d/X", result.Url);
            var sent = handler.Payloads[^1]; Assert.Equal("member_avatar_upload", sent.GetProperty("action").GetString());
            Assert.Equal(15, sent.GetProperty("memberId").GetInt32());
            Assert.Equal(id, sent.GetProperty("existingFileId").GetString());
            Assert.Equal(bytes, Convert.FromBase64String(sent.GetProperty("fileBase64").GetString()!));
            Assert.Equal(mime, sent.GetProperty("contentType").GetString());
            Assert.False(sent.TryGetProperty("email", out _)); Assert.False(sent.TryGetProperty("fileName", out _)); id = result.FileId;
        }
        Assert.True(await service.DeleteAvatarAsync(15, id));
        Assert.Equal("member_avatar_delete", handler.Payloads[^1].GetProperty("action").GetString());
        Assert.Equal("X", handler.Payloads[^1].GetProperty("fileId").GetString());
    }

    [Fact]
    public async Task Jpg_alias_and_whitespace_normalize_without_changing_storage_identity()
    {
        var handler = new RecordingHandler(); var result = await Create(handler).UploadAvatarAsync(28, [255, 216, 255], " IMAGE/JPG ", " X ");
        Assert.True(result.Success); Assert.Equal("X", result.FileId);
        Assert.Equal("image/jpeg", handler.Payloads[0].GetProperty("contentType").GetString());
    }

    [Theory]
    [InlineData("success")]
    [InlineData("fileId")]
    [InlineData("md5Checksum")]
    [InlineData("contentType")]
    [InlineData("fileSize")]
    public async Task Every_storage_confirmation_is_required_before_success(string field)
    {
        var handler = new RecordingHandler { RemoveField = field };
        var result = await Create(handler).UploadAvatarAsync(15, [255, 216, 255], "image/jpeg", "X");
        Assert.False(result.Success); Assert.Null(result.Url);
        Assert.Null(result.FileId);
    }

    [Theory]
    [InlineData("fileId", "different-id")]
    [InlineData("fileId", "../bad-id")]
    [InlineData("md5Checksum", "incorrect")]
    [InlineData("contentType", "image/png")]
    [InlineData("fileSize", "3")]
    public async Task Wrong_identity_or_media_cannot_be_reported_as_success(string field, string value)
    {
        var handler = new RecordingHandler { ReplaceField = field, ReplaceValue = value };
        var result = await Create(handler).UploadAvatarAsync(15, [255, 216, 255], "image/jpeg", "X");
        Assert.False(result.Success); Assert.Null(result.Url);
        Assert.Null(result.FileId);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"success\":false,\"fileId\":\"X\",\"cleanupSuccess\":false}")]
    public async Task Old_or_failed_deployments_do_not_return_a_successful_URL(string json)
    {
        var result = await Create(new RecordingHandler { ResponseJson = json }).UploadAvatarAsync(15, [255, 216, 255], "image/jpeg", null);
        Assert.False(result.Success); Assert.Null(result.Url);
        if (json.Contains("cleanupSuccess")) Assert.Equal("X", result.FileId);
    }

    [Fact]
    public async Task Invalid_member_image_or_file_id_does_not_call_storage()
    {
        var handler = new RecordingHandler(); var service = Create(handler);
        Assert.False((await service.UploadAvatarAsync(0, [255, 216, 255], "image/jpeg", null)).Success);
        Assert.False((await service.UploadAvatarAsync(15, [], "image/jpeg", null)).Success);
        Assert.False((await service.UploadAvatarAsync(15, new byte[5 * 1024 * 1024 + 1], "image/jpeg", null)).Success);
        Assert.False((await service.UploadAvatarAsync(15, [255, 216, 255], "image/png", null)).Success);
        Assert.False((await service.UploadAvatarAsync(15, [255, 216, 255], "image/svg+xml", null)).Success);
        Assert.False((await service.UploadAvatarAsync(15, [255, 216, 255], "image/jpeg", "../bad")).Success);
        Assert.False(await service.DeleteAvatarAsync(-1, "X")); Assert.False(await service.DeleteAvatarAsync(15, "bad/id"));
        Assert.Empty(handler.Payloads);
    }

    [Theory]
    [InlineData("{\"success\":false}")]
    [InlineData("{\"success\":true}")]
    [InlineData("{\"success\":true,\"deleted\":false}")]
    [InlineData("null")]
    public async Task Delete_with_X_requires_explicit_deleted_confirmation(string json) =>
        Assert.False(await Create(new RecordingHandler { ResponseJson = json }).DeleteAvatarAsync(15, "X"));

    [Fact]
    public async Task Legacy_delete_with_no_files_is_idempotent()
    {
        var handler = new RecordingHandler { ResponseJson = "{\"success\":true,\"deleted\":false}" };
        Assert.True(await Create(handler).DeleteAvatarAsync(15, null));
        Assert.Equal(JsonValueKind.Null, handler.Payloads[0].GetProperty("fileId").ValueKind);
    }

    [Fact]
    public async Task Transport_failure_and_missing_configuration_fail_safely()
    {
        foreach (var handler in new[] { new RecordingHandler { FailHttp = true }, new RecordingHandler { FailTransport = true } })
        {
            var service = Create(handler); Assert.False((await service.UploadAvatarAsync(15, [255, 216, 255], "image/jpeg", null)).Success);
            Assert.False(await service.DeleteAvatarAsync(15, "X"));
        }
        var missing = new RecordingHandler(); var unconfigured = Create(missing, "");
        Assert.False((await unconfigured.UploadAvatarAsync(15, [255, 216, 255], "image/jpeg", null)).Success);
        Assert.False(await unconfigured.DeleteAvatarAsync(15, "X")); Assert.Empty(missing.Payloads);
    }

    private static GoogleDriveMemberAvatarService Create(RecordingHandler handler, string url = "https://script.google.com/test") => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GoogleDrive:WebAppUrl"] = url }).Build(),
        new SingleClientFactory(new HttpClient(handler)), NullLogger<GoogleDriveMemberAvatarService>.Instance);

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal(nameof(GoogleDriveMemberAvatarService), name); return client; }
    }
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<JsonElement> Payloads { get; } = [];
        public string? ResponseJson { get; init; }
        public string? RemoveField { get; init; }
        public string? ReplaceField { get; init; }
        public string? ReplaceValue { get; init; }
        public bool FailHttp { get; init; }
        public bool FailTransport { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (FailTransport) throw new HttpRequestException("Network failure");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var sent = body.RootElement; Payloads.Add(sent.Clone());
            var response = new Dictionary<string, object?> { ["success"] = true, ["deleted"] = true };
            if (sent.GetProperty("action").GetString() == "member_avatar_upload")
            {
                var bytes = Convert.FromBase64String(sent.GetProperty("fileBase64").GetString()!);
                response["fileId"] = "X"; response["md5Checksum"] = Convert.ToHexString(MD5.HashData(bytes));
                response["contentType"] = sent.GetProperty("contentType").GetString(); response["fileSize"] = bytes.Length;
                response["url"] = "https://untrusted.example/ignored";
            }
            if (RemoveField is not null) response.Remove(RemoveField);
            if (ReplaceField is not null) response[ReplaceField] = ReplaceValue;
            return new HttpResponseMessage(FailHttp ? HttpStatusCode.BadGateway : HttpStatusCode.OK) {
                Content = new StringContent(ResponseJson ?? JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
}

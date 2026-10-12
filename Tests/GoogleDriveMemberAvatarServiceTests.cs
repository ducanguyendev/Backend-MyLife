using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
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
    public async Task Image_fetch_is_cached_and_returns_validated_image_bytes()
    {
        var handler = new ImageHandler(); var service = CreateImageService(handler);
        var first = await service.GetAvatarAsync(15, "X", DateTime.UnixEpoch);
        var second = await service.GetAvatarAsync(15, "X", DateTime.UnixEpoch);
        Assert.NotNull(first); Assert.Equal("image/jpeg", first.ContentType);
        Assert.Equal(new byte[] { 255, 216, 255, 0 }, first.Content);
        Assert.Equal(first.ETag, second!.ETag); Assert.Equal(1, handler.Calls);
        Assert.Equal($"https://lh3.googleusercontent.com/d/X?v={DateTime.UnixEpoch.Ticks}", handler.LastUrl);
    }

    [Fact]
    public async Task Replaced_image_revision_bypasses_upstream_cdn_and_server_cache()
    {
        var handler = new ImageHandler(); var service = CreateImageService(handler);
        var firstVersion = DateTime.UnixEpoch;
        var secondVersion = firstVersion.AddMilliseconds(1);
        var first = await service.GetAvatarAsync(15, "X", firstVersion);

        handler.Content = [255, 216, 255, 9];
        var replaced = await service.GetAvatarAsync(15, "X", secondVersion);
        var cachedReplacement = await service.GetAvatarAsync(15, "X", secondVersion);

        Assert.NotNull(first); Assert.NotNull(replaced);
        Assert.NotEqual(first.ETag, replaced.ETag);
        Assert.Equal(new byte[] { 255, 216, 255, 9 }, replaced.Content);
        Assert.Equal(replaced.ETag, cachedReplacement!.ETag);
        Assert.Equal(2, handler.Calls);
        Assert.Equal($"https://lh3.googleusercontent.com/d/X?v={firstVersion.Ticks}", handler.RequestedUrls[0]);
        Assert.Equal($"https://lh3.googleusercontent.com/d/X?v={secondVersion.Ticks}", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task Explicit_invalidation_allows_replaced_or_deleted_avatar_to_be_fetched_again()
    {
        var handler = new ImageHandler();
        var cache = new MemberAvatarImageCache(new MemoryCache(new MemoryCacheOptions()));
        var service = new GoogleDriveMemberAvatarService(new ConfigurationBuilder().Build(), new SingleClientFactory(new HttpClient(handler)),
            NullLogger<GoogleDriveMemberAvatarService>.Instance, cache);
        var version = DateTime.UnixEpoch;
        Assert.NotNull(await service.GetAvatarAsync(15, "X", version));
        cache.Invalidate(15, "X", version);
        Assert.NotNull(await service.GetAvatarAsync(15, "X", version));
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "image/jpeg", 4)]
    [InlineData(HttpStatusCode.OK, "text/html", 4)]
    [InlineData(HttpStatusCode.OK, "image/svg+xml", 4)]
    [InlineData(HttpStatusCode.OK, "image/jpeg", 5 * 1024 * 1024 + 1)]
    public async Task Invalid_or_failed_upstream_is_rejected_and_negative_cached(HttpStatusCode status, string contentType, int length)
    {
        var handler = new ImageHandler { Status = status, ContentType = contentType, Content = new byte[length] };
        handler.Content[0] = 255;
        var service = CreateImageService(handler);
        Assert.Null(await service.GetAvatarAsync(15, "X", DateTime.UnixEpoch));
        Assert.Null(await service.GetAvatarAsync(15, "X", DateTime.UnixEpoch));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Invalid_file_identity_does_not_request_upstream()
    {
        var handler = new ImageHandler(); var service = CreateImageService(handler);
        Assert.Null(await service.GetAvatarAsync(15, "../X", DateTime.UnixEpoch));
        Assert.Equal(0, handler.Calls);
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
        new SingleClientFactory(new HttpClient(handler)), NullLogger<GoogleDriveMemberAvatarService>.Instance,
        new MemberAvatarImageCache(new MemoryCache(new MemoryCacheOptions())));

    private static GoogleDriveMemberAvatarService CreateImageService(ImageHandler handler) => new(
        new ConfigurationBuilder().Build(), new SingleClientFactory(new HttpClient(handler)), NullLogger<GoogleDriveMemberAvatarService>.Instance,
        new MemberAvatarImageCache(new MemoryCache(new MemoryCacheOptions())));

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

    private sealed class ImageHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUrl { get; private set; }
        public List<string?> RequestedUrls { get; } = [];
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ContentType { get; init; } = "image/jpeg";
        public byte[] Content { get; set; } = [255, 216, 255, 0];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; LastUrl = request.RequestUri?.ToString();
            RequestedUrls.Add(LastUrl);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Content) {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType) } } });
        }
    }
}

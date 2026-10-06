using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyLife.Features.Library.Services;
using Xunit;

public sealed class GoogleDriveLibraryStorageServiceTests
{
    private static readonly byte[] Jpeg = [255, 216, 255, 128, 254, 1];

    [Fact]
    public async Task Create_folder_sends_exact_album_identity_and_upload_uses_safe_unique_names_with_verified_media()
    {
        var handler = new RecordingHandler();
        var service = Service(handler);
        var folder = await service.CreateAlbumFolderAsync(long.MaxValue, default);
        Assert.True(folder.Success);
        Assert.Equal(long.MaxValue.ToString(), handler.Payloads[0].GetProperty("albumId").GetString());
        var first = await service.UploadPhotoAsync(folder.FolderId!, Jpeg, "image/jpg", "../unsafe.jpg", default);
        var second = await service.UploadPhotoAsync(folder.FolderId!, Jpeg, "image/jpeg", "../unsafe.jpg", default);
        Assert.True(first.Success);
        Assert.True(second.Success);
        foreach (var payload in handler.Payloads.Skip(1))
        {
            Assert.Equal("library_upload_photo", payload.GetProperty("action").GetString());
            Assert.Equal(folder.FolderId, payload.GetProperty("folderId").GetString());
            Assert.Matches("^photo_[a-f0-9]{32}\\.jpg$", payload.GetProperty("fileName").GetString());
            Assert.Equal("image/jpeg", payload.GetProperty("contentType").GetString());
            Assert.Equal(Jpeg, Convert.FromBase64String(payload.GetProperty("fileBase64").GetString()!));
        }
        Assert.NotEqual(handler.Payloads[1].GetProperty("fileName").GetString(), handler.Payloads[2].GetProperty("fileName").GetString());
        Assert.Equal($"https://lh3.googleusercontent.com/d/{first.FileId}", first.Url);
        Assert.Equal(Jpeg.LongLength, first.FileSize);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("mime")]
    [InlineData("size")]
    [InlineData("missing-proof")]
    [InlineData("false-success")]
    [InlineData("changed-id")]
    public async Task Upload_requires_confirmed_bytes_mime_size_and_valid_id(string corruption)
    {
        var handler = new RecordingHandler { Corruption = corruption };
        var result = await Service(handler).UploadPhotoAsync("folder-1", Jpeg, "image/jpeg", "a.jpg", default);
        Assert.False(result.Success);
        Assert.Null(result.Url);
        if (corruption != "changed-id") Assert.Equal("file-1", result.FileId); // compensation identity retained
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"success\":true}")]
    [InlineData("{\"success\":true,\"folderId\":\"folder-1\",\"folderName\":\"wrong\"}")]
    public async Task Malformed_or_wrong_folder_responses_are_failure(string response)
    {
        var handler = new RecordingHandler { Response = response };
        Assert.False((await Service(handler).CreateAlbumFolderAsync(15, default)).Success);
    }

    [Fact]
    public async Task Delete_sends_album_and_file_ids_and_requires_explicit_deleted_confirmation()
    {
        var handler = new RecordingHandler();
        var service = Service(handler);
        Assert.True(await service.DeletePhotoAsync("folder-1", "file-1", default));
        Assert.True(await service.DeleteAlbumFolderAsync("folder-1", default));
        Assert.Equal("library_delete_photo", handler.Payloads[0].GetProperty("action").GetString());
        Assert.Equal("folder-1", handler.Payloads[0].GetProperty("folderId").GetString());
        Assert.Equal("file-1", handler.Payloads[0].GetProperty("fileId").GetString());
        Assert.Equal("library_delete_album", handler.Payloads[1].GetProperty("action").GetString());
        var rejected = Service(new RecordingHandler { Response = "{\"success\":true,\"deleted\":false}" });
        Assert.False(await rejected.DeletePhotoAsync("folder-1", "file-1", default));
        Assert.False(await rejected.DeleteAlbumFolderAsync("folder-1", default));
    }

    private static GoogleDriveLibraryStorageService Service(HttpMessageHandler handler) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["GoogleDrive:WebAppUrl"] = "https://script.google.com/test" }).Build(),
        new ClientFactory(new HttpClient(handler)), NullLogger<GoogleDriveLibraryStorageService>.Instance);
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<JsonElement> Payloads { get; } = [];
        public string? Response { get; init; }
        public string? Corruption { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var payload = json.RootElement.Clone();
            Payloads.Add(payload);
            var response = Response;
            if (response is null)
            {
                var action = payload.GetProperty("action").GetString();
                if (action == "library_create_album_folder") response = JsonSerializer.Serialize(new {
                    success = true, folderId = "folder-1", folderName = $"album_{payload.GetProperty("albumId").GetString()}" });
                else if (action == "library_upload_photo")
                {
                    var bytes = Convert.FromBase64String(payload.GetProperty("fileBase64").GetString()!);
                    response = Corruption == "missing-proof" ? "{\"success\":true,\"fileId\":\"file-1\"}" : JsonSerializer.Serialize(new {
                        success = Corruption != "false-success", fileId = Corruption == "changed-id" ? "invalid/id" : "file-1",
                        md5Checksum = Corruption == "checksum" ? "wrong" : Convert.ToHexString(MD5.HashData(bytes)),
                        contentType = Corruption == "mime" ? "image/png" : payload.GetProperty("contentType").GetString(),
                        fileSize = Corruption == "size" ? bytes.Length + 1 : bytes.Length,
                        url = "https://untrusted.example/path?t=123"
                    });
                }
                else response = "{\"success\":true,\"deleted\":true}";
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

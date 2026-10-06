using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MyLife.Features.Library.Services;
using MyLife.Features.Library.DTOs;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;
using Xunit;

public sealed class LibraryFlowsTests(MyLifeFactory factory) : IClassFixture<MyLifeFactory>
{
    private readonly HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    private readonly TestLibraryStorage storage = factory.Services.GetRequiredService<TestLibraryStorage>();
    private static readonly byte[] Jpeg = Fixture(0);
    private static readonly byte[] Png = Fixture(2);
    private static readonly byte[] Webp = Fixture(1);

    [Fact]
    public async Task Batch_metadata_and_photo_edit_round_trip_without_touching_drive_identity_or_bytes()
    {
        var owner = await User();
        var other = await User();
        var id = (await Create(owner.Token)).GetProperty("id").GetInt64();
        var metadata = new LibraryPhotoMetadataDto { Title = "  Họp mặt 2026  ", Category = "events",
            DisplayDate = "Xuân 2026", Description = "Ảnh đại gia đình", Author = "Ban liên lạc" };
        using var upload = await Upload(owner.Token, id, [("a.jpg", "image/jpeg", Jpeg), ("b.png", "image/png", Png)], metadata);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var photos = await upload.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var photo in photos.EnumerateArray())
        {
            Assert.Equal("Họp mặt 2026", photo.GetProperty("title").GetString());
            Assert.Equal("events", photo.GetProperty("category").GetString());
            Assert.Equal("Xuân 2026", photo.GetProperty("displayDate").GetString());
            Assert.Equal("Ảnh đại gia đình", photo.GetProperty("description").GetString());
            Assert.Equal(photo.GetProperty("caption").GetString(), photo.GetProperty("description").GetString());
            Assert.Equal("Ban liên lạc", photo.GetProperty("author").GetString());
        }
        var photoId = photos[0].GetProperty("id").GetInt64();
        var calls = storage.UploadCount;
        string fileId;
        await using (var scope = factory.Services.CreateAsyncScope())
            fileId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryPhotos.SingleAsync(p => p.Id == photoId)).DriveFileId;
        var edit = new { title = "Từ đường", category = "temple", displayDate = "1820", description = "Mô tả mới", author = "Nguồn gia đình" };
        using var denied = await Send(other.Token, HttpMethod.Put, $"/api/library/photos/{photoId}", edit);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var invalid = await Send(owner.Token, HttpMethod.Put, $"/api/library/photos/{photoId}", new { category = "all" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var update = await Send(owner.Token, HttpMethod.Put, $"/api/library/photos/{photoId}", edit);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var detail = await Send(owner.Token, HttpMethod.Get, $"/api/library/albums/{id}");
        var roundTrip = (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photos")[0];
        Assert.Equal("Từ đường", roundTrip.GetProperty("title").GetString());
        Assert.Equal("temple", roundTrip.GetProperty("category").GetString());
        Assert.Equal("Mô tả mới", roundTrip.GetProperty("description").GetString());
        Assert.Equal(calls, storage.UploadCount);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(fileId, (await db.LibraryPhotos.SingleAsync(p => p.Id == photoId)).DriveFileId);
            Assert.Equal(Jpeg, storage.Files[fileId].Bytes);
            Assert.Contains("20261006063103_ExtendLibraryPhotoMetadata", await db.Database.GetAppliedMigrationsAsync());
        }
    }

    [Fact]
    public async Task Database_failure_after_drive_success_compensates_files_and_initial_album_row()
    {
        var user = await User();
        var failures = factory.Services.GetRequiredService<LibraryPersistenceFailureInterceptor>();
        failures.FailFinalAlbumSave = true;
        using var failedCreate = await Send(user.Token, HttpMethod.Post, "/api/library/albums", new { name = "DB failure" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedCreate.StatusCode);
        Assert.True(storage.Folders[storage.LastCreatedFolder!].Trashed);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.LibraryAlbums.AnyAsync(a => a.CreatedByUserId == user.Id));
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains("20261006042455_AddLibraryModule", await db.Database.GetAppliedMigrationsAsync());
        }
        var id = (await Create(user.Token)).GetProperty("id").GetInt64();
        failures.FailPhotoSave = true;
        using var failedUpload = await Upload(user.Token, id, [("a.jpg", "image/jpeg", Jpeg), ("b.png", "image/png", Png)]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedUpload.StatusCode);
        Assert.Equal(2, storage.Files.Values.Count(p => p.FolderId == $"folder-album-{id}" && p.Trashed));
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryPhotos.AnyAsync(p => p.AlbumId == id));
    }

    [Fact]
    public async Task Create_list_detail_update_and_single_plus_batch_upload_persist_correct_metadata()
    {
        var user = await User();
        var album = await Create(user.Token, " Gia đình 2026 ");
        Assert.Equal("Gia đình 2026", album.GetProperty("name").GetString());
        var id = album.GetProperty("id").GetInt64();
        Assert.Equal(0, album.GetProperty("photoCount").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.LibraryAlbums.SingleAsync(a => a.Id == id);
            Assert.Equal(user.Id, row.CreatedByUserId);
            Assert.Equal($"folder-album-{id}", row.DriveFolderId);
            Assert.Null(row.CoverPhotoId);
        }
        using var single = await Upload(user.Token, id, [("IMG_0001.jpg", "image/jpeg", Jpeg)]);
        Assert.Equal(HttpStatusCode.Created, single.StatusCode);
        using var batch = await Upload(user.Token, id, [("IMG_0001.jpg", "image/jpeg", Jpeg),
            ("ảnh.png", "image/png", Png), ("image.webp", "image/webp", Webp)]);
        Assert.Equal(HttpStatusCode.Created, batch.StatusCode);
        var photos = await batch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, photos.GetArrayLength());
        Assert.Equal("IMG_0001.jpg", photos[0].GetProperty("fileName").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.LibraryPhotos.Where(p => p.AlbumId == id).ToListAsync();
            Assert.Equal(4, rows.Count);
            Assert.Equal(4, rows.Select(p => p.DriveFileId).Distinct().Count());
            foreach (var photo in rows)
            {
                Assert.Equal($"https://lh3.googleusercontent.com/d/{photo.DriveFileId}", photo.Url);
                Assert.DoesNotContain("?", photo.Url);
                Assert.Equal(photo.FileSize, storage.Files[photo.DriveFileId].Bytes.LongLength);
                Assert.Equal(photo.ContentType, storage.Files[photo.DriveFileId].Mime);
            }
            // Cover schema references an existing photo; no cover Drive file.
            var row = await db.LibraryAlbums.SingleAsync(a => a.Id == id);
            row.CoverPhotoId = rows[1].Id;
            rows[1].SortOrder = -1;
            await db.SaveChangesAsync();
        }
        using var detail = await Send(user.Token, HttpMethod.Get, $"/api/library/albums/{id}");
        var data = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, data.GetProperty("photoCount").GetInt32());
        Assert.Equal(-1, data.GetProperty("photos")[0].GetProperty("sortOrder").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, data.GetProperty("coverPhotoUrl").ValueKind);
        using var update = await Send(user.Token, HttpMethod.Put, $"/api/library/albums/{id}", new { name = "Renamed", description = "updated" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal($"album_{id}", storage.Folders[$"folder-album-{id}"].Name);
        using var list = await Send(user.Token, HttpMethod.Get, "/api/library/albums");
        var listed = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(listed.EnumerateArray());
        Assert.Equal("Renamed", listed[0].GetProperty("name").GetString());
        Assert.Equal(4, listed[0].GetProperty("photoCount").GetInt32());
    }

    [Fact]
    public async Task Create_storage_failure_rolls_back_the_album_and_cleans_known_folder()
    {
        var user = await User();
        storage.FailCreate = true;
        using var failed = await Send(user.Token, HttpMethod.Post, "/api/library/albums", new { name = "failed" });
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryAlbums.AnyAsync(a => a.CreatedByUserId == user.Id));
        Assert.True(storage.Folders[storage.LastCreatedFolder!].Trashed);
        storage.FailCreate = false;
    }

    [Fact]
    public async Task Mid_batch_failure_removes_only_new_files_and_keeps_existing_photos()
    {
        var user = await User();
        var id = (await Create(user.Token)).GetProperty("id").GetInt64();
        using var initial = await Upload(user.Token, id, [("old.jpg", "image/jpeg", Jpeg)]);
        var oldPhoto = (await initial.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetInt64();
        storage.FailOnUpload = storage.UploadCount + 3;
        using var batch = await Upload(user.Token, id, [("a.jpg", "image/jpeg", Jpeg), ("b.png", "image/png", Png), ("c.webp", "image/webp", Webp)]);
        Assert.Equal(HttpStatusCode.BadGateway, batch.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var kept = await db.LibraryPhotos.Where(p => p.AlbumId == id).ToListAsync();
        Assert.Single(kept);
        Assert.Equal(oldPhoto, kept[0].Id);
        Assert.Single(storage.Files.Values, f => f.FolderId == $"folder-album-{id}" && !f.Trashed);
        Assert.Equal(2, storage.Files.Values.Count(f => f.FolderId == $"folder-album-{id}" && f.Trashed));
    }

    [Fact]
    public async Task Failed_upload_with_returned_file_id_is_also_compensated_and_cleanup_failure_remains_failure()
    {
        var user = await User();
        var id = (await Create(user.Token)).GetProperty("id").GetInt64();
        storage.FailOnUpload = storage.UploadCount + 2;
        storage.ReturnFailedFileId = true;
        storage.FailDeletePhoto = true;
        using var batch = await Upload(user.Token, id, [("a.jpg", "image/jpeg", Jpeg), ("b.png", "image/png", Png)]);
        Assert.Equal(HttpStatusCode.BadGateway, batch.StatusCode);
        var body = await batch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("cleanup is incomplete", body.GetProperty("message").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryPhotos.AnyAsync(p => p.AlbumId == id));
        Assert.Equal(2, storage.DeletePhotoCalls.Count(call => call.Folder == $"folder-album-{id}"));
        storage.FailDeletePhoto = false;
        storage.ReturnFailedFileId = false;
    }

    [Fact]
    public async Task Photo_and_album_deletion_keep_db_on_storage_failure_then_delete_cover_and_cascade_on_success()
    {
        var user = await User();
        var id = (await Create(user.Token)).GetProperty("id").GetInt64();
        using var uploaded = await Upload(user.Token, id, [("a.jpg", "image/jpeg", Jpeg), ("b.png", "image/png", Png)]);
        var photoId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetInt64();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.LibraryAlbums.SingleAsync(a => a.Id == id)).CoverPhotoId = photoId;
            await db.SaveChangesAsync();
        }
        storage.FailDeletePhoto = true;
        using var failPhoto = await Send(user.Token, HttpMethod.Delete, $"/api/library/photos/{photoId}");
        Assert.Equal(HttpStatusCode.BadGateway, failPhoto.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.LibraryPhotos.AnyAsync(p => p.Id == photoId));
            Assert.Equal(photoId, (await db.LibraryAlbums.SingleAsync(a => a.Id == id)).CoverPhotoId);
        }
        storage.FailDeletePhoto = false;
        using var deletePhoto = await Send(user.Token, HttpMethod.Delete, $"/api/library/photos/{photoId}");
        Assert.Equal(HttpStatusCode.NoContent, deletePhoto.StatusCode);
        storage.FailDeleteAlbum = true;
        using var failAlbum = await Send(user.Token, HttpMethod.Delete, $"/api/library/albums/{id}");
        Assert.Equal(HttpStatusCode.BadGateway, failAlbum.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null((await db.LibraryAlbums.SingleAsync(a => a.Id == id)).CoverPhotoId);
            Assert.Single(await db.LibraryPhotos.Where(p => p.AlbumId == id).ToListAsync());
        }
        storage.FailDeleteAlbum = false;
        using var deleteAlbum = await Send(user.Token, HttpMethod.Delete, $"/api/library/albums/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteAlbum.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.LibraryAlbums.AnyAsync(a => a.Id == id));
            Assert.False(await db.LibraryPhotos.AnyAsync(p => p.AlbumId == id));
        }
        Assert.True(storage.Folders[$"folder-album-{id}"].Trashed);
    }

    [Fact]
    public async Task Ownership_applies_to_all_routes_and_unauthenticated_requests_are_rejected()
    {
        var owner = await User();
        var other = await User();
        var id = (await Create(owner.Token)).GetProperty("id").GetInt64();
        using var uploaded = await Upload(owner.Token, id, [("a.jpg", "image/jpeg", Jpeg)]);
        var photoId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetInt64();
        using var get = await Send(other.Token, HttpMethod.Get, $"/api/library/albums/{id}");
        using var update = await Send(other.Token, HttpMethod.Put, $"/api/library/albums/{id}", new { name = "stolen" });
        using var delete = await Send(other.Token, HttpMethod.Delete, $"/api/library/albums/{id}");
        using var deletePhoto = await Send(other.Token, HttpMethod.Delete, $"/api/library/photos/{photoId}");
        using var upload = await Upload(other.Token, id, [("a.jpg", "image/jpeg", Jpeg)]);
        foreach (var response in new[] { get, update, delete, deletePhoto, upload }) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var list = await Send(other.Token, HttpMethod.Get, "/api/library/albums");
        Assert.Equal(0, (await list.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/library/albums")).StatusCode);
    }

    [Fact]
    public async Task Unsafe_invalid_and_unbounded_uploads_are_rejected_without_storage_success()
    {
        var user = await User();
        var id = (await Create(user.Token)).GetProperty("id").GetInt64();
        var start = storage.UploadCount;
        var invalid = new (string, string, byte[])[][] {
            [], Enumerable.Repeat(("a.jpg", "image/jpeg", Jpeg), 21).ToArray(),
            [("../a.jpg", "image/jpeg", Jpeg)], [("a.jpg", "image/png", Png)],
            [("a.svg", "image/svg+xml", Png)], [("a.jpg", "image/jpeg", "<html>bad</html>"u8.ToArray())],
            [("big.jpg", "image/jpeg", new byte[5 * 1024 * 1024 + 1])],
        };
        foreach (var files in invalid)
        {
            using var response = await Upload(user.Token, id, files);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(start, storage.UploadCount);
        using var blank = await Send(user.Token, HttpMethod.Post, "/api/library/albums", new { name = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task Concurrent_album_delete_waits_for_upload_then_removes_its_photos()
    {
        var user = await User();
        var id = (await Create(user.Token)).GetProperty("id").GetInt64();
        storage.UploadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.UploadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var upload = Upload(user.Token, id, [("a.jpg", "image/jpeg", Jpeg)]);
        await storage.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var deletion = Send(user.Token, HttpMethod.Delete, $"/api/library/albums/{id}");
        storage.UploadGate.SetResult();
        using var uploaded = await upload;
        using var deleted = await deletion;
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        storage.UploadGate = null;
        storage.UploadEntered = null;
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryPhotos.AnyAsync(p => p.AlbumId == id));
        Assert.True(storage.Folders[$"folder-album-{id}"].Trashed);
    }

    private async Task<(string Token, int Id)> User()
    {
        var email = $"library.{Guid.NewGuid():N}@gmail.com";
        const string password = "User_Test!234567";
        var registered = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Library Test", phoneNumber = "0912345678", gender = "Nam",
            dateOfBirth = "1995-05-20", email, password, confirmPassword = password });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/login") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-Client-Platform", "mobile");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("user").GetProperty("id").GetInt32());
    }
    private async Task<JsonElement> Create(string token, string name = "Album")
    {
        using var response = await Send(token, HttpMethod.Post, "/api/library/albums", new { name, description = "Description" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<HttpResponseMessage> Send(string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
    private async Task<HttpResponseMessage> Upload(string token, long albumId, (string Name, string Mime, byte[] Bytes)[] files, LibraryPhotoMetadataDto? metadata = null)
    {
        using var form = new MultipartFormDataContent();
        if (metadata is not null)
        {
            form.Add(new StringContent(metadata.Title ?? ""), "title");
            form.Add(new StringContent(metadata.Category), "category");
            form.Add(new StringContent(metadata.DisplayDate ?? ""), "displayDate");
            form.Add(new StringContent(metadata.Description ?? ""), "description");
            form.Add(new StringContent(metadata.Author ?? ""), "author");
        }
        foreach (var file in files)
        {
            var content = new ByteArrayContent(file.Bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(file.Mime);
            form.Add(content, "files", file.Name);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/library/albums/{albumId}/photos") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
    private static byte[] Fixture(int index)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "avatar-replacements.json")));
        return Convert.FromBase64String(json.RootElement[index].GetProperty("base64").GetString()!);
    }
}

public sealed class TestLibraryStorage : ILibraryStorageService
{
    private readonly string runId = Guid.NewGuid().ToString("N");
    public sealed record Folder(string Name) { public bool Trashed { get; set; } }
    public sealed record Photo(string FolderId, byte[] Bytes, string Mime) { public bool Trashed { get; set; } }
    public ConcurrentDictionary<string, Folder> Folders { get; } = [];
    public ConcurrentDictionary<string, Photo> Files { get; } = [];
    public ConcurrentQueue<(string Folder, string File)> DeletePhotoCalls { get; } = [];
    public int UploadCount { get; private set; }
    public int FailOnUpload { get; set; } = -1;
    public bool ReturnFailedFileId { get; set; }
    public bool FailCreate { get; set; }
    public bool FailDeletePhoto { get; set; }
    public bool FailDeleteAlbum { get; set; }
    public string? LastCreatedFolder { get; private set; }
    public TaskCompletionSource? UploadGate { get; set; }
    public TaskCompletionSource? UploadEntered { get; set; }
    public Task<LibraryFolderResult> CreateAlbumFolderAsync(long albumId, CancellationToken cancellationToken)
    {
        LastCreatedFolder = $"folder-album-{albumId}";
        Folders.TryAdd(LastCreatedFolder, new($"album_{albumId}"));
        return Task.FromResult(new LibraryFolderResult(!FailCreate, LastCreatedFolder));
    }
    public async Task<LibraryFileResult> UploadPhotoAsync(string albumDriveFolderId, byte[] content, string contentType, string originalFileName, CancellationToken cancellationToken)
    {
        UploadCount++;
        var id = $"library-file-{runId}-{UploadCount}";
        if (UploadCount == FailOnUpload && !ReturnFailedFileId) return new(false, null, null, null, 0);
        Files[id] = new(albumDriveFolderId, content.ToArray(), contentType);
        UploadEntered?.TrySetResult();
        if (UploadGate is not null) await UploadGate.Task.WaitAsync(cancellationToken);
        return new(UploadCount != FailOnUpload, id, $"https://lh3.googleusercontent.com/d/{id}", contentType, content.LongLength);
    }
    public Task<bool> DeletePhotoAsync(string albumDriveFolderId, string driveFileId, CancellationToken cancellationToken)
    {
        DeletePhotoCalls.Enqueue((albumDriveFolderId, driveFileId));
        if (FailDeletePhoto) return Task.FromResult(false);
        if (Files.TryGetValue(driveFileId, out var photo)) photo.Trashed = true;
        return Task.FromResult(true);
    }
    public Task<bool> DeleteAlbumFolderAsync(string driveFolderId, CancellationToken cancellationToken)
    {
        if (FailDeleteAlbum) return Task.FromResult(false);
        if (Folders.TryGetValue(driveFolderId, out var folder)) folder.Trashed = true;
        foreach (var photo in Files.Values.Where(p => p.FolderId == driveFolderId)) photo.Trashed = true;
        return Task.FromResult(true);
    }
}

public sealed class LibraryPersistenceFailureInterceptor : SaveChangesInterceptor
{
    public bool FailFinalAlbumSave { get; set; }
    public bool FailPhotoSave { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var db = eventData.Context!;
        if (FailFinalAlbumSave && db.ChangeTracker.Entries<LibraryAlbum>().Any(e => e.State == EntityState.Modified && e.Entity.DriveFolderId is not null))
        {
            FailFinalAlbumSave = false;
            throw new DbUpdateException("Test-only Library album save failure.");
        }
        if (FailPhotoSave && db.ChangeTracker.Entries<LibraryPhoto>().Any(e => e.State == EntityState.Added))
        {
            FailPhotoSave = false;
            throw new DbUpdateException("Test-only Library photo save failure.");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

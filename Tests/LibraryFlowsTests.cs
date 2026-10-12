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
    public async Task Categories_are_user_created_owned_dynamic_and_legacy_photos_do_not_need_registry_rows()
    {
        var owner = await User(); var other = await User();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.LibraryCategories.AddRange(
                new LibraryCategory { CreatedByUserId = owner.Id, Name = "Historical Photos", Slug = "photos", IsDefault = true, CreatedAt = now, UpdatedAt = now },
                new LibraryCategory { CreatedByUserId = owner.Id, Name = "Decrees", Slug = "decrees", IsDefault = true, CreatedAt = now, UpdatedAt = now },
                new LibraryCategory { CreatedByUserId = owner.Id, Name = "Gatherings", Slug = "events", IsDefault = true, CreatedAt = now, UpdatedAt = now },
                new LibraryCategory { CreatedByUserId = owner.Id, Name = "Temple", Slug = "temple", IsDefault = true, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        async Task<JsonElement> Categories(string token) {
            using var response = await Send(token, HttpMethod.Get, "/api/library/categories");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        async Task<JsonElement> Category(string name, string token) {
            using var response = await Send(token, HttpMethod.Post, "/api/library/categories", new { name, slug = "injected", isDefault = true, createdByUserId = other.Id });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        Assert.Empty((await Categories(owner.Token)).EnumerateArray());
        Assert.Empty((await Categories(owner.Token)).EnumerateArray());
        Assert.Empty((await Categories(other.Token)).EnumerateArray());
        List<long> legacyDefaults;
        await using (var scope = factory.Services.CreateAsyncScope())
            legacyDefaults = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .LibraryCategories.AsNoTracking().Where(c => c.CreatedByUserId == owner.Id && c.IsDefault).Select(c => c.Id).ToListAsync();
        Assert.Equal(4, legacyDefaults.Count);

        var emptyCategoryAlbum = (await Create(owner.Token, "Zero categories")).GetProperty("id").GetInt64();
        var storageUploadsBeforeEmptyCategory = storage.UploadCount;
        using (var blockedUpload = await Upload(owner.Token, emptyCategoryAlbum, [("blocked.jpg", "image/jpeg", Jpeg)],
            new LibraryPhotoMetadataDto { Category = string.Empty }))
            Assert.Equal(HttpStatusCode.BadRequest, blockedUpload.StatusCode);
        Assert.Equal(storageUploadsBeforeEmptyCategory, storage.UploadCount);

        var animation = await Category("Hoạt hình", owner.Token);
        Assert.Equal("hoat-hinh", animation.GetProperty("slug").GetString());
        Assert.False(animation.GetProperty("isDefault").GetBoolean());
        using (var reservedCategory = await Send(owner.Token, HttpMethod.Post, "/api/library/categories", new { name = "all" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, reservedCategory.StatusCode);
            Assert.Equal("LIBRARY_CATEGORY_INVALID", (await reservedCategory.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var animationAlbumId = (await Create(owner.Token, "Animation")).GetProperty("id").GetInt64();
        using (var animationUpload = await Upload(owner.Token, animationAlbumId, [("doraemon.jpg", "image/jpeg", Jpeg)],
            new LibraryPhotoMetadataDto { Category = animation.GetProperty("slug").GetString()! }))
        {
            Assert.Equal(HttpStatusCode.Created, animationUpload.StatusCode);
            Assert.Equal("hoat-hinh", (await animationUpload.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("category").GetString());
        }
        var custom = await Category(" Du lịch ", owner.Token);
        Assert.Equal("Du lịch", custom.GetProperty("name").GetString()); Assert.Equal("du-lich", custom.GetProperty("slug").GetString());
        Assert.False(custom.GetProperty("isDefault").GetBoolean());
        var initialUserCategories = await Categories(owner.Token);
        Assert.Equal(new[] { "hoat-hinh", "du-lich" }, initialUserCategories.EnumerateArray().Select(item => item.GetProperty("slug").GetString()).ToArray());
        Assert.Equal("du-lich-2", (await Category("Du lich", owner.Token)).GetProperty("slug").GetString());
        var memorial = await Category("Đám giỗ", owner.Token);
        Assert.Equal("dam-gio", memorial.GetProperty("slug").GetString());
        foreach (var name in new[] { "", "   ", new string('a', 101) }) {
            using var invalid = await Send(owner.Token, HttpMethod.Post, "/api/library/categories", new { name });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("LIBRARY_CATEGORY_INVALID", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var id = custom.GetProperty("id").GetInt64();
        using var denied = await Send(other.Token, HttpMethod.Delete, $"/api/library/categories/{id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("LIBRARY_CATEGORY_NOT_FOUND", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        foreach (var legacyId in legacyDefaults)
        {
            using var defaultDelete = await Send(owner.Token, HttpMethod.Delete, $"/api/library/categories/{legacyId}");
            Assert.Equal(HttpStatusCode.Conflict, defaultDelete.StatusCode);
            Assert.Equal("LIBRARY_CATEGORY_CANNOT_DELETE", (await defaultDelete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            using var defaultEdit = await Send(owner.Token, HttpMethod.Put, $"/api/library/categories/{legacyId}", new { name = "Default rename" });
            Assert.Equal(HttpStatusCode.Conflict, defaultEdit.StatusCode);
            Assert.Equal("LIBRARY_CATEGORY_CANNOT_EDIT", (await defaultEdit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        foreach (var name in new string?[] { null, "", "   ", new string('a', 101) }) {
            using var invalid = await Send(owner.Token, HttpMethod.Put, $"/api/library/categories/{id}", new { name });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("LIBRARY_CATEGORY_INVALID", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using var foreignEdit = await Send(other.Token, HttpMethod.Put, $"/api/library/categories/{id}", new { name = "Foreign rename" });
        Assert.Equal(HttpStatusCode.NotFound, foreignEdit.StatusCode);
        Assert.Equal("LIBRARY_CATEGORY_NOT_FOUND", (await foreignEdit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var albumId = (await Create(owner.Token)).GetProperty("id").GetInt64();
        var metadata = new LibraryPhotoMetadataDto { Category = "du-lich" };
        using var uploaded = await Upload(owner.Token, albumId, [("a.jpg", "image/jpeg", Jpeg)], metadata);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var photo = (await uploaded.Content.ReadFromJsonAsync<JsonElement>())[0];
        Assert.False(string.IsNullOrEmpty(photo.GetProperty("driveFileId").GetString()));
        using var photoBeforeRename = await Send(owner.Token, HttpMethod.Get, $"/api/library/albums/{albumId}");
        var persistedPhoto = (await photoBeforeRename.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photos")[0];
        var uploadCountBeforeRename = storage.UploadCount;
        var deleteCountBeforeRename = storage.DeletePhotoCalls.Count;
        LibraryCategory before;
        await using (var scope = factory.Services.CreateAsyncScope())
            before = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryCategories.AsNoTracking().SingleAsync(c => c.Id == id);
        using var rename = await Send(owner.Token, HttpMethod.Put, $"/api/library/categories/{id}", new {
            name = " Du lịch gia đình ", slug = "injected", isDefault = true, createdByUserId = other.Id, id = -1 });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var renamed = await rename.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Du lịch gia đình", renamed.GetProperty("name").GetString());
        Assert.Equal("du-lich", renamed.GetProperty("slug").GetString());
        Assert.Equal(id, renamed.GetProperty("id").GetInt64());
        Assert.False(renamed.GetProperty("isDefault").GetBoolean());
        var reloaded = (await Categories(owner.Token)).EnumerateArray().Single(c => c.GetProperty("id").GetInt64() == id);
        Assert.Equal(renamed.ToString(), reloaded.ToString());
        using var photoReload = await Send(owner.Token, HttpMethod.Get, $"/api/library/albums/{albumId}");
        Assert.Equal(persistedPhoto.ToString(), (await photoReload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photos")[0].ToString());
        Assert.Equal(uploadCountBeforeRename, storage.UploadCount); Assert.Equal(deleteCountBeforeRename, storage.DeletePhotoCalls.Count);
        await using (var scope = factory.Services.CreateAsyncScope()) {
            var after = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryCategories.AsNoTracking().SingleAsync(c => c.Id == id);
            Assert.Equal(before.CreatedAt, after.CreatedAt); Assert.Equal(before.CreatedByUserId, after.CreatedByUserId);
            Assert.Equal(before.Slug, after.Slug); Assert.True(after.UpdatedAt > before.UpdatedAt);
        }
        using var inUse = await Send(owner.Token, HttpMethod.Delete, $"/api/library/categories/{id}");
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal("LIBRARY_CATEGORY_IN_USE", (await inUse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var edit = await Send(owner.Token, HttpMethod.Put, $"/api/library/photos/{photo.GetProperty("id").GetInt64()}", new { category = memorial.GetProperty("slug").GetString() });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var remove = await Send(owner.Token, HttpMethod.Delete, $"/api/library/categories/{id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var photoId = photo.GetProperty("id").GetInt64();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.LibraryPhotos.SingleAsync(p => p.Id == photoId);
            stored.Category = "photos";
            await db.SaveChangesAsync();
        }
        using (var legacyRead = await Send(owner.Token, HttpMethod.Get, $"/api/library/albums/{albumId}"))
        {
            Assert.Equal(HttpStatusCode.OK, legacyRead.StatusCode);
            var legacyPhoto = (await legacyRead.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photos")[0];
            Assert.Equal("photos", legacyPhoto.GetProperty("category").GetString());
            Assert.Equal(photo.GetProperty("driveFileId").GetString(), legacyPhoto.GetProperty("driveFileId").GetString());
        }
        var onlyOther = await Category("Chỉ của user khác", other.Token);
        foreach (var slug in new[] { "unknown", "all", "photos", "decrees", "events", "temple", onlyOther.GetProperty("slug").GetString()! }) {
            using var invalid = await Upload(owner.Token, albumId, [("a.jpg", "image/jpeg", Jpeg)], new LibraryPhotoMetadataDto { Category = slug });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var invalidEdit = await Send(owner.Token, HttpMethod.Put, $"/api/library/photos/{photo.GetProperty("id").GetInt64()}", new { category = slug });
            Assert.Equal(HttpStatusCode.BadRequest, invalidEdit.StatusCode);
        }
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(4, await db.LibraryCategories.CountAsync(c => c.CreatedByUserId == owner.Id && c.IsDefault));
            Assert.Equal(3, await db.LibraryCategories.CountAsync(c => c.CreatedByUserId == owner.Id && !c.IsDefault));
            Assert.True(await db.LibraryCategories.AnyAsync(c => c.CreatedByUserId == owner.Id && c.Name == "Hoạt hình" && !c.IsDefault));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/library/categories")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_empty_category_lists_and_custom_slug_collisions_are_safe()
    {
        var user = await User();
        var initial = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Send(user.Token, HttpMethod.Get, "/api/library/categories")));
        foreach (var response in initial) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()); response.Dispose(); }
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Send(user.Token, HttpMethod.Post, "/api/library/categories", new { name = "Du lịch" })));
        var slugs = new HashSet<string>();
        foreach (var response in results) { Assert.Equal(HttpStatusCode.Created, response.StatusCode); slugs.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!); response.Dispose(); }
        Assert.Equal(6, slugs.Count); Assert.Contains("du-lich", slugs); Assert.Contains("du-lich-6", slugs);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(6, await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryCategories.CountAsync(c => c.CreatedByUserId == user.Id && !c.IsDefault));
    }

    [Fact]
    public async Task Batch_metadata_and_photo_edit_round_trip_without_touching_drive_identity_or_bytes()
    {
        var owner = await User();
        var other = await User();
        var gatheringSlug = await CreateUserCategory(owner.Token, "Họp mặt dòng tộc");
        var templeSlug = await CreateUserCategory(owner.Token, "Từ đường");
        var id = (await Create(owner.Token)).GetProperty("id").GetInt64();
        var uploadCountBeforeInvalidDate = storage.UploadCount;
        using var invalidDateUpload = await Upload(owner.Token, id, [("invalid.jpg", "image/jpeg", Jpeg)],
            new LibraryPhotoMetadataDto { Category = gatheringSlug, DisplayDate = "adasdas" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidDateUpload.StatusCode);
        Assert.Equal("LIBRARY_INVALID_DISPLAY_DATE", (await invalidDateUpload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(uploadCountBeforeInvalidDate, storage.UploadCount);
        var metadata = new LibraryPhotoMetadataDto { Title = "  Họp mặt 2026  ", Category = gatheringSlug,
            DisplayDate = "09/10/2026", Description = "Ảnh đại gia đình", Author = "Ban liên lạc" };
        using var upload = await Upload(owner.Token, id, [("a.jpg", "image/jpeg", Jpeg), ("b.png", "image/png", Png)], metadata);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var photos = await upload.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var photo in photos.EnumerateArray())
        {
            Assert.Equal("Họp mặt 2026", photo.GetProperty("title").GetString());
            Assert.Equal(gatheringSlug, photo.GetProperty("category").GetString());
            Assert.Equal("09/10/2026", photo.GetProperty("displayDate").GetString());
            Assert.Equal("Ảnh đại gia đình", photo.GetProperty("description").GetString());
            Assert.Equal(photo.GetProperty("caption").GetString(), photo.GetProperty("description").GetString());
            Assert.Equal("Ban liên lạc", photo.GetProperty("author").GetString());
        }
        var photoId = photos[0].GetProperty("id").GetInt64();
        var calls = storage.UploadCount;
        string fileId;
        await using (var scope = factory.Services.CreateAsyncScope())
            fileId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().LibraryPhotos.SingleAsync(p => p.Id == photoId)).DriveFileId;
        using var invalidEditDate = await Send(owner.Token, HttpMethod.Put, $"/api/library/photos/{photoId}",
            new { title = "Từ đường", category = templeSlug, displayDate = "31/02/2026", description = "Mô tả mới", author = "Nguồn gia đình" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEditDate.StatusCode);
        Assert.Equal("LIBRARY_INVALID_DISPLAY_DATE", (await invalidEditDate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var edit = new { title = "Từ đường", category = templeSlug, displayDate = "09/10/2020", description = "Mô tả mới", author = "Nguồn gia đình" };
        using var denied = await Send(other.Token, HttpMethod.Put, $"/api/library/photos/{photoId}", edit);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal("LIBRARY_NOT_FOUND", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        using var invalid = await Send(owner.Token, HttpMethod.Put, $"/api/library/photos/{photoId}", new { category = "all" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var update = await Send(owner.Token, HttpMethod.Put, $"/api/library/photos/{photoId}", edit);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var detail = await Send(owner.Token, HttpMethod.Get, $"/api/library/albums/{id}");
        var roundTrip = (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photos")[0];
        Assert.Equal("Từ đường", roundTrip.GetProperty("title").GetString());
        Assert.Equal(templeSlug, roundTrip.GetProperty("category").GetString());
        Assert.Equal("09/10/2020", roundTrip.GetProperty("displayDate").GetString());
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
        Assert.Equal("LIBRARY_STORAGE_FAILED", (await failPhoto.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
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
        Assert.Equal("LIBRARY_STORAGE_FAILED", (await failAlbum.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
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

    [Fact]
    public async Task Photo_image_is_owned_cached_and_invalidated_after_photo_or_album_delete()
    {
        var owner = await User(); var other = await User();
        var albumId = (await Create(owner.Token)).GetProperty("id").GetInt64();
        using var upload = await Upload(owner.Token, albumId, [("a.jpg", "image/jpeg", Jpeg)]);
        var photoId = (await upload.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetInt64();

        using var foreign = await Send(other.Token, HttpMethod.Get, $"/api/library/photos/{photoId}/image");
        using var missing = await Send(owner.Token, HttpMethod.Get, "/api/library/photos/9223372036854775000/image");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var first = await Send(owner.Token, HttpMethod.Get, $"/api/library/photos/{photoId}/image");
        using var cached = await Send(owner.Token, HttpMethod.Get, $"/api/library/photos/{photoId}/image");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal("image/jpeg", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal("private, no-cache", first.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", first.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Jpeg, await first.Content.ReadAsByteArrayAsync());
        Assert.Equal(first.Headers.ETag, cached.Headers.ETag);
        Assert.Equal(1, storage.ImageReadCount);
        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/library/photos/{photoId}/image");
        conditionalRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        conditionalRequest.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var unchanged = await client.SendAsync(conditionalRequest);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);

        using var remove = await Send(owner.Token, HttpMethod.Delete, $"/api/library/photos/{photoId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var afterDelete = await Send(owner.Token, HttpMethod.Get, $"/api/library/photos/{photoId}/image");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);

        var album2 = (await Create(owner.Token, "Second album")).GetProperty("id").GetInt64();
        using var upload2 = await Upload(owner.Token, album2, [("b.jpg", "image/jpeg", Jpeg)]);
        var albumPhotoId = (await upload2.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetInt64();
        using var albumImage = await Send(owner.Token, HttpMethod.Get, $"/api/library/photos/{albumPhotoId}/image");
        Assert.Equal(HttpStatusCode.OK, albumImage.StatusCode);
        using var removeAlbum = await Send(owner.Token, HttpMethod.Delete, $"/api/library/albums/{album2}");
        Assert.Equal(HttpStatusCode.NoContent, removeAlbum.StatusCode);
        using var afterAlbumDelete = await Send(owner.Token, HttpMethod.Get, $"/api/library/photos/{albumPhotoId}/image");
        Assert.Equal(HttpStatusCode.NotFound, afterAlbumDelete.StatusCode);
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
    private async Task<string> CreateUserCategory(string token, string name)
    {
        using var response = await Send(token, HttpMethod.Post, "/api/library/categories", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString()!;
    }
    private async Task<HttpResponseMessage> Send(string token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
    private async Task<HttpResponseMessage> Upload(string token, long albumId, (string Name, string Mime, byte[] Bytes)[] files, LibraryPhotoMetadataDto? metadata = null)
    {
        metadata ??= new LibraryPhotoMetadataDto { Category = await CreateUserCategory(token, $"Test category {Guid.NewGuid():N}") };
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(metadata.Title ?? ""), "title");
        form.Add(new StringContent(metadata.Category), "category");
        form.Add(new StringContent(metadata.DisplayDate ?? ""), "displayDate");
        form.Add(new StringContent(metadata.Description ?? ""), "description");
        form.Add(new StringContent(metadata.Author ?? ""), "author");
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
    public int ImageReadCount { get; private set; }
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
    public Task<LibraryImageReadResult?> ReadPhotoAsync(long photoId, string albumDriveFolderId, string driveFileId,
        string contentType, long fileSize, DateTime revision, CancellationToken cancellationToken)
    {
        ImageReadCount++;
        if (!Files.TryGetValue(driveFileId, out var photo) || photo.Trashed) return Task.FromResult<LibraryImageReadResult?>(null);
        var etag = $"\"test-{photoId}-{revision.Ticks}\"";
        return Task.FromResult<LibraryImageReadResult?>(new(photo.Bytes.ToArray(), photo.Mime, etag));
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

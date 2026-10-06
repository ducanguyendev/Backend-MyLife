using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyLife.Features.Library.Services;
using MyLife.Shared.Data;
using MyLife.Shared.Entities;
using Npgsql;
using Xunit;

public sealed class LibraryCategoryMigrationTests
{
    [Theory]
    [InlineData("Ảnh cưới 2026", "anh-cuoi-2026")]
    [InlineData("Đám giỗ", "dam-gio")]
    [InlineData("  Du--lịch!!! ", "du-lich")]
    [InlineData("all", "all-category")]
    [InlineData("照片", "category")]
    public void Slug_normalization_is_deterministic_and_reserves_all(string name, string expected) =>
        Assert.Equal(expected, LibraryCategorySlug.FromName(name));

    [Fact]
    public void Long_collision_suffix_never_exceeds_64_characters()
    {
        var slug = LibraryCategorySlug.FromName(new string('a', 100));
        Assert.Equal(64, slug.Length); var suffixed = LibraryCategorySlug.WithSuffix(slug, 123);
        Assert.Equal(64, suffixed.Length); Assert.EndsWith("-123", suffixed);
    }

    [Fact]
    public async Task Upgrade_from_photo_metadata_schema_preserves_existing_albums_photos_and_category_values()
    {
        var schema = "library_upgrade_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(MyLifeFactory.ConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await create.ExecuteNonQueryAsync();
        try {
            var builder = new NpgsqlConnectionStringBuilder(MyLifeFactory.ConnectionString) { SearchPath = schema };
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261006063103_ExtendLibraryPhotoMetadata");
            var user = new User { Email = schema + "@example.com", FullName = "Legacy owner", PasswordHash = "test-only", IsActive = true };
            db.Users.Add(user); await db.SaveChangesAsync();
            var album = new LibraryAlbum { Name = "Existing album", CreatedByUserId = user.Id, DriveFolderId = "existing-folder" };
            db.LibraryAlbums.Add(album); await db.SaveChangesAsync();
            var photo = new LibraryPhoto { AlbumId = album.Id, DriveFileId = "existing-drive-file", Url = "https://lh3.googleusercontent.com/d/existing-drive-file",
                FileName = "existing.jpg", ContentType = "image/jpeg", FileSize = 123, Category = "decrees", Title = "Existing photo", CreatedAt = DateTime.UtcNow };
            db.LibraryPhotos.Add(photo); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await migrator.MigrateAsync();
            Assert.Equal("Existing album", (await db.LibraryAlbums.SingleAsync()).Name);
            var stored = await db.LibraryPhotos.SingleAsync();
            Assert.Equal(photo.Id, stored.Id); Assert.Equal("decrees", stored.Category); Assert.Equal(photo.Url, stored.Url); Assert.Equal(photo.DriveFileId, stored.DriveFileId);
            Assert.Equal(0, await db.LibraryCategories.CountAsync());
            stored.Category = new string('a', 64); await db.SaveChangesAsync();
            Assert.Equal(64, (await db.LibraryPhotos.SingleAsync()).Category.Length);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Contains("20261006090341_AddLibraryCategories", await db.Database.GetAppliedMigrationsAsync());
        } finally {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection); await drop.ExecuteNonQueryAsync();
        }
    }
}

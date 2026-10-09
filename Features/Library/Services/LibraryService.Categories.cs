using Microsoft.EntityFrameworkCore;
using MyLife.Features.Library.DTOs;
using MyLife.Shared.Entities;

namespace MyLife.Features.Library.Services;

public sealed partial class LibraryService
{
    private static readonly (string Slug, string Name)[] DefaultCategories = [
        ("photos", "Ảnh tư liệu"), ("decrees", "Sắc phong & Gia phả"),
        ("events", "Họp mặt dòng tộc"), ("temple", "Từ đường & Lăng mộ")
    ];

    public async Task EnsureDefaultLibraryCategoriesAsync(int userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockCategoryRegistryAsync(userId, ct);
        await EnsureDefaultCategoriesCoreAsync(userId, ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<LibraryCategoryDto>> ListCategoriesAsync(int userId, CancellationToken ct)
    {
        await EnsureDefaultLibraryCategoriesAsync(userId, ct);
        return await db.LibraryCategories.AsNoTracking().Where(c => c.CreatedByUserId == userId)
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new LibraryCategoryDto(c.Id, c.Name, c.Slug, c.IsDefault)).ToListAsync(ct);
    }

    public async Task<LibraryCategoryDto> CreateCategoryAsync(int userId, CreateLibraryCategoryDto dto, CancellationToken ct)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100)
            throw new LibraryOperationException(400, "Category name is required (max 100 characters).", code: "LIBRARY_CATEGORY_INVALID");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockCategoryRegistryAsync(userId, ct);
        await EnsureDefaultCategoriesCoreAsync(userId, ct);
        var slugs = (await db.LibraryCategories.Where(c => c.CreatedByUserId == userId).Select(c => c.Slug).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var baseSlug = LibraryCategorySlug.FromName(name);
        var slug = baseSlug;
        for (var suffix = 2; slugs.Contains(slug); suffix++) slug = LibraryCategorySlug.WithSuffix(baseSlug, suffix);
        var now = DateTime.UtcNow;
        var category = new LibraryCategory { CreatedByUserId = userId, Name = name, Slug = slug,
            CreatedAt = now, UpdatedAt = now, IsDefault = false };
        db.LibraryCategories.Add(category);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(category.Id, category.Name, category.Slug, category.IsDefault);
    }

    public async Task<LibraryCategoryDto> UpdateCategoryAsync(int userId, long categoryId, UpdateLibraryCategoryDto dto, CancellationToken ct)
    {
        var name = dto.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 100)
            throw new LibraryOperationException(400, "Category name is required (max 100 characters).", code: "LIBRARY_CATEGORY_INVALID");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockCategoryRegistryAsync(userId, ct);
        var category = await db.LibraryCategories.SingleOrDefaultAsync(c => c.Id == categoryId && c.CreatedByUserId == userId, ct)
            ?? throw new LibraryOperationException(404, "Library category not found.", code: "LIBRARY_CATEGORY_NOT_FOUND");
        if (category.IsDefault)
            throw new LibraryOperationException(409, "Default categories cannot be edited.", code: "LIBRARY_CATEGORY_CANNOT_EDIT");
        // Photos reference Slug. Renaming never changes the slug or photo rows.
        category.Name = name;
        category.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(category.Id, category.Name, category.Slug, category.IsDefault);
    }

    public async Task DeleteCategoryAsync(int userId, long categoryId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockCategoryRegistryAsync(userId, ct);
        var category = await db.LibraryCategories.SingleOrDefaultAsync(c => c.Id == categoryId && c.CreatedByUserId == userId, ct)
            ?? throw new LibraryOperationException(404, "Library category not found.", code: "LIBRARY_CATEGORY_NOT_FOUND");
        if (category.IsDefault)
            throw new LibraryOperationException(409, "Default Library categories cannot be deleted.", code: "LIBRARY_CATEGORY_CANNOT_DELETE");
        if (await db.LibraryPhotos.AnyAsync(p => p.Album.CreatedByUserId == userId && p.Category == category.Slug, ct))
            throw new LibraryOperationException(409, "The category is currently in use.", code: "LIBRARY_CATEGORY_IN_USE");
        db.LibraryCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task LockCategoryRegistryAsync(int userId, CancellationToken ct)
    {
        // Serialize this user's registry changes and photo category assignments
        // across API instances. Always acquire this before any album row lock.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE", ct);
    }

    private async Task EnsureDefaultCategoriesCoreAsync(int userId, CancellationToken ct)
    {
        var existing = (await db.LibraryCategories.Where(c => c.CreatedByUserId == userId).Select(c => c.Slug).ToListAsync(ct)).ToHashSet();
        var now = DateTime.UtcNow;
        foreach (var (slug, name) in DefaultCategories.Where(c => !existing.Contains(c.Slug)))
            db.LibraryCategories.Add(new LibraryCategory { CreatedByUserId = userId, Name = name, Slug = slug,
                IsDefault = true, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateCategoryAsync(int userId, string slug, CancellationToken ct)
    {
        if (slug == "all" || !await db.LibraryCategories.AnyAsync(c => c.CreatedByUserId == userId && c.Slug == slug, ct))
            throw new LibraryOperationException(400, "Category does not belong to the current user.", code: "LIBRARY_CATEGORY_INVALID");
    }
}

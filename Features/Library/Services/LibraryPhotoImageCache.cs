using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace MyLife.Features.Library.Services;

public interface ILibraryPhotoImageCache
{
    Task<LibraryImageReadResult?> GetOrCreateAsync(long photoId, string key,
        Func<Task<LibraryImageReadResult?>> factory, CancellationToken ct);
    void Invalidate(long photoId, string driveFileId, DateTime revision);
}

public sealed class LibraryPhotoImageCache(IMemoryCache cache) : ILibraryPhotoImageCache
{
    private readonly ConcurrentDictionary<long, SemaphoreSlim> gates = new();

    public async Task<LibraryImageReadResult?> GetOrCreateAsync(long photoId, string key,
        Func<Task<LibraryImageReadResult?>> factory, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out LibraryImageReadResult? cached)) return cached;
        var gate = gates.GetOrAdd(photoId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out cached)) return cached;
            var result = await factory();
            cache.Set(key, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = result is null ? TimeSpan.FromSeconds(20) : TimeSpan.FromMinutes(30),
                Size = result?.Content.Length ?? 1
            });
            return result;
        }
        finally { gate.Release(); }
    }

    public void Invalidate(long photoId, string driveFileId, DateTime revision) =>
        cache.Remove(GoogleDriveLibraryStorageService.ImageCacheKey(photoId, driveFileId, revision));
}

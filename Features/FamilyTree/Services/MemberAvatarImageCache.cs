using Microsoft.Extensions.Caching.Memory;

namespace MyLife.Features.FamilyTree.Services;

public sealed record MemberAvatarImageResult(byte[] Content, string ContentType, string ETag);

public interface IMemberAvatarImageCache
{
    Task<MemberAvatarImageResult?> GetOrCreateAsync(int memberId, string key, Func<Task<MemberAvatarImageResult?>> factory, CancellationToken ct);
    void Invalidate(int memberId, string? fileId, DateTime updatedAt);
}

public sealed class MemberAvatarImageCache(IMemoryCache cache) : IMemberAvatarImageCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> gates = new();

    public async Task<MemberAvatarImageResult?> GetOrCreateAsync(int memberId, string key, Func<Task<MemberAvatarImageResult?>> factory, CancellationToken ct)
    {
        if (cache.TryGetValue(key, out MemberAvatarImageResult? cached)) return cached;
        var gate = gates.GetOrAdd(memberId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out cached)) return cached;
            var result = await factory();
            // Briefly negative-cache upstream failures too, so repeated image
            // mounts cannot turn a Google 429 into a request storm.
            cache.Set(key, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = result is null ? TimeSpan.FromSeconds(20) : TimeSpan.FromMinutes(30),
                Size = result?.Content.Length ?? 1
            });
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(int memberId, string? fileId, DateTime updatedAt)
    {
        if (string.IsNullOrWhiteSpace(fileId)) return;
        cache.Remove(GoogleDriveMemberAvatarService.ImageCacheKey(memberId, fileId, updatedAt));
    }
}

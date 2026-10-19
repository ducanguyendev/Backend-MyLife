using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyLife.Shared.Data;

namespace MyLife.Features.FamilyTree.Services;

public interface IFamilyLocationGeocodingQueue
{
    void Enqueue(long treeId, int memberId, string address);
}

public sealed class FamilyLocationGeocodingQueue : BackgroundService, IFamilyLocationGeocodingQueue
{
    private sealed record WorkItem(long TreeId, int MemberId, string Address);
    private readonly Channel<WorkItem> channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly HashSet<int> queued = [];
    private readonly object sync = new();
    private readonly IServiceScopeFactory scopeFactory;
    private readonly GeocodingOptions settings;
    private readonly ILogger<FamilyLocationGeocodingQueue> logger;
    private DateTimeOffset lastRequestAt = DateTimeOffset.MinValue;

    public FamilyLocationGeocodingQueue(IServiceScopeFactory scopeFactory, IOptions<GeocodingOptions> settings, ILogger<FamilyLocationGeocodingQueue> logger)
    { this.scopeFactory = scopeFactory; this.settings = settings.Value; this.logger = logger; }

    public void Enqueue(long treeId, int memberId, string address)
    {
        var normalizedAddress = FamilyAddressNormalizer.Normalize(address);
        if (!settings.Enabled || normalizedAddress is null) return;
        lock (sync)
        {
            if (queued.Add(memberId) && !channel.Writer.TryWrite(new WorkItem(treeId, memberId, normalizedAddress))) queued.Remove(memberId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(item, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Family map geocoding item failed with {ErrorType}.", ex.GetType().Name); }
            finally { lock (sync) queued.Remove(item.MemberId); }
        }
    }

    private async Task ProcessAsync(WorkItem item, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.FamilyMembers.SingleOrDefaultAsync(x => x.Id == item.MemberId && x.FamilyTreeId == item.TreeId, ct);
        if (member is null || !string.Equals(FamilyAddressNormalizer.Normalize(member.Address), item.Address, StringComparison.OrdinalIgnoreCase) || IsFreshCache(member, settings.FailureCacheDays)) return;

        var elapsed = lastRequestAt == DateTimeOffset.MinValue ? int.MaxValue : (int)Math.Min(int.MaxValue, (DateTimeOffset.UtcNow - lastRequestAt).TotalMilliseconds);
        var remaining = lastRequestAt == DateTimeOffset.MinValue ? 0 : Math.Max(15000, settings.MinIntervalMilliseconds) - elapsed;
        if (remaining > 0) await Task.Delay(remaining, ct);
        lastRequestAt = DateTimeOffset.UtcNow;
        var geocoder = scope.ServiceProvider.GetRequiredService<IGeocodingService>();
        var result = await geocoder.GeocodeAsync(item.Address, ct);

        // The address may have changed while the upstream request was in flight.
        var currentAddress = await db.FamilyMembers.Where(x => x.Id == item.MemberId && x.FamilyTreeId == item.TreeId).Select(x => x.Address).SingleOrDefaultAsync(ct);
        if (!string.Equals(FamilyAddressNormalizer.Normalize(currentAddress), item.Address, StringComparison.OrdinalIgnoreCase)) return;
        member.GeocodedAddress = item.Address;
        member.GeocodedAt = DateTime.UtcNow;
        if (result is { } location && double.IsFinite(location.Latitude) && location.Latitude is >= -90 and <= 90 &&
            double.IsFinite(location.Longitude) && location.Longitude is >= -180 and <= 180 && !string.IsNullOrWhiteSpace(location.DisplayName))
        {
            member.Latitude = location.Latitude;
            member.Longitude = location.Longitude;
            member.LocationDisplayName = location.DisplayName.Trim()[..Math.Min(location.DisplayName.Trim().Length, 500)];
        }
        else
        {
            member.Latitude = null; member.Longitude = null; member.LocationDisplayName = null;
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool IsFreshCache(MyLife.Shared.Entities.FamilyMember member, int failureCacheDays) =>
        string.Equals(FamilyAddressNormalizer.Normalize(member.Address), FamilyAddressNormalizer.Normalize(member.GeocodedAddress), StringComparison.OrdinalIgnoreCase) &&
        member.Latitude is double latitude && double.IsFinite(latitude) && latitude is >= -90 and <= 90 &&
        member.Longitude is double longitude && double.IsFinite(longitude) && longitude is >= -180 and <= 180 &&
        !string.IsNullOrWhiteSpace(member.LocationDisplayName) ||
        string.Equals(FamilyAddressNormalizer.Normalize(member.Address), FamilyAddressNormalizer.Normalize(member.GeocodedAddress), StringComparison.OrdinalIgnoreCase) &&
        member.Latitude is null && member.Longitude is null && member.GeocodedAt is DateTime failedAt &&
        failedAt > DateTime.UtcNow.AddDays(-failureCacheDays);
}

public sealed class FamilyMapService(AppDbContext db, IFamilyLocationGeocodingQueue queue, IOptions<GeocodingOptions> settings)
{
    public async Task<Features.FamilyTree.DTOs.FamilyMapDto> GetMapAsync(int userId, CancellationToken ct)
    {
        var treeId = await db.FamilyTrees.AsNoTracking().Where(x => x.OwnerUserId == userId).Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (treeId is null)
            return new Features.FamilyTree.DTOs.FamilyMapDto();

        var members = await db.FamilyMembers.AsNoTracking().Where(x => x.FamilyTreeId == treeId.Value)
            .Select(x => new { x.Id, x.FullName, x.Generation, x.Address, x.GeocodedAddress, x.GeocodedAt, x.Latitude, x.Longitude, x.LocationDisplayName })
            .OrderBy(x => x.Generation).ThenBy(x => x.FullName).ToListAsync(ct);

        var canGeocode = settings.Value.Enabled && string.Equals(settings.Value.Provider, "Nominatim", StringComparison.OrdinalIgnoreCase);
        var needsGeocoding = members.Where(x => canGeocode && !string.IsNullOrWhiteSpace(x.Address) &&
            (!string.Equals(FamilyAddressNormalizer.Normalize(x.Address), FamilyAddressNormalizer.Normalize(x.GeocodedAddress), StringComparison.OrdinalIgnoreCase) ||
             (x.Latitude is null && x.Longitude is null && (x.GeocodedAt is null || x.GeocodedAt <= DateTime.UtcNow.AddDays(-Math.Max(1, settings.Value.FailureCacheDays)))))).ToList();
        var limit = Math.Clamp(settings.Value.MaxQueuePerRequest, 0, 5);
        foreach (var member in needsGeocoding.Take(limit))
            queue.Enqueue(treeId.Value, member.Id, member.Address!);

        var located = members.Where(x => string.Equals(FamilyAddressNormalizer.Normalize(x.Address), FamilyAddressNormalizer.Normalize(x.GeocodedAddress), StringComparison.OrdinalIgnoreCase) &&
            x.Latitude is double lat && double.IsFinite(lat) && lat is >= -90 and <= 90 &&
            x.Longitude is double lon && double.IsFinite(lon) && lon is >= -180 and <= 180 &&
            !string.IsNullOrWhiteSpace(x.LocationDisplayName)).ToList();
        var locations = located.GroupBy(x => x.LocationDisplayName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new Features.FamilyTree.DTOs.FamilyMapLocationDto
            {
                Key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(group.Key.ToUpperInvariant())))[..16].ToLowerInvariant(),
                DisplayName = group.Key,
                // Round to roughly city scale before the map API exposes coordinates.
                Latitude = Math.Round(group.Average(x => Math.Round(x.Latitude!.Value, 1, MidpointRounding.AwayFromZero)), 1, MidpointRounding.AwayFromZero),
                Longitude = Math.Round(group.Average(x => Math.Round(x.Longitude!.Value, 1, MidpointRounding.AwayFromZero)), 1, MidpointRounding.AwayFromZero),
                MemberCount = group.Count(),
                Members = group.Select(x => new Features.FamilyTree.DTOs.FamilyMapMemberDto { Id = x.Id, FullName = x.FullName, Generation = x.Generation }).ToList()
            }).OrderByDescending(x => x.MemberCount).ThenBy(x => x.DisplayName).ToList();
        return new Features.FamilyTree.DTOs.FamilyMapDto { TotalMembers = members.Count, LocatedMembers = located.Count, UnlocatedMembers = members.Count - located.Count, PendingGeocodingMembers = needsGeocoding.Count, Locations = locations };
    }
}

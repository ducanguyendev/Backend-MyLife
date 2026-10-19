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
        if (member is null || !string.Equals(FamilyAddressNormalizer.Normalize(member.Address), item.Address, StringComparison.OrdinalIgnoreCase) || IsFreshCache(member, settings.FailureCacheHours)) return;

        var elapsed = lastRequestAt == DateTimeOffset.MinValue ? int.MaxValue : (int)Math.Min(int.MaxValue, (DateTimeOffset.UtcNow - lastRequestAt).TotalMilliseconds);
        var remaining = lastRequestAt == DateTimeOffset.MinValue ? 0 : settings.EffectiveMinIntervalMilliseconds - elapsed;
        if (remaining > 0) await Task.Delay(remaining, ct);
        lastRequestAt = DateTimeOffset.UtcNow;
        var geocoder = scope.ServiceProvider.GetRequiredService<IGeocodingService>();
        logger.LogDebug("Family map geocode queued member {MemberId} for normalized address {Address}.", item.MemberId, item.Address);
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
            logger.LogDebug("Family map geocode succeeded for member {MemberId}: {DisplayName}.", item.MemberId, member.LocationDisplayName);
        }
        else
        {
            member.Latitude = null; member.Longitude = null; member.LocationDisplayName = null;
            logger.LogDebug("Family map geocode failed or found no result for member {MemberId}.", item.MemberId);
        }
        await db.SaveChangesAsync(ct);
    }

    private static bool IsFreshCache(MyLife.Shared.Entities.FamilyMember member, int failureCacheHours) =>
        FamilyMapMemberStatusResolver.Resolve(member.Address, member.GeocodedAddress, member.GeocodedAt, member.Latitude, member.Longitude,
            member.LocationDisplayName, true, failureCacheHours, DateTime.UtcNow) is FamilyMapMemberLocationStatus.Located or FamilyMapMemberLocationStatus.Failed;
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
        var now = DateTime.UtcNow;
        var states = members.Select(member =>
        {
            var status = FamilyMapMemberStatusResolver.Resolve(member.Address, member.GeocodedAddress, member.GeocodedAt, member.Latitude, member.Longitude,
                member.LocationDisplayName, canGeocode, settings.Value.FailureCacheHours, now);
            return new { Member = member, Status = status };
        }).ToList();
        var needsGeocoding = states.Where(x => x.Status == FamilyMapMemberLocationStatus.Pending).ToList();
        var noAddressCount = states.Count(x => x.Status == FamilyMapMemberLocationStatus.NoAddress);
        var failedCount = states.Count(x => x.Status == FamilyMapMemberLocationStatus.Failed);
        var limit = Math.Clamp(settings.Value.MaxQueuePerRequest, 0, 5);
        foreach (var state in needsGeocoding.Take(limit))
            queue.Enqueue(treeId.Value, state.Member.Id, state.Member.Address!);

        var located = states.Where(x => x.Status == FamilyMapMemberLocationStatus.Located).ToList();
        var locations = FamilyMapLocationBuilder.Build(located.Select(x => new FamilyMapLocatedMember(
            x.Member.Id, x.Member.FullName, x.Member.Generation, x.Member.Latitude!.Value, x.Member.Longitude!.Value, x.Member.LocationDisplayName!)));
        return new Features.FamilyTree.DTOs.FamilyMapDto { TotalMembers = members.Count, LocatedMembers = located.Count, UnlocatedMembers = members.Count - located.Count, PendingGeocodingMembers = needsGeocoding.Count, NoAddressMembers = noAddressCount, FailedGeocodingMembers = failedCount, Locations = locations };
    }
}

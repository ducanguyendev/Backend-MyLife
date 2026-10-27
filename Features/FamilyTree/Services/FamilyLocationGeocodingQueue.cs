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
    private readonly Dictionary<int, WorkItem> pendingLatest = [];
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
            var item = new WorkItem(treeId, memberId, normalizedAddress);
            if (!queued.Add(memberId))
            {
                pendingLatest[memberId] = item;
                return;
            }
            if (!channel.Writer.TryWrite(item)) queued.Remove(memberId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(item, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Family map geocoding item failed with {ErrorType}.", ex.GetType().Name); }
            finally
            {
                lock (sync)
                {
                    if (!pendingLatest.Remove(item.MemberId, out var latest) || !channel.Writer.TryWrite(latest))
                        queued.Remove(item.MemberId);
                }
            }
        }
    }

    private async Task ProcessAsync(WorkItem item, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.FamilyMembers.SingleOrDefaultAsync(x => x.Id == item.MemberId && x.FamilyTreeId == item.TreeId, ct);
        if (member is null || string.Equals(member.LocationSource, "MANUAL", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(FamilyAddressNormalizer.Normalize(member.Address), item.Address, StringComparison.OrdinalIgnoreCase) ||
            IsFreshCache(member, settings.FailureCacheHours)) return;

        var elapsed = lastRequestAt == DateTimeOffset.MinValue ? int.MaxValue : (int)Math.Min(int.MaxValue, (DateTimeOffset.UtcNow - lastRequestAt).TotalMilliseconds);
        var remaining = lastRequestAt == DateTimeOffset.MinValue ? 0 : settings.EffectiveMinIntervalMilliseconds - elapsed;
        if (remaining > 0) await Task.Delay(remaining, ct);
        lastRequestAt = DateTimeOffset.UtcNow;
        var geocoder = scope.ServiceProvider.GetRequiredService<IGeocodingService>();
        logger.LogDebug("Family map geocode queued member {MemberId} for address fingerprint {AddressFingerprint}.", item.MemberId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.Address.ToUpperInvariant())))[..12]);
        var result = await geocoder.GeocodeAsync(item.Address, ct);

        // The address may have changed while the upstream request was in flight.
        var current = await db.FamilyMembers.Where(x => x.Id == item.MemberId && x.FamilyTreeId == item.TreeId)
            .Select(x => new { x.Address, x.LocationSource }).SingleOrDefaultAsync(ct);
        if (current is null ||
            !string.Equals(FamilyAddressNormalizer.Normalize(current.Address), item.Address, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current.LocationSource, "MANUAL", StringComparison.OrdinalIgnoreCase)) return;
        var geocodedAt = DateTime.UtcNow;
        double? latitude;
        double? longitude;
        string? displayName;
        string? locationSource;
        if (result is { } location && double.IsFinite(location.Latitude) && location.Latitude is >= -90 and <= 90 &&
            double.IsFinite(location.Longitude) && location.Longitude is >= -180 and <= 180 && !string.IsNullOrWhiteSpace(location.DisplayName))
        {
            latitude = location.Latitude;
            longitude = location.Longitude;
            displayName = location.DisplayName.Trim()[..Math.Min(location.DisplayName.Trim().Length, 500)];
            locationSource = "GEOCODED";
            logger.LogDebug("Family map geocode succeeded for member {MemberId}.", item.MemberId);
        }
        else
        {
            latitude = null;
            longitude = null;
            displayName = null;
            locationSource = null;
            logger.LogDebug("Family map geocode failed or found no result for member {MemberId}.", item.MemberId);
        }
        // Apply only if the address and manual-source state still match. This
        // closes the race where a user confirms a location after the final read.
        await db.FamilyMembers
            .Where(x => x.Id == item.MemberId && x.FamilyTreeId == item.TreeId &&
                x.Address == current.Address && (x.LocationSource == null || x.LocationSource != "MANUAL"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.GeocodedAddress, item.Address)
                .SetProperty(x => x.GeocodedAt, geocodedAt)
                .SetProperty(x => x.Latitude, latitude)
                .SetProperty(x => x.Longitude, longitude)
                .SetProperty(x => x.LocationDisplayName, displayName)
                .SetProperty(x => x.LocationSource, locationSource), ct);
    }

    private static bool IsFreshCache(MyLife.Shared.Entities.FamilyMember member, int failureCacheHours) =>
        FamilyMapMemberStatusResolver.Resolve(member.Address, member.GeocodedAddress, member.GeocodedAt, member.Latitude, member.Longitude,
            member.LocationDisplayName, true, failureCacheHours, DateTime.UtcNow, member.LocationSource) is FamilyMapMemberLocationStatus.Located or FamilyMapMemberLocationStatus.Failed;
}

public sealed class FamilyMapService(AppDbContext db, IFamilyLocationGeocodingQueue queue, IOptions<GeocodingOptions> settings)
{
    public async Task<FamilyMapRetryGeocodingResult> RetryFailedGeocodingAsync(int userId, CancellationToken ct)
    {
        var treeId = await db.FamilyTrees.AsNoTracking().Where(x => x.OwnerUserId == userId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (treeId is null) return new FamilyMapRetryGeocodingResult(0, 0, 0, 0);

        var members = await db.FamilyMembers.Where(x => x.FamilyTreeId == treeId.Value)
            .Select(x => new { x.Id, x.Address, x.GeocodedAddress, x.GeocodedAt, x.Latitude, x.Longitude, x.LocationDisplayName, x.LocationSource })
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        var manual = members.Where(x => string.Equals(x.LocationSource, "MANUAL", StringComparison.OrdinalIgnoreCase)).ToArray();
        var states = members.Select(member => new
        {
            Member = member,
            Status = FamilyMapMemberStatusResolver.Resolve(member.Address, member.GeocodedAddress, member.GeocodedAt,
                member.Latitude, member.Longitude, member.LocationDisplayName, settings.Value.Enabled,
                settings.Value.FailureCacheHours, now, member.LocationSource),
        }).ToArray();
        var noAddress = states.Count(x => x.Status == FamilyMapMemberLocationStatus.NoAddress);
        var located = states.Count(x => x.Status == FamilyMapMemberLocationStatus.Located &&
            !string.Equals(x.Member.LocationSource, "MANUAL", StringComparison.OrdinalIgnoreCase));
        var retryable = settings.Value.Enabled
            ? states.Where(x => (x.Status is FamilyMapMemberLocationStatus.Failed or FamilyMapMemberLocationStatus.Pending) &&
                !string.Equals(x.Member.LocationSource, "MANUAL", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(x.Member.Address)).ToArray()
            : [];
        var failedIds = retryable.Where(x => x.Status == FamilyMapMemberLocationStatus.Failed).Select(x => x.Member.Id).ToArray();

        if (failedIds.Length > 0)
        {
            await db.FamilyMembers.Where(x => x.FamilyTreeId == treeId.Value && failedIds.Contains(x.Id) &&
                (x.LocationSource == null || x.LocationSource.ToUpper() != "MANUAL"))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.GeocodedAddress, (string?)null)
                    .SetProperty(x => x.GeocodedAt, (DateTime?)null), ct);
        }

        foreach (var item in retryable)
            queue.Enqueue(treeId.Value, item.Member.Id, item.Member.Address!);

        return new FamilyMapRetryGeocodingResult(retryable.Length, manual.Length, located, noAddress);
    }

    public async Task<Features.FamilyTree.DTOs.FamilyMapDto> GetMapAsync(int userId, CancellationToken ct)
    {
        var treeId = await db.FamilyTrees.AsNoTracking().Where(x => x.OwnerUserId == userId).Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (treeId is null)
            return new Features.FamilyTree.DTOs.FamilyMapDto();

        var members = await db.FamilyMembers.AsNoTracking().Where(x => x.FamilyTreeId == treeId.Value)
            .Select(x => new { x.Id, x.FullName, x.Generation, x.Address, x.GeocodedAddress, x.GeocodedAt, x.Latitude, x.Longitude, x.LocationDisplayName, x.LocationSource })
            .OrderBy(x => x.Generation).ThenBy(x => x.FullName).ToListAsync(ct);

        var canGeocode = settings.Value.Enabled;
        var now = DateTime.UtcNow;
        var states = members.Select(member =>
        {
            var status = FamilyMapMemberStatusResolver.Resolve(member.Address, member.GeocodedAddress, member.GeocodedAt, member.Latitude, member.Longitude,
                member.LocationDisplayName, canGeocode, settings.Value.FailureCacheHours, now, member.LocationSource);
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
        return new Features.FamilyTree.DTOs.FamilyMapDto { TotalMembers = members.Count, LocatedMembers = located.Count, UnlocatedMembers = members.Count - located.Count, PendingGeocodingMembers = needsGeocoding.Count, NoAddressMembers = noAddressCount, FailedGeocodingMembers = failedCount, Locations = locations,
            Members = states.Select(x => new Features.FamilyTree.DTOs.FamilyMapMemberLocationDto { Id = x.Member.Id, FullName = x.Member.FullName, Generation = x.Member.Generation, Address = x.Member.Address, Status = x.Status switch { FamilyMapMemberLocationStatus.NoAddress => "NO_ADDRESS", FamilyMapMemberLocationStatus.Pending => "PENDING", FamilyMapMemberLocationStatus.Failed => "FAILED", FamilyMapMemberLocationStatus.Located => "LOCATED", _ => "UNAVAILABLE" }, Latitude = x.Member.Latitude, Longitude = x.Member.Longitude, LocationDisplayName = x.Member.LocationDisplayName, LocationSource = x.Member.LocationSource }).ToList() };
    }
}

public sealed record FamilyMapRetryGeocodingResult(int Queued, int SkippedManual, int SkippedLocated, int NoAddress);

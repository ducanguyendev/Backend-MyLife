namespace MyLife.Features.FamilyTree.Services;

public enum FamilyMapMemberLocationStatus
{
    NoAddress,
    Pending,
    Failed,
    Located,
    Unavailable,
}

public static class FamilyMapMemberStatusResolver
{
    public static FamilyMapMemberLocationStatus Resolve(
        string? address,
        string? geocodedAddress,
        DateTime? geocodedAt,
        double? latitude,
        double? longitude,
        string? displayName,
        bool geocodingEnabled,
        int failureCacheHours,
        DateTime utcNow,
        string? locationSource = null)
    {
        var normalizedAddress = FamilyAddressNormalizer.Normalize(address);
        var hasValidCoordinates = latitude is double lat && double.IsFinite(lat) && lat is >= -90 and <= 90 &&
            longitude is double lon && double.IsFinite(lon) && lon is >= -180 and <= 180 && !string.IsNullOrWhiteSpace(displayName);
        if (string.Equals(locationSource, "MANUAL", StringComparison.OrdinalIgnoreCase))
            return hasValidCoordinates ? FamilyMapMemberLocationStatus.Located : FamilyMapMemberLocationStatus.Failed;
        if (normalizedAddress is null) return FamilyMapMemberLocationStatus.NoAddress;

        var addressMatches = string.Equals(normalizedAddress, FamilyAddressNormalizer.Normalize(geocodedAddress), StringComparison.OrdinalIgnoreCase);
        if (addressMatches && hasValidCoordinates) return FamilyMapMemberLocationStatus.Located;

        var failureExpiry = utcNow.AddHours(-Math.Clamp(failureCacheHours, 1, 168));
        if (addressMatches && latitude is null && longitude is null && geocodedAt is DateTime failedAt && failedAt > failureExpiry)
            return FamilyMapMemberLocationStatus.Failed;

        return geocodingEnabled ? FamilyMapMemberLocationStatus.Pending : FamilyMapMemberLocationStatus.Unavailable;
    }
}

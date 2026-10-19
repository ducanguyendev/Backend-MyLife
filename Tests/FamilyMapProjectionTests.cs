using MyLife.Features.FamilyTree.Services;
using Xunit;

public sealed class FamilyMapProjectionTests
{
    [Fact]
    public void ClassifiesNoAddressPendingFailedLocatedAndChangedAddressSeparately()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(FamilyMapMemberLocationStatus.NoAddress, Resolve(null, null, null, null, null, null, true, now));
        Assert.Equal(FamilyMapMemberLocationStatus.Pending, Resolve("12 Road", null, null, null, null, null, true, now));
        Assert.Equal(FamilyMapMemberLocationStatus.Failed, Resolve("12 Road", "12 Road", now.AddHours(-2), null, null, null, true, now));
        Assert.Equal(FamilyMapMemberLocationStatus.Located, Resolve("12 Road", "12 Road", now, 10.123456, 106.654321, "12 Road, City", true, now));
        Assert.Equal(FamilyMapMemberLocationStatus.Pending, Resolve("New address", "Old address", now, 10.1, 106.1, "Old address", true, now));
        Assert.Equal(FamilyMapMemberLocationStatus.Unavailable, Resolve("12 Road", null, null, null, null, null, false, now));
    }

    [Fact]
    public void FailureMovesBackToPendingAfterConfiguredRetryWindow()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(FamilyMapMemberLocationStatus.Failed, Resolve("Address", "Address", now.AddHours(-23), null, null, null, true, now));
        Assert.Equal(FamilyMapMemberLocationStatus.Pending, Resolve("Address", "Address", now.AddHours(-25), null, null, null, true, now));
    }

    [Fact]
    public void GroupsSamePreciseLocationAndKeepsNearbyAddressesWithSameLabelSeparate()
    {
        var locations = FamilyMapLocationBuilder.Build(new[]
        {
            new FamilyMapLocatedMember(1, "A", 1, 10.1234567, 106.1234567, "Ho Chi Minh City, Vietnam"),
            new FamilyMapLocatedMember(2, "B", 1, 10.2234567, 106.2234567, "Ho Chi Minh City, Vietnam"),
            new FamilyMapLocatedMember(3, "C", 1, 10.1234568, 106.1234568, "12 Road, Ho Chi Minh City, Vietnam"),
        });

        Assert.Equal(2, locations.Count);
        Assert.Equal(new[] { 2, 1 }, locations.Select(location => location.MemberCount));
        Assert.InRange(locations[0].Latitude, 10.12345674, 10.12345676);
        Assert.InRange(locations[0].Longitude, 106.12345674, 106.12345676);
        Assert.Equal("12 Road, Ho Chi Minh City, Vietnam", locations[0].DisplayName);
    }

    private static FamilyMapMemberLocationStatus Resolve(string? address, string? geocodedAddress, DateTime? geocodedAt,
        double? latitude, double? longitude, string? displayName, bool enabled, DateTime now) =>
        FamilyMapMemberStatusResolver.Resolve(address, geocodedAddress, geocodedAt, latitude, longitude, displayName, enabled, 24, now);
}

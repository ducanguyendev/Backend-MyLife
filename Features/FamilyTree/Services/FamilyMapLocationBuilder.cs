using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MyLife.Features.FamilyTree.DTOs;

namespace MyLife.Features.FamilyTree.Services;

public sealed record FamilyMapLocatedMember(int Id, string FullName, int Generation, double Latitude, double Longitude, string DisplayName);

public static class FamilyMapLocationBuilder
{
    public static IReadOnlyList<FamilyMapLocationDto> Build(IEnumerable<FamilyMapLocatedMember> locatedMembers) =>
        locatedMembers
            .GroupBy(member => LocationKey(member.Latitude, member.Longitude), StringComparer.Ordinal)
            .Select(group => new FamilyMapLocationDto
            {
                Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)))[..16].ToLowerInvariant(),
                DisplayName = group.Select(member => member.DisplayName.Trim()).OrderByDescending(name => name.Length).First(),
                Latitude = group.Average(member => member.Latitude),
                Longitude = group.Average(member => member.Longitude),
                MemberCount = group.Count(),
                Members = group.Select(member => new FamilyMapMemberDto { Id = member.Id, FullName = member.FullName, Generation = member.Generation }).ToList(),
            })
            .OrderByDescending(location => location.MemberCount)
            .ThenBy(location => location.DisplayName)
            .ToList();

    private static string LocationKey(double latitude, double longitude) =>
        $"{Math.Round(latitude, 5, MidpointRounding.AwayFromZero).ToString("F5", CultureInfo.InvariantCulture)}:{Math.Round(longitude, 5, MidpointRounding.AwayFromZero).ToString("F5", CultureInfo.InvariantCulture)}";
}

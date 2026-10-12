namespace MyLife.Features.FamilyTree.Services;

public sealed record GeocodingResult(double Latitude, double Longitude, string DisplayName);

public interface IGeocodingService
{
    Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken);
}

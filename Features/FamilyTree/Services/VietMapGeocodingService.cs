using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MyLife.Features.FamilyTree.Services;

public sealed class VietMapGeocodingService(
    IHttpClientFactory clients,
    IOptions<GeocodingOptions> options,
    IConfiguration configuration,
    NominatimGeocodingService fallback,
    ILogger<VietMapGeocodingService> logger) : IGeocodingService
{
    public async Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var apiKey = configuration["VietMap:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable("VIETMAP_API_KEY");
        if (!settings.Enabled) return null;
        if (!settings.VietMapEnabled || string.IsNullOrWhiteSpace(apiKey))
        {
            if (settings.VietMapEnabled && string.IsNullOrWhiteSpace(apiKey))
                logger.LogWarning("VIETMAP geocoding is enabled but VietMap:ApiKey is not configured; using fallback provider.");
            return await fallback.GeocodeAsync(address, cancellationToken);
        }

        try
        {
            var normalized = FamilyAddressNormalizer.Normalize(address);
            if (normalized is null) return null;
            var baseUrl = settings.VietMapBaseUrl.TrimEnd('/');
            var searchUri = new UriBuilder($"{baseUrl}/api/autocomplete/v4")
            {
                Query = $"apikey={Uri.EscapeDataString(apiKey)}&text={Uri.EscapeDataString(normalized)}&display_type={Math.Clamp(settings.VietMapDisplayType, 1, 6)}",
            }.Uri;
            using var searchResponse = await clients.CreateClient(nameof(VietMapGeocodingService))
                .GetAsync(searchUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            logger.LogDebug("VIETMAP autocomplete returned HTTP {StatusCode} for address fingerprint {AddressFingerprint}.",
                (int)searchResponse.StatusCode, Fingerprint(normalized));
            if (!searchResponse.IsSuccessStatusCode)
            {
                if (searchResponse.StatusCode == HttpStatusCode.Unauthorized)
                    logger.LogWarning("VIETMAP rejected the configured API key; using fallback provider.");
                return await fallback.GeocodeAsync(normalized, cancellationToken);
            }

            await using var searchStream = await searchResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var searchDocument = await JsonDocument.ParseAsync(searchStream, cancellationToken: cancellationToken);
            if (searchDocument.RootElement.ValueKind != JsonValueKind.Array)
                return await fallback.GeocodeAsync(normalized, cancellationToken);

            var firstCandidate = searchDocument.RootElement.EnumerateArray()
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(ReadString(candidate, "ref_id")));
            var referenceId = ReadString(firstCandidate, "ref_id");
            if (string.IsNullOrWhiteSpace(referenceId))
                return await fallback.GeocodeAsync(normalized, cancellationToken);

            var placeUri = new UriBuilder($"{baseUrl}/api/place/v4")
            {
                Query = $"apikey={Uri.EscapeDataString(apiKey)}&refid={Uri.EscapeDataString(referenceId)}",
            }.Uri;
            using var placeResponse = await clients.CreateClient(nameof(VietMapGeocodingService))
                .GetAsync(placeUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            logger.LogDebug("VIETMAP place returned HTTP {StatusCode} for address fingerprint {AddressFingerprint}.",
                (int)placeResponse.StatusCode, Fingerprint(normalized));
            if (!placeResponse.IsSuccessStatusCode)
                return await fallback.GeocodeAsync(normalized, cancellationToken);

            await using var placeStream = await placeResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var placeDocument = await JsonDocument.ParseAsync(placeStream, cancellationToken: cancellationToken);
            var place = placeDocument.RootElement;
            if (!TryCoordinate(place, "lat", -90, 90, out var latitude) ||
                !TryCoordinate(place, "lng", -180, 180, out var longitude) ||
                string.IsNullOrWhiteSpace(ReadString(place, "display")))
                return await fallback.GeocodeAsync(normalized, cancellationToken);

            return new GeocodingResult(latitude, longitude, ReadString(place, "display")!.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException)
        {
            logger.LogWarning("VIETMAP geocoding failed with {ErrorType}; using fallback provider.", ex.GetType().Name);
            return await fallback.GeocodeAsync(address, cancellationToken);
        }
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..12];

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryCoordinate(JsonElement element, string name, double minimum, double maximum, out double result)
    {
        result = 0;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return false;
        var valid = value.ValueKind == JsonValueKind.Number
            ? value.TryGetDouble(out result)
            : value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        return valid && double.IsFinite(result) && result >= minimum && result <= maximum;
    }
}

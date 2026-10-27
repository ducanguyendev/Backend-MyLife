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
    private static int missingKeyWarningLogged;

    public async Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var apiKey = configuration["VietMap:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = Environment.GetEnvironmentVariable("VIETMAP_API_KEY");
        if (!settings.Enabled) return null;

        var normalized = FamilyAddressNormalizer.Normalize(address);
        if (normalized is null) return null;
        var fingerprint = Fingerprint(normalized);
        if (!settings.VietMapEnabled || string.IsNullOrWhiteSpace(apiKey))
        {
            if (settings.VietMapEnabled && Interlocked.Exchange(ref missingKeyWarningLogged, 1) == 0)
                logger.LogWarning("VIETMAP API key is not configured; using Nominatim fallback.");
            return await UseFallbackAsync(normalized, fingerprint, "", 0, "", "VIETMAP_DISABLED", cancellationToken);
        }

        var searchStatus = "";
        var candidateCount = 0;
        var placeStatus = "";
        var baseUrl = settings.VietMapBaseUrl.TrimEnd('/');
        try
        {
            var searchUri = new UriBuilder($"{baseUrl}/api/search/v4")
            {
                Query = $"apikey={Uri.EscapeDataString(apiKey)}&text={Uri.EscapeDataString(normalized)}&display_type=5",
            }.Uri;
            using var searchResponse = await clients.CreateClient(nameof(VietMapGeocodingService))
                .GetAsync(searchUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            searchStatus = ((int)searchResponse.StatusCode).ToString(CultureInfo.InvariantCulture);
            if (!searchResponse.IsSuccessStatusCode)
                return await UseFallbackAsync(normalized, fingerprint, searchStatus, 0, "", StatusReason(searchResponse.StatusCode), cancellationToken);

            await using var searchStream = await searchResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var searchDocument = await JsonDocument.ParseAsync(searchStream, cancellationToken: cancellationToken);
            if (searchDocument.RootElement.ValueKind != JsonValueKind.Array)
                return await UseFallbackAsync(normalized, fingerprint, searchStatus, 0, "", "VIETMAP_EMPTY", cancellationToken);

            var candidates = searchDocument.RootElement.EnumerateArray().ToArray();
            candidateCount = candidates.Length;
            var referenceId = candidates.Select(candidate => ReadString(candidate, "ref_id"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (referenceId is null)
                return await UseFallbackAsync(normalized, fingerprint, searchStatus, candidateCount, "", "VIETMAP_EMPTY", cancellationToken);

            var placeUri = new UriBuilder($"{baseUrl}/api/place/v4")
            {
                Query = $"apikey={Uri.EscapeDataString(apiKey)}&refid={Uri.EscapeDataString(referenceId)}",
            }.Uri;
            using var placeResponse = await clients.CreateClient(nameof(VietMapGeocodingService))
                .GetAsync(placeUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            placeStatus = ((int)placeResponse.StatusCode).ToString(CultureInfo.InvariantCulture);
            if (!placeResponse.IsSuccessStatusCode)
                return await UseFallbackAsync(normalized, fingerprint, searchStatus, candidateCount, placeStatus, StatusReason(placeResponse.StatusCode), cancellationToken);

            await using var placeStream = await placeResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var placeDocument = await JsonDocument.ParseAsync(placeStream, cancellationToken: cancellationToken);
            var place = placeDocument.RootElement;
            if (!TryCoordinate(place, "lat", -90, 90, out var latitude) ||
                !TryCoordinate(place, "lng", -180, 180, out var longitude) ||
                string.IsNullOrWhiteSpace(ReadString(place, "display")))
                return await UseFallbackAsync(normalized, fingerprint, searchStatus, candidateCount, placeStatus, "VIETMAP_INVALID_PLACE", cancellationToken);

            var result = new GeocodingResult(latitude, longitude, ReadString(place, "display")!.Trim());
            LogAttempt(fingerprint, searchStatus, candidateCount, placeStatus, "Success", "");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException)
        {
            var reason = ex switch
            {
                TaskCanceledException => "VIETMAP_TIMEOUT",
                HttpRequestException => "VIETMAP_NETWORK_ERROR",
                JsonException when placeStatus.Length > 0 => "VIETMAP_INVALID_PLACE",
                _ => "VIETMAP_EMPTY",
            };
            return await UseFallbackAsync(normalized, fingerprint, searchStatus, candidateCount, placeStatus, reason, cancellationToken);
        }
    }

    private async Task<GeocodingResult?> UseFallbackAsync(string address, string fingerprint, string searchStatus,
        int candidateCount, string placeStatus, string reason, CancellationToken cancellationToken)
    {
        var result = await fallback.GeocodeAsync(address, cancellationToken);
        var outcome = result is not null ? "Fallback" : reason == "VIETMAP_EMPTY" ? "Empty" : "Error";
        LogAttempt(fingerprint, searchStatus, candidateCount, placeStatus, outcome, reason);
        return result;
    }

    private void LogAttempt(string fingerprint, string searchStatus, int candidateCount, string placeStatus, string result, string reason) =>
        logger.LogInformation("Geocoding attempt Provider=VietMap AddressFingerprint={AddressFingerprint} SearchStatus={SearchStatus} CandidateCount={CandidateCount} PlaceStatus={PlaceStatus} Result={Result} FallbackReason={FallbackReason}.",
            fingerprint, searchStatus, candidateCount, placeStatus, result, reason);

    private static string StatusReason(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "VIETMAP_401",
        HttpStatusCode.Locked => "VIETMAP_423",
        >= HttpStatusCode.InternalServerError => "VIETMAP_5XX",
        _ => "VIETMAP_ERROR",
    };

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

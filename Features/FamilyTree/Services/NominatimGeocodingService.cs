using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MyLife.Features.FamilyTree.Services;

public sealed class GeocodingOptions
{
    public string Provider { get; set; } = "Nominatim";
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
    public string UserAgent { get; set; } = "MyLife/1.0 (family-tree-project)";
    public bool Enabled { get; set; } = true;
    public int MinIntervalMilliseconds { get; set; } = 1100;
    public int FailureCacheDays { get; set; } = 30;
    public int MaxQueuePerRequest { get; set; } = 5;
}

public sealed class NominatimGeocodingService(HttpClient client, IOptions<GeocodingOptions> options, ILogger<NominatimGeocodingService> logger, IMemoryCache cache) : IGeocodingService
{
    public async Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !string.Equals(settings.Provider, "Nominatim", StringComparison.OrdinalIgnoreCase)) return null;
        var normalizedAddress = FamilyAddressNormalizer.Normalize(address);
        if (normalizedAddress is null) return null;
        var cacheDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedAddress.ToUpperInvariant())));
        var cacheKey = $"family-nominatim:{cacheDigest}";
        if (cache.TryGetValue<GeocodeCacheValue>(cacheKey, out var cached)) return cached!.Result;

        try
        {
            var baseUrl = settings.BaseUrl.TrimEnd('/');
            var uri = $"{baseUrl}/search?format=jsonv2&limit=1&addressdetails=1&countrycodes=vn&q={Uri.EscapeDataString(normalizedAddress)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.AcceptLanguage.ParseAdd("vi");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Nominatim rate limited the FamilyTree geocoding queue.");
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            if (document.RootElement.GetArrayLength() == 0)
            {
                CacheResult(cacheKey, null, settings.FailureCacheDays);
                return null;
            }
            var first = document.RootElement[0];
            if (!TryCoordinate(first, "lat", -90, 90, out var latitude) || !TryCoordinate(first, "lon", -180, 180, out var longitude)) return null;
            var display = BuildDisplayName(first);
            if (string.IsNullOrWhiteSpace(display)) return null;
            var result = new GeocodingResult(latitude, longitude, display);
            CacheResult(cacheKey, result, 365);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException)
        {
            logger.LogWarning("Nominatim geocoding failed with {ErrorType}.", ex.GetType().Name);
            return null;
        }
    }

    private void CacheResult(string key, GeocodingResult? result, int days) => cache.Set(
        key,
        new GeocodeCacheValue(result),
        new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(Math.Clamp(days, 1, 365)),
            Size = 512,
        });

    private sealed record GeocodeCacheValue(GeocodingResult? Result);

    private static bool TryCoordinate(JsonElement result, string name, double min, double max, out double value)
    {
        value = 0;
        return result.TryGetProperty(name, out var item) &&
            double.TryParse(item.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value >= min && value <= max;
    }

    private static string BuildDisplayName(JsonElement result)
    {
        if (!result.TryGetProperty("address", out var address)) return "";
        string? Get(params string[] names)
        {
            foreach (var name in names)
                if (address.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString();
            return null;
        }
        var parts = new[] { Get("city", "town", "village", "municipality", "county"), Get("state", "province", "region"), Get("country") }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", parts);
    }
}

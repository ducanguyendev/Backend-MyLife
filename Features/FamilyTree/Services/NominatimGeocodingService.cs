using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public int FailureCacheHours { get; set; } = 24;
    public int MaxQueuePerRequest { get; set; } = 5;
    public bool VietMapEnabled { get; set; } = true;
    public string VietMapBaseUrl { get; set; } = "https://maps.vietmap.vn";
    public int VietMapDisplayType { get; set; } = 5;

    public int EffectiveMinIntervalMilliseconds => Math.Max(1000, MinIntervalMilliseconds);
}

public sealed class NominatimGeocodingService(HttpClient client, IOptions<GeocodingOptions> options, ILogger<NominatimGeocodingService> logger, IMemoryCache cache)
{
    public async Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return null;
        var normalizedAddress = FamilyAddressNormalizer.Normalize(address);
        if (normalizedAddress is null) return null;
        var cacheDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedAddress.ToUpperInvariant())));
        var cacheKey = $"family-nominatim:{cacheDigest}";
        if (cache.TryGetValue<GeocodeCacheValue>(cacheKey, out var cached)) return cached!.Result;

        try
        {
            var baseUrl = settings.BaseUrl.TrimEnd('/');
            var uri = $"{baseUrl}/search?format=jsonv2&limit=3&addressdetails=1&countrycodes=vn&q={Uri.EscapeDataString(normalizedAddress)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.AcceptLanguage.ParseAdd("vi");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            logger.LogDebug("Nominatim returned HTTP {StatusCode} for address fingerprint {AddressFingerprint}.", (int)response.StatusCode, cacheDigest[..12]);
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
                CacheResult(cacheKey, null, TimeSpan.FromHours(Math.Clamp(settings.FailureCacheHours, 1, 168)));
                return null;
            }
            var selected = document.RootElement.EnumerateArray()
                .Select(item => CreateCandidate(item, normalizedAddress))
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Importance)
                .ThenByDescending(candidate => candidate.DetailCount)
                .FirstOrDefault();
            if (selected is null)
            {
                CacheResult(cacheKey, null, TimeSpan.FromHours(Math.Clamp(settings.FailureCacheHours, 1, 168)));
                return null;
            }
            var result = new GeocodingResult(selected.Latitude, selected.Longitude, selected.DisplayName);
            logger.LogDebug("Nominatim selected a location for address fingerprint {AddressFingerprint}.", cacheDigest[..12]);
            CacheResult(cacheKey, result, TimeSpan.FromDays(365));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException)
        {
            logger.LogWarning("Nominatim geocoding failed with {ErrorType}.", ex.GetType().Name);
            return null;
        }
    }

    private void CacheResult(string key, GeocodingResult? result, TimeSpan lifetime) => cache.Set(
        key,
        new GeocodeCacheValue(result),
        new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime,
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

    private sealed record GeocodingCandidate(double Latitude, double Longitude, string DisplayName, double Score, double Importance, int DetailCount);

    private static GeocodingCandidate? CreateCandidate(JsonElement result, string query)
    {
        if (!TryCoordinate(result, "lat", -90, 90, out var latitude) || !TryCoordinate(result, "lon", -180, 180, out var longitude)) return null;
        var display = BuildDisplayName(result);
        if (string.IsNullOrWhiteSpace(display)) return null;

        var address = result.TryGetProperty("address", out var addressElement) && addressElement.ValueKind == JsonValueKind.Object
            ? addressElement
            : default;
        var parts = AddressParts(address).ToArray();
        var detailCount = parts.Count(part => part.Key is "house_number" or "road" or "pedestrian" or "footway" or "neighbourhood" or "suburb" or "quarter" or "residential" or "city_district" or "district" or "ward");
        var hasHouseNumber = parts.Any(part => part.Key == "house_number");
        var hasRoad = parts.Any(part => part.Key is "road" or "pedestrian" or "footway" or "residential");
        var detailedQuery = Regex.IsMatch(query, @"(?:^|[\s,])\d+[a-zA-Z]?(?:/\d+[a-zA-Z]?)*", RegexOptions.CultureInvariant);
        var searchableResult = NormalizeForComparison(string.Join(' ', parts.Select(part => part.Value).Append(display)));
        var queryTokens = Regex.Matches(NormalizeForComparison(query), @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Where(token => token.Length > 1)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var matchedTokens = queryTokens.Count(token => searchableResult.Contains(token, StringComparison.Ordinal));
        var importance = ReadNumber(result, "importance");
        var featureType = ReadString(result, "type");
        var featureClass = ReadString(result, "class");
        var specificityBonus = featureType is "house" or "building" or "residential" or "road" or "pedestrian" or "footway" ? 2.5 : 0;
        var residentialClassBonus = featureClass is "building" or "highway" or "place" ? 1 : 0;
        var score = matchedTokens * 1.5 + detailCount * 0.8 + importance * 2 + specificityBonus + residentialClassBonus;
        if (detailedQuery) score += hasHouseNumber ? 8 : -4;
        if (detailedQuery && hasRoad) score += 4;
        return new GeocodingCandidate(latitude, longitude, display, score, importance, detailCount);
    }

    private static string BuildDisplayName(JsonElement result)
    {
        if (!result.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object)
            return ReadString(result, "display_name") ?? "";

        var parts = AddressParts(address)
            .OrderBy(part => DisplayNameOrder(part.Key))
            .Select(part => part.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : ReadString(result, "display_name") ?? "";
    }

    private static IEnumerable<(string Key, string Value)> AddressParts(JsonElement address)
    {
        if (address.ValueKind != JsonValueKind.Object) yield break;
        var priorities = new[]
        {
            "house_number", "road", "pedestrian", "footway", "neighbourhood", "suburb", "quarter", "residential",
            "ward", "city_district", "district", "city", "town", "village", "municipality", "county", "state", "province", "region", "country"
        };
        foreach (var key in priorities)
            if (address.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                yield return (key, value.GetString()!.Trim());
    }

    private static int DisplayNameOrder(string key) => key switch
    {
        "house_number" => 0, "road" => 1, "pedestrian" => 1, "footway" => 1,
        "neighbourhood" => 2, "suburb" => 2, "quarter" => 2, "residential" => 2,
        "ward" => 3, "city_district" or "district" => 4,
        "city" or "town" or "village" or "municipality" or "county" => 5,
        "state" or "province" or "region" => 6,
        "country" => 7,
        _ => 8,
    };

    private static string NormalizeForComparison(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double ReadNumber(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        double.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : 0;
}

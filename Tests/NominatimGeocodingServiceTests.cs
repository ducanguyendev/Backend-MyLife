using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MyLife.Features.FamilyTree.Services;
using Xunit;

public sealed class NominatimGeocodingServiceTests
{
    [Fact]
    public async Task SelectsTheDetailedMatchFromMultipleVietnameseResults()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHandler(_ => Json("[{\"lat\":\"10.8\",\"lon\":\"106.6\",\"importance\":0.8,\"type\":\"city\",\"address\":{\"city\":\"Ho Chi Minh City\",\"state\":\"Ho Chi Minh\",\"country\":\"Vietnam\"}},{\"lat\":\"10.8123456\",\"lon\":\"106.6712345\",\"importance\":0.5,\"type\":\"house\",\"address\":{\"house_number\":\"12\",\"road\":\"Nguyen Van Bao\",\"ward\":\"Ward 4\",\"city_district\":\"Go Vap\",\"city\":\"Ho Chi Minh City\",\"country\":\"Vietnam\"}}]"));
        var service = Create(handler, cache);

        const string exactVietnameseAddress = "12 Nguy\u1ec5n V\u0103n B\u1ea3o, G\u00f2 V\u1ea5p, Ho Chi Minh City";
        var result = await service.GeocodeAsync(exactVietnameseAddress, CancellationToken.None);

        Assert.Equal(new GeocodingResult(10.8123456, 106.6712345, "12, Nguyen Van Bao, Ward 4, Go Vap, Ho Chi Minh City, Vietnam"), result);
        Assert.Contains("format=jsonv2", handler.RequestUri!.Query);
        Assert.Contains("limit=3", handler.RequestUri.Query);
        Assert.Contains("addressdetails=1", handler.RequestUri.Query);
        Assert.Contains("countrycodes=vn", handler.RequestUri.Query);
        Assert.Contains(exactVietnameseAddress, Uri.UnescapeDataString(handler.RequestUri.Query));
        Assert.Contains("vi", handler.AcceptLanguage);
    }

    [Fact]
    public async Task DifferentAddressDoesNotReuseFailureCachedForPreviousAddress()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHandler(request => Uri.UnescapeDataString(request.RequestUri!.Query).Contains("old address", StringComparison.OrdinalIgnoreCase)
            ? Json("[]")
            : Json("[{\"lat\":\"10.123456\",\"lon\":\"106.123456\",\"address\":{\"road\":\"New Road\",\"city\":\"Ho Chi Minh City\",\"country\":\"Vietnam\"}}]"));
        var service = Create(handler, cache);

        Assert.Null(await service.GeocodeAsync("Old address", CancellationToken.None));
        var updated = await service.GeocodeAsync("New Road, Ho Chi Minh City", CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(10.123456, updated.Latitude);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task FailureCacheExpiresAfterConfiguredLifetime()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var handler = new StubHandler(_ => Json("[]"));
        var service = new NominatimGeocodingService(
            new HttpClient(handler),
            Options.Create(new GeocodingOptions { BaseUrl = "https://nominatim.test", FailureCacheHours = 1 }),
            NullLogger<NominatimGeocodingService>.Instance,
            cache);

        Assert.Null(await service.GeocodeAsync("Address that may later resolve", CancellationToken.None));
        Assert.Null(await service.GeocodeAsync("Address that may later resolve", CancellationToken.None));
        Assert.Equal(1, handler.Calls);

        clock.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));
        Assert.Null(await service.GeocodeAsync("Address that may later resolve", CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public void RequestIntervalUsesConfiguredValueWithOneSecondSafetyFloor()
    {
        Assert.Equal(1100, new GeocodingOptions { MinIntervalMilliseconds = 1100 }.EffectiveMinIntervalMilliseconds);
        Assert.Equal(1000, new GeocodingOptions { MinIntervalMilliseconds = 250 }.EffectiveMinIntervalMilliseconds);
    }

    [Fact]
    public async Task EmptyResultIsCachedByNormalizedCaseInsensitiveAddress()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHandler(_ => Json("[]"));
        var service = Create(handler, cache);

        Assert.Null(await service.GeocodeAsync("  Gò   Vấp ", CancellationToken.None));
        Assert.Null(await service.GeocodeAsync("gò vấp", CancellationToken.None));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TimeoutAndMalformedResponseFailWithoutThrowingOrPoisoningCache()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        var service = Create(handler, cache);
        Assert.Null(await service.GeocodeAsync("Same address", CancellationToken.None));

        handler.Response = _ => Json("{malformed");
        Assert.Null(await service.GeocodeAsync("Same address", CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task NonSuccessAndInvalidCoordinatesAreNotExposedAsLocations()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var service = Create(handler, cache);
        Assert.Null(await service.GeocodeAsync("Address", CancellationToken.None));

        handler.Response = _ => Json("[{\"lat\":\"999\",\"lon\":\"106\",\"address\":{\"city\":\"City\"}}]");
        Assert.Null(await service.GeocodeAsync("Address", CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    private static NominatimGeocodingService Create(StubHandler handler, IMemoryCache cache) => new(
        new HttpClient(handler),
        Options.Create(new GeocodingOptions { BaseUrl = "https://nominatim.test", FailureCacheHours = 24 }),
        NullLogger<NominatimGeocodingService>.Instance,
        cache);

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Response { get; set; } = response;
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string AcceptLanguage { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            AcceptLanguage = request.Headers.AcceptLanguage.ToString();
            return Task.FromResult(Response(request));
        }
    }

    private sealed class ManualClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.UtcNow;
        public void Advance(TimeSpan duration) => UtcNow += duration;
    }
}

using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MyLife.Features.FamilyTree.Services;
using Xunit;

public sealed class NominatimGeocodingServiceTests
{
    [Fact]
    public async Task SendsVietnameseNominatimRequestAndMapsPublicFields()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new StubHandler(_ => Json("[{\"lat\":\"10.812\",\"lon\":\"106.671\",\"address\":{\"suburb\":\"Ward 5\",\"city\":\"Ho Chi Minh City\",\"state\":\"Ho Chi Minh\",\"country\":\"Vietnam\"}}]"));
        var service = Create(handler, cache);

        var result = await service.GeocodeAsync("  12   Nguyen Van Bao, Go Vap  ", CancellationToken.None);

        Assert.Equal(new GeocodingResult(10.812, 106.671, "Ho Chi Minh City, Ho Chi Minh, Vietnam"), result);
        Assert.Contains("format=jsonv2", handler.RequestUri!.Query);
        Assert.Contains("limit=1", handler.RequestUri.Query);
        Assert.Contains("countrycodes=vn", handler.RequestUri.Query);
        Assert.Contains("12 Nguyen Van Bao, Go Vap", Uri.UnescapeDataString(handler.RequestUri.Query));
        Assert.Contains("vi", handler.AcceptLanguage);
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
        Options.Create(new GeocodingOptions { BaseUrl = "https://nominatim.test", FailureCacheDays = 30 }),
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
}

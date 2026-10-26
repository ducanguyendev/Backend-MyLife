using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyLife.Features.FamilyTree.Services;
using Xunit;

public sealed class VietMapGeocodingServiceTests
{
    [Fact]
    public async Task AutocompleteAndPlaceSuccessDoesNotCallNominatim()
    {
        var vietMap = new StubHandler((request, _) =>
        {
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            if (request.RequestUri.AbsolutePath.EndsWith("autocomplete/v4", StringComparison.Ordinal))
            {
                Assert.Contains("display_type=5", query);
                return Json("[{\"ref_id\":\"geocode:abc\",\"display\":\"12 Nguyen Trai\"}]");
            }
            return Json("{\"lat\":10.75,\"lng\":106.7,\"display\":\"12 Nguyen Trai, Ho Chi Minh City\"}");
        });
        var nominatim = new StubHandler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected fallback"));
        var service = Create(vietMap, nominatim, "private-test-key");

        var result = await service.GeocodeAsync("12 Nguyen Trai", CancellationToken.None);

        Assert.Equal(10.75, result?.Latitude);
        Assert.Equal(106.7, result?.Longitude);
        Assert.Equal("12 Nguyen Trai, Ho Chi Minh City", result?.DisplayName);
        Assert.Equal(2, vietMap.Calls);
        Assert.Equal(0, nominatim.Calls);
        Assert.DoesNotContain("private-test-key", string.Join(' ', vietMap.Requests.Select(uri => uri.AbsolutePath)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Locked)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task VietMapHttpFailureFallsBack(HttpStatusCode status)
    {
        var service = Create(new StubHandler((_, _) => new HttpResponseMessage(status)),
            NominatimSuccess(), "private-test-key");
        var result = await service.GeocodeAsync("Some address", CancellationToken.None);
        Assert.Equal(10.8, result?.Latitude);
        Assert.Equal(106.6, result?.Longitude);
        Assert.Equal("Ho Chi Minh City", result?.DisplayName);
    }

    [Fact]
    public async Task EmptySearchFallsBack()
    {
        var vietMap = new StubHandler((_, _) => Json("[]"));
        var nominatim = NominatimSuccess();
        var result = await Create(vietMap, nominatim, "key").GeocodeAsync("Some address", CancellationToken.None);
        Assert.Equal(10.8, result?.Latitude);
        Assert.Equal(106.6, result?.Longitude);
        Assert.Equal(1, vietMap.Calls);
        Assert.Equal(1, nominatim.Calls);
    }

    [Fact]
    public async Task InvalidPlaceCoordinatesFallBack()
    {
        var vietMap = new StubHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("autocomplete/v4", StringComparison.Ordinal)
            ? Json("[{\"ref_id\":\"auto:abc\"}]") : Json("{\"lat\":999,\"lng\":106,\"display\":\"invalid\"}"));
        var nominatim = NominatimSuccess();
        var result = await Create(vietMap, nominatim, "key").GeocodeAsync("Some address", CancellationToken.None);
        Assert.Equal("Ho Chi Minh City", result?.DisplayName);
        Assert.Equal(2, vietMap.Calls);
        Assert.Equal(1, nominatim.Calls);
    }

    [Fact]
    public async Task MissingApiKeyUsesFallback()
    {
        var vietMap = new StubHandler((_, _) => throw new Xunit.Sdk.XunitException("Should not call VIETMAP"));
        var nominatim = NominatimSuccess();
        var result = await Create(vietMap, nominatim, null).GeocodeAsync("Some address", CancellationToken.None);
        Assert.Equal("Ho Chi Minh City", result?.DisplayName);
        Assert.Equal(0, vietMap.Calls);
        Assert.Equal(1, nominatim.Calls);
    }

    [Fact]
    public async Task TimeoutFallsBackWithoutLoggingApiKey()
    {
        var vietMap = new StubHandler((_, _) => throw new TaskCanceledException("timeout"));
        var nominatim = NominatimSuccess();
        var logger = new RecordingLogger<VietMapGeocodingService>();
        var service = Create(vietMap, nominatim, "private-test-key", logger);
        Assert.Equal("Ho Chi Minh City", (await service.GeocodeAsync("Some address", CancellationToken.None))?.DisplayName);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("private-test-key", StringComparison.Ordinal));
    }

    private static VietMapGeocodingService Create(StubHandler vietMap, StubHandler nominatim, string? key,
        ILogger<VietMapGeocodingService>? logger = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VietMap:ApiKey"] = key,
        }).Build();
        var clients = new StubClientFactory(vietMap, nominatim);
        var fallback = new NominatimGeocodingService(clients.CreateClient("nominatim"),
            Options.Create(new GeocodingOptions { BaseUrl = "https://nominatim.test" }),
            NullLogger<NominatimGeocodingService>.Instance,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        return new VietMapGeocodingService(clients,
            Options.Create(new GeocodingOptions { VietMapBaseUrl = "https://vietmap.test" }), config, fallback,
            logger ?? NullLogger<VietMapGeocodingService>.Instance);
    }

    private static StubHandler NominatimSuccess() => new((_, _) => Json("[{\"lat\":\"10.8\",\"lon\":\"106.6\",\"display_name\":\"Ho Chi Minh City\",\"importance\":0.5,\"address\":{\"city\":\"Ho Chi Minh City\"}}]"));
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private sealed class StubClientFactory(StubHandler vietMap, StubHandler nominatim) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(name == "nominatim" ? nominatim : vietMap);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response(request, cancellationToken));
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}

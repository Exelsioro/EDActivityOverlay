using System.Net;
using System.Net.Http;
using System.Text;
using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationDataProviderTests
{
    [Fact]
    public async Task SpanshProviderParsesValuesAndJournalBodyId()
    {
        const long address = 10477373803;
        long bodyAddress = address + (4L << 55);
        string json = $$$"""
            {"record":{"id64":{{{address}}},"name":"Test System","body_count":2,"estimated_scan_value":1200,"estimated_mapping_value":230000,"updated_at":"2026-08-20T10:00:00Z","bodies":[
              {"id64":{{{address}}},"name":"Test System","type":"Star","subtype":"G Star","estimated_scan_value":1200},
              {"id64":{{{bodyAddress}}},"name":"Test System 4","type":"Planet","subtype":"Water world","distance_to_arrival":420.5,"estimated_scan_value":25000,"estimated_mapping_value":230000,"terraforming_state":"Terraformable"}
            ]}}
            """;
        using var client = new HttpClient(new JsonHandler(_ => json));
        var provider = new SpanshExplorationProvider(client);

        ExplorationSystemDataSnapshot? result = await provider.GetSystemAsync(address, "Test System", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Spansh", result!.Source);
        Assert.Equal(2, result.BodyCount);
        Assert.Equal(230000, result.EstimatedMappingValue);
        ExternalExplorationBodySnapshot planet = Assert.Single(result.Bodies, body => body.Type == "Planet");
        Assert.Equal(4, planet.BodyId);
        Assert.Equal(230000, planet.EstimatedMappingValue);
        Assert.Equal(420.5, planet.DistanceFromArrivalLs);
    }

    [Fact]
    public async Task EdsmProviderCombinesBodiesWithEstimatedValues()
    {
        string bodies = """{"id64":123,"name":"Fallback","bodyCount":1,"bodies":[{"bodyId":7,"name":"Fallback 7","type":"Planet","subType":"Earth-like world","distanceToArrival":800,"isLandable":false,"gravity":1.02,"updateTime":"2026-08-19 10:00:00"}]}""";
        string values = """{"estimatedValue":250000,"estimatedValueMapped":900000,"valuableBodies":[{"bodyName":"Fallback 7","valueMax":900000}]}""";
        using var client = new HttpClient(new JsonHandler(uri => uri.Contains("estimated-value") ? values : bodies));
        var provider = new EdsmExplorationProvider(client);

        ExplorationSystemDataSnapshot? result = await provider.GetSystemAsync(123, "Fallback", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("EDSM", result!.Source);
        Assert.Equal(900000, Assert.Single(result.Bodies).EstimatedMappingValue);
        Assert.Equal(250000, result.EstimatedScanValue);
    }

    [Fact]
    public async Task EdsmProviderCalculatesMissingPerBodyValuesLocally()
    {
        string bodies = """{"id64":123,"name":"Fallback","bodyCount":1,"bodies":[{"bodyId":7,"name":"Fallback 7","type":"Planet","subType":"Water world","earthMasses":1.0,"terraformingState":"Terraformable"}]}""";
        using var client = new HttpClient(new JsonHandler(uri => uri.Contains("estimated-value") ? "{}" : bodies));
        var provider = new EdsmExplorationProvider(client);

        ExplorationSystemDataSnapshot? result = await provider.GetSystemAsync(123, "Fallback", CancellationToken.None);

        ExternalExplorationBodySnapshot body = Assert.Single(result!.Bodies);
        Assert.True(body.ValuesCalculatedLocally);
        Assert.Equal(1, body.EarthMasses);
        Assert.True(body.EstimatedScanValue > 0);
        Assert.True(body.EstimatedMappingValue > body.EstimatedScanValue);
        Assert.Equal(body.EstimatedScanValue, result.EstimatedScanValue);
    }

    [Fact]
    public async Task LoaderUsesFallbackAndPersistsSuccessfulResult()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ed-overlay-system-cache-{Guid.NewGuid():N}");
        try
        {
            var expected = SystemSnapshot("EDSM", DateTimeOffset.UtcNow);
            var loader = new ExplorationDataLoader(
                new ExplorationSystemCache(directory),
                new IExplorationSystemProvider[] { new StubProvider("Spansh", null), new StubProvider("EDSM", expected) });

            ExplorationSystemDataSnapshot? loaded = await loader.LoadAsync(123, "Fallback", TimeSpan.FromDays(7), true, false, CancellationToken.None);
            ExplorationSystemDataSnapshot? cached = await new ExplorationSystemCache(directory).LoadAsync(
                123, "Fallback", TimeSpan.FromDays(7), false, CancellationToken.None);

            Assert.Equal("EDSM", loaded?.Source);
            Assert.True(cached?.FromCache);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task LoaderReturnsStaleCacheWhenProvidersFail()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ed-overlay-system-cache-{Guid.NewGuid():N}");
        try
        {
            var cache = new ExplorationSystemCache(directory);
            await cache.SaveAsync(SystemSnapshot("Spansh", DateTimeOffset.UtcNow.AddDays(-30)), CancellationToken.None);
            var loader = new ExplorationDataLoader(cache, new IExplorationSystemProvider[] { new StubProvider("Spansh", null) });

            ExplorationSystemDataSnapshot? loaded = await loader.LoadAsync(
                123, "Fallback", TimeSpan.FromDays(7), false, false, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.True(loaded!.FromCache);
            Assert.True(loaded.IsStale);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ForcedRefreshBypassesFreshCache()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ed-overlay-system-cache-{Guid.NewGuid():N}");
        try
        {
            var cache = new ExplorationSystemCache(directory);
            await cache.SaveAsync(SystemSnapshot("Cached", DateTimeOffset.UtcNow), CancellationToken.None);
            var loader = new ExplorationDataLoader(
                cache,
                new IExplorationSystemProvider[] { new StubProvider("Spansh", SystemSnapshot("Network", DateTimeOffset.UtcNow)) });

            ExplorationSystemDataSnapshot? loaded = await loader.LoadAsync(
                123, "Fallback", TimeSpan.FromDays(7), false, true, CancellationToken.None);

            Assert.Equal("Network", loaded?.Source);
            Assert.False(loaded?.FromCache);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static ExplorationSystemDataSnapshot SystemSnapshot(string source, DateTimeOffset fetched) => new(
        123, "Fallback", source, fetched, fetched, false, false, 1, 10, 20,
        null, null, null, false, Array.Empty<ExternalExplorationBodySnapshot>());

    private sealed class StubProvider(string name, ExplorationSystemDataSnapshot? result) : IExplorationSystemProvider
    {
        public string Name { get; } = name;
        public Task<ExplorationSystemDataSnapshot?> GetSystemAsync(long systemAddress, string systemName, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class JsonHandler(Func<string, string> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseFactory(request.RequestUri!.AbsoluteUri), Encoding.UTF8, "application/json")
            });
    }
}

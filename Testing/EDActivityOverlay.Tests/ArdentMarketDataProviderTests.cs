using System.Net;
using System.Text;
using EDActivityOverlay.Services.Ardent;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ArdentMarketDataProviderTests
{
    [Fact]
    public async Task ExportMappingUsesBuyPriceAsCommanderPurchasePrice()
    {
        using var http = new HttpClient(new FakeHandler())
        {
            BaseAddress = new Uri("https://api.ardent-insight.com/")
        };

        var provider = new ArdentMarketDataProvider(
            new ArdentApiClient(http, new ArdentRequestCache(), 1));

        TradeSystemLocation origin = await provider.ResolveSystemAsync(
            new TradeSystemReference("Origin"));

        IReadOnlyList<TradeMarketOrder> orders = await provider.GetNearbyExportsAsync(
            origin,
            "gold",
            30,
            new TradeSearchConstraints
            {
                OriginSystemName = "Origin",
                CargoCapacity = 10,
                SourceSearchRadiusLy = 30,
                TargetSearchRadiusLy = 60,
                MaxDataAge = TimeSpan.FromDays(3),
                MinLandingPadSize = 1
            });

        TradeMarketOrder order = Assert.Single(orders);
        Assert.Equal(1_234, order.BuyFromStationPrice);
        Assert.Equal(999, order.SellToStationPrice);
        Assert.Equal(42, order.SystemAddress);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.PathAndQuery ?? string.Empty;
            string json = path.Contains("/system/name/Origin", StringComparison.Ordinal)
                ? """
                  {"systemAddress":1,"systemName":"Origin","systemX":0,"systemY":0,"systemZ":0}
                  """
                : """
                  [{"commodityName":"gold","marketId":10,"stationName":"Test Station","stationType":"Coriolis","distanceToArrival":500,"maxLandingPadSize":3,"systemAddress":42,"systemName":"Supplier","systemX":20,"systemY":0,"systemZ":0,"buyPrice":1234,"sellPrice":999,"demand":0,"stock":500,"updatedAt":"2026-08-29T01:00:00Z","distance":20}]
                  """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}

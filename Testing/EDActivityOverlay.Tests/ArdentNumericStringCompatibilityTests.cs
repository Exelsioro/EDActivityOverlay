using System.Net;
using System.Net.Http;
using System.Text;
using EDActivityOverlay.Services.Ardent;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ArdentNumericStringCompatibilityTests
{
    [Fact]
    public async Task SystemCommodityPayloadAcceptsQuotedNumericMarketFields()
    {
        const string json = """
            [
              {
                "commodityName": "gold",
                "marketId": "128106744",
                "buyPrice": "4000",
                "sellPrice": "5000",
                "stock": "750",
                "demand": "900",
                "systemAddress": "10477373803"
              }
            ]
            """;

        using var http = new HttpClient(new StaticJsonHandler(json))
        {
            BaseAddress = new Uri("https://api.ardent-insight.com/")
        };

        var client = new ArdentApiClient(http);
        IReadOnlyList<ArdentMarketOrderDto> rows =
            await client.GetSystemCommoditiesAsync(10477373803);

        ArdentMarketOrderDto row = Assert.Single(rows);
        Assert.Equal(128106744, row.MarketId);
        Assert.Equal(4000, row.BuyPrice);
        Assert.Equal(5000, row.SellPrice);
        Assert.Equal(750, row.Stock);
        Assert.Equal(900, row.Demand);
        Assert.Equal(10477373803, row.SystemAddress);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"unknown\"")]
    [InlineData("{}")]
    public async Task InvalidMarketRowDoesNotDiscardValidRows(string invalidId)
    {
        string json = "[{\"marketId\":" + invalidId + "},{\"marketId\":42,\"commodityName\":\"gold\"}]";
        using var http = new HttpClient(new StaticJsonHandler(json));
        var rows = await new ArdentApiClient(http).GetSystemCommoditiesAsync(1);
        Assert.Equal(42, Assert.Single(rows).MarketId);
    }

    [Fact]
    public async Task FullyInvalidResponseDoesNotPoisonCache()
    {
        var handler = new RecoveringHandler();
        using var http = new HttpClient(handler);
        var client = new ArdentApiClient(http);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => client.GetSystemCommoditiesAsync(1));
        Assert.Equal(42, Assert.Single(await client.GetSystemCommoditiesAsync(1)).MarketId);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task BulkMarketSkipsNullStationJoinRowsAndRetainsCache()
    {
        const string json = """
            [{"commodityName":null,"marketId":null,"stationName":"Clauss Point",
              "stationType":"CraterOutpost","buyPrice":null,"stock":null},
             {"commodityName":"gold","marketId":42,"buyPrice":4000,"stock":100}]
            """;
        using var http = new HttpClient(new StaticJsonHandler(json));
        var cache = new ArdentRequestCache();
        var client = new ArdentApiClient(http, cache);
        Assert.Equal(42, Assert.Single(await client.GetSystemCommoditiesAsync(1)).MarketId);
        Assert.True(cache.TryGet("v2/system/address/1/commodities", out _));
    }

    private sealed class RecoveringHandler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(++Requests == 1 ? "[{\"marketId\":null}]" : "[{\"marketId\":42}]")
            });
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
    }
}

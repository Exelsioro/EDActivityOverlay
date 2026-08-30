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

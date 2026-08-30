using System.Text.Json;
using EDActivityOverlay.Services.Ardent;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ArdentCommodityReportNullabilityTests
{
    [Fact]
    public void CommodityReportAcceptsNullAggregateFields()
    {
        const string json =
            """
            [
              {
                "commodityName": "mystery",
                "minBuyPrice": null,
                "maxSellPrice": null,
                "totalStock": null,
                "totalDemand": null
              },
              {
                "commodityName": "gold",
                "minBuyPrice": 3979,
                "maxSellPrice": 70761,
                "totalStock": 73016533,
                "totalDemand": 1899662825
              }
            ]
            """;

        ArdentCommodityReportDto[] rows =
            JsonSerializer.Deserialize<ArdentCommodityReportDto[]>(
                json)
            ?? [];

        Assert.Equal(
            2,
            rows.Length);

        Assert.Equal(
            0,
            rows[0].MinBuyPrice);

        Assert.Equal(
            0,
            rows[0].MaxSellPrice);

        Assert.Equal(
            0,
            rows[0].TotalStock);

        Assert.Equal(
            0,
            rows[0].TotalDemand);

        Assert.Equal(
            3_979,
            rows[1].MinBuyPrice);

        Assert.Equal(
            70_761,
            rows[1].MaxSellPrice);

        Assert.Equal(
            73_016_533,
            rows[1].TotalStock);

        Assert.Equal(
            1_899_662_825,
            rows[1].TotalDemand);
    }
}

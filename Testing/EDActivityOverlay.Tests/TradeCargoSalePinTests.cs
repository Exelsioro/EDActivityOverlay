using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeCargoSalePinTests
{
    [Fact]
    public void MixedCargoSaleMapsToSaleOnlyPinnedRoute()
    {
        var candidate =
            new CargoSaleCandidate
            {
                Target =
                    new TradeMarketOrder
                    {
                        MarketId = 9001,
                        SystemAddress = 44,
                        SystemName = "Target System",
                        StationName = "Target Station",
                        StationType = "Coriolis Starport",
                        MaxLandingPadSize = 3,
                        DistanceToArrivalLs = 602,
                        SellToStationPrice = 788_579,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        ReferenceDistanceLy = 54
                    },
                Lines =
                [
                    new CargoSaleLine
                    {
                        CommodityId = "monazite",
                        DisplayName = "Monazite",
                        CargoAmount = 129,
                        SellAmount = 129,
                        SellPrice = 788_579,
                        Revenue = 101_726_691
                    },
                    new CargoSaleLine
                    {
                        CommodityId = "serendibite",
                        DisplayName = "Serendibite",
                        CargoAmount = 9,
                        SellAmount = 9,
                        SellPrice = 92_527,
                        Revenue = 832_743
                    }
                ],
                TotalCargoUnits = 138,
                SellableUnits = 138,
                TotalRevenue = 102_559_434,
                WorstDataAge = TimeSpan.FromMinutes(4)
            };

        var state =
            new GameStateSnapshot
            {
                StarSystem = "Wolf 1241",
                SystemAddress = 12,
                Station = "Origin Station",
                MarketId = 42
            };

        var route =
            TradeRoutePresentationAdapter.ToPresentation(
                candidate,
                state);

        Assert.True(
            route.IsCargoSaleOnly);
        Assert.False(
            route.IsRoundTrip);
        Assert.Equal(
            138,
            route.CargoCapacity);
        Assert.Equal(
            102_559_434,
            route.PlannedSaleValue);
        Assert.Equal(
            "Wolf 1241",
            route.CardHeader.FromStation.System);
        Assert.Equal(
            "Target System",
            route.CardHeader.ToStation.System);
        Assert.Equal(
            2,
            route.CargoSaleItems.Count);
        Assert.Equal(
            129,
            route.CargoSaleItems[0].Quantity);
        Assert.Equal(
            "serendibite",
            route.CargoSaleItems[1].InternalName);
    }
}

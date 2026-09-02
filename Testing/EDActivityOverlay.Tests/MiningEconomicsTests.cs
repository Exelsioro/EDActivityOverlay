using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningEconomicsTests
{
    [Fact]
    public void CargoAndRefinedValueUseMarketPriceButNotProspectPercent()
    {
        DateTimeOffset started =
            new(2026, 9, 2, 17, 0, 0, TimeSpan.Zero);

        var session =
            MiningSessionSnapshot.Empty with
            {
                SessionId = Guid.NewGuid(),
                State = MiningSessionState.Active,
                StartedUtc = started,
                LastActivityUtc = started.AddMinutes(30),
                Refinements =
                [
                    new MiningRefinementSnapshot(
                        1,
                        started.AddMinutes(5),
                        "Platinum",
                        "Platinum"),
                    new MiningRefinementSnapshot(
                        2,
                        started.AddMinutes(6),
                        "Platinum",
                        "Platinum")
                ]
            };

        var state =
            new GameStateSnapshot
            {
                CargoByCommodityId =
                    new Dictionary<string, CargoCommoditySnapshot>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["Platinum"] =
                            new(
                                "Platinum",
                                "Platinum",
                                10)
                    }
            };

        var quote =
            new MiningMarketPriceQuote(
                "Platinum",
                60_000,
                70_000,
                80_000,
                3,
                started);

        var prices =
            new MiningMarketPriceSnapshot(
                1,
                "Test",
                started,
                false,
                string.Empty,
                new Dictionary<string, MiningMarketPriceQuote>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["Platinum"] = quote
                });

        MiningEconomicsSnapshot result =
            MiningEconomicsCalculator.Calculate(
                session,
                state,
                prices,
                started.AddMinutes(30));

        Assert.Equal(
            10,
            result.PricedCargoTons);
        Assert.Equal(
            700_000,
            result.EstimatedCargoValue);
        Assert.Equal(
            2,
            result.PricedRefinedTons);
        Assert.Equal(
            140_000,
            result.EstimatedSessionValue);
        Assert.Equal(
            280_000,
            result.EstimatedCreditsPerHour);
    }

    [Fact]
    public void SessionCarriesPersistentRingContextWithoutChangingLegacyConstructor()
    {
        MiningSessionSnapshot session =
            MiningSessionSnapshot.Empty with
            {
                RingClass = "eRingClass_Metalic",
                ReserveLevel = "PristineResources",
                HotspotCommodityIds =
                [
                    "Platinum",
                    "Painite"
                ]
            };

        Assert.Equal(
            "eRingClass_Metalic",
            session.RingClass);
        Assert.Equal(
            "PristineResources",
            session.ReserveLevel);
        Assert.Equal(
            2,
            session.HotspotCommodityIds.Count);
    }
}

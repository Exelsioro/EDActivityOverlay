using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Trading;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV9bActiveHudTests
{
    [Fact]
    public void SessionMovesFromSupplierToLoadedCargoToBuyer()
    {
        TradeRouteCandidate route =
            Route();

        var constraints =
            Constraints();

        GameStateSnapshot initial =
            Snapshot();

        var session =
            new TradeActiveRouteSession(
                route,
                constraints,
                initial);

        Assert.Equal(
            TradeActiveStage.TravellingToSource,
            session.Stage);

        GameStateSnapshot atSource =
            initial with
            {
                Docked = true,
                MarketSnapshotId = 10,
                MarketUpdatedUtc = DateTimeOffset.UtcNow,
                MarketByCommodityId =
                    Market(
                        "gold",
                        buy: 1_050,
                        sell: 0,
                        supply: 1_000,
                        demand: 0)
            };

        session.Update(
            atSource);

        Assert.Equal(
            TradeActiveStage.AtSource,
            session.Stage);

        GameStateSnapshot loaded =
            atSource with
            {
                CargoByCommodityId =
                    Cargo(
                        "gold",
                        100)
            };

        session.Update(
            loaded);

        Assert.True(
            session.CargoLoaded);

        GameStateSnapshot travelling =
            loaded with
            {
                Docked = false,
                MarketSnapshotId = null
            };

        session.Update(
            travelling);

        Assert.Equal(
            TradeActiveStage.TravellingToTarget,
            session.Stage);

        GameStateSnapshot atTarget =
            loaded with
            {
                Docked = true,
                MarketSnapshotId = 20,
                MarketUpdatedUtc = DateTimeOffset.UtcNow,
                MarketByCommodityId =
                    Market(
                        "gold",
                        buy: 0,
                        sell: 2_000,
                        supply: 0,
                        demand: 1_000)
            };

        session.Update(
            atTarget);

        Assert.Equal(
            TradeActiveStage.AtTarget,
            session.Stage);

        GameStateSnapshot sold =
            atTarget with
            {
                CargoByCommodityId =
                    Cargo(
                        "gold",
                        0)
            };

        session.Update(
            sold);

        Assert.Equal(
            TradeActiveStage.Completed,
            session.Stage);
    }

    [Fact]
    public void TargetPriceDropOffersReroute()
    {
        TradeRouteCandidate route =
            Route();

        var session =
            new TradeActiveRouteSession(
                route,
                Constraints(),
                Snapshot());

        GameStateSnapshot atSource =
            Snapshot() with
            {
                Docked = true,
                MarketSnapshotId = 10
            };

        session.Update(
            atSource);

        session.Update(
            atSource with
            {
                CargoByCommodityId =
                    Cargo(
                        "gold",
                        100)
            });

        DateTimeOffset marketUpdate =
            DateTimeOffset.UtcNow;

        session.Update(
            Snapshot() with
            {
                Docked = true,
                MarketSnapshotId = 20,
                MarketUpdatedUtc = marketUpdate,
                CargoByCommodityId =
                    Cargo(
                        "gold",
                        100),
                MarketByCommodityId =
                    Market(
                        "gold",
                        buy: 0,
                        sell: 1_700,
                        supply: 0,
                        demand: 1_000)
            });

        Assert.True(
            session.TargetDegraded);

        Assert.True(
            session.ShouldOfferReroute);

        session.AcknowledgeTargetDegradation();

        Assert.False(
            session.ShouldOfferReroute);
    }

    [Fact]
    public void PresentationCarriesStableIdsWithoutChangingCardSurface()
    {
        TradeRouteCandidate candidate =
            Route();

        var presentation =
            TradeRoutePresentationAdapter.ToPresentation(
                candidate);

        Assert.Equal(
            10,
            presentation.CardHeader.FromStation.MarketId);

        Assert.Equal(
            20,
            presentation.CardHeader.ToStation.MarketId);

        Assert.Equal(
            "gold",
            presentation.FirstRoute.BuyCommodity.InternalName);

        Assert.Equal(
            "gold",
            presentation.FirstRoute.SellCommodity.InternalName);
    }

    [Fact]
    public void CompactHudContainsDedicatedSellCargoButton()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        Assert.Contains(
            "x:Name=\"CompactSellCargoButton\"",
            xaml,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "RoutesList_PreviewMouseLeftButtonUp",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "SelectionChanged=\"RoutesList_SelectionChanged\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TradePinKeepsWorkspaceAlive()
    {
        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Trade.cs");

        Assert.Contains(
            "keepTradeWorkspace:",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ActivatePinnedRoute",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullTradeSuppressesPinnedRouteCard()
    {
        string host =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "ActivityWorkspaceOverlayWindow.Trade.cs");

        string main =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "MainWindow.OverlayOrchestration.cs");

        string pinned =
            ReadProjectFile(
                "EDActivityOverlay",
                "Windows",
                "PinnedRouteOverlay.xaml.cs");

        Assert.Contains(
            "SetPinnedRouteSuppressedByTradeWorkspace",
            host,
            StringComparison.Ordinal);

        Assert.Contains(
            "pinnedRouteSuppressedByTradeWorkspace",
            main,
            StringComparison.Ordinal);

        Assert.Contains(
            "SetSuppressedByTradeWorkspace",
            pinned,
            StringComparison.Ordinal);

        Assert.Contains(
            "if (suppressedByTradeWorkspace)",
            pinned,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitPinButtonRemainsThePinEntryPoint()
    {
        string xaml =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml");

        string code =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        Assert.Contains(
            "Click=\"PinRouteButton_Click\"",
            xaml,
            StringComparison.Ordinal);

        Assert.Contains(
            "private void PinRouteButton_Click(",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "PreviewMouseLeftButtonUp=\"RoutesList_PreviewMouseLeftButtonUp\"",
            xaml,
            StringComparison.Ordinal);
    }
    private static TradeRouteCandidate Route() =>
        new()
        {
            Source =
                Order(
                    10,
                    "Source",
                    0,
                    1_000,
                    0),
            Target =
                Order(
                    20,
                    "Target",
                    30,
                    0,
                    2_000),
            ProfitPerTon =
                1_000,
            TradableAmount =
                100,
            ProfitPerTrip =
                100_000,
            OriginToSourceDistanceLy =
                10,
            SourceToTargetDistanceLy =
                30,
            SourceAge =
                TimeSpan.FromMinutes(
                    10),
            TargetAge =
                TimeSpan.FromMinutes(
                    10)
        };

    private static TradeMarketOrder Order(
        long marketId,
        string station,
        double x,
        int buy,
        int sell) =>
        new()
        {
            CommodityName =
                "gold",
            MarketId =
                marketId,
            StationName =
                station,
            StationType =
                "Coriolis",
            DistanceToArrivalLs =
                500,
            MaxLandingPadSize =
                3,
            SystemAddress =
                marketId,
            SystemName =
                $"{station} System",
            SystemX =
                x,
            SystemY =
                0,
            SystemZ =
                0,
            BuyFromStationPrice =
                buy,
            SellToStationPrice =
                sell,
            Stock =
                10_000,
            Demand =
                10_000,
            UpdatedAt =
                DateTimeOffset.UtcNow
                - TimeSpan.FromMinutes(
                    10)
        };

    private static TradeSearchConstraints Constraints() =>
        new()
        {
            OriginSystemName =
                "Origin",
            CargoCapacity =
                100,
            SourceSearchRadiusLy =
                30,
            TargetSearchRadiusLy =
                80,
            MaxDataAge =
                TimeSpan.FromDays(
                    3),
            MinLandingPadSize =
                1,
            MinSupply =
                1,
            MinDemand =
                1
        };

    private static GameStateSnapshot Snapshot() =>
        new()
        {
            JournalAvailable =
                true,
            StarSystem =
                "Origin",
            SystemAddress =
                1,
            SystemX =
                0,
            SystemY =
                0,
            SystemZ =
                0,
            MaxJumpRangeLy =
                30,
            UnladenMassTonnes =
                300,
            CargoByCommodityId =
                Cargo(
                    "gold",
                    0)
        };

    private static IReadOnlyDictionary<string, CargoCommoditySnapshot> Cargo(
        string commodity,
        int count)
    {
        var result =
            new Dictionary<string, CargoCommoditySnapshot>(
                StringComparer.OrdinalIgnoreCase);

        if (count > 0)
        {
            result[commodity] =
                new CargoCommoditySnapshot(
                    commodity,
                    commodity,
                    count);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, MarketItemSnapshot> Market(
        string commodity,
        int buy,
        int sell,
        int supply,
        int demand) =>
        new Dictionary<string, MarketItemSnapshot>(
            StringComparer.OrdinalIgnoreCase)
        {
            [commodity] =
                new MarketItemSnapshot(
                    commodity,
                    buy,
                    sell,
                    supply,
                    demand)
        };

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            string candidate =
                Path.Combine(
                    [
                        directory.FullName,
                        .. relative
                    ]);

            if (File.Exists(
                    candidate))
            {
                return File.ReadAllText(
                    candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}

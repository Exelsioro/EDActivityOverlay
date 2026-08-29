using System;
using System.IO;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class NavigationExplorationDisplayRegressionTests
{
    [Fact]
    public void ShipStatusAnchorsNextSystemAfterCurrentRouteEntry()
    {
        var state =
            new GameStateSnapshot
            {
                StarSystem =
                    "Current",
                CurrentStarClass =
                    "G",
                FuelMain =
                    28,
                FuelCapacityMain =
                    32,
                NavRoute =
                [
                    new NavRouteStar(
                        "Already Visited",
                        "K"),
                    new NavRouteStar(
                        "Current",
                        "G"),
                    new NavRouteStar(
                        "Actual Next",
                        "M"),
                    new NavRouteStar(
                        "Destination",
                        "F")
                ]
            };

        ShipStatusPresentation view =
            ShipStatusPresentationBuilder.Build(
                state);

        Assert.Equal(
            "Actual Next",
            view.NextSystem);

        Assert.Equal(
            2,
            view.RemainingJumps);

        Assert.True(
            view.NextStarScoopable);
    }

    [Fact]
    public void RouteResolverFallsBackToFirstEntryWhenLiveSystemIsTemporarilyUnknown()
    {
        var state =
            new GameStateSnapshot
            {
                NavRoute =
                [
                    new NavRouteStar(
                        "A",
                        "L"),
                    new NavRouteStar(
                        "B",
                        "K")
                ]
            };

        NavRouteProgress progress =
            NavRouteProgressResolver.Resolve(
                state);

        Assert.Equal(
            "A",
            progress.Current?.System);

        Assert.Equal(
            "B",
            progress.Next?.System);

        Assert.Equal(
            1,
            progress.RemainingJumps);
    }

    [Fact]
    public void RouteResolverRejectsKnownCurrentSystemThatIsAbsentFromRoute()
    {
        var state =
            new GameStateSnapshot
            {
                StarSystem =
                    "Different",
                NavRoute =
                [
                    new NavRouteStar(
                        "A",
                        "L"),
                    new NavRouteStar(
                        "B",
                        "K")
                ]
            };

        NavRouteProgress progress =
            NavRouteProgressResolver.Resolve(
                state);

        Assert.Empty(
            progress.RouteFromCurrent);

        Assert.Null(
            progress.Next);
    }

    [Fact]
    public void FuelAdvisorIgnoresAlreadyVisitedRoutePrefix()
    {
        var state =
            new GameStateSnapshot
            {
                StarSystem =
                    "Current",
                FuelMain =
                    28,
                FuelCapacityMain =
                    32,
                FuelPerLightYearEstimate =
                    0.1,
                NavRoute =
                [
                    new NavRouteStar(
                        "Already Visited",
                        "K",
                        0,
                        0,
                        0),
                    new NavRouteStar(
                        "Current",
                        "G",
                        10,
                        0,
                        0),
                    new NavRouteStar(
                        "Actual Next",
                        "M",
                        20,
                        0,
                        0),
                    new NavRouteStar(
                        "Destination",
                        "L",
                        30,
                        0,
                        0)
                ]
            };

        FuelRouteAssessment assessment =
            FuelRouteAdvisor.Evaluate(
                state);

        Assert.Equal(
            2,
            assessment.RemainingJumps);

        Assert.Equal(
            1,
            assessment.JumpsToNextScoopable);

        Assert.Equal(
            "Actual Next",
            assessment.NextScoopableSystem);
    }

    [Fact]
    public void MappingPlanningValueUsesEfficiencyEstimateConsistently()
    {
        ExplorationBodySnapshot body =
            Body(
                mapped: false,
                efficient: false);

        Assert.Equal(
            125_000,
            ExplorationPresentationValueResolver.ResolveMappingEstimate(
                body));

        var state =
            new GameStateSnapshot
            {
                StarSystem =
                    "Test",
                SystemAddress =
                    42,
                ExplorationBodies =
                [
                    body
                ]
            };

        ExplorationSystemCatalog catalog =
            ExplorationSystemCatalogBuilder.Build(
                state,
                ExplorationDataState.Idle,
                ExplorationSpoilerModes.EnrichScanned,
                ExplorationSystemHistorySnapshot.Empty);

        Assert.Equal(
            125_000,
            Assert.Single(
                    catalog.Bodies)
                .EstimatedMappingValue);
    }

    [Fact]
    public void CurrentVisitValueDoesNotDoubleCountScanAndMapping()
    {
        ExplorationBodySnapshot inefficient =
            Body(
                mapped: true,
                efficient: false);

        ExplorationBodySnapshot efficient =
            Body(
                mapped: true,
                efficient: true);

        Assert.Equal(
            100_000,
            ExplorationPresentationValueResolver.ResolveCurrentVisitValue(
                inefficient));

        Assert.Equal(
            125_000,
            ExplorationPresentationValueResolver.ResolveCurrentVisitValue(
                efficient));
    }

    [Fact]
    public void WorkspacePreservesCatalogSelectionAndSuppressesPassiveCursor()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.xaml.cs"));

        Assert.Contains(
            "previousSelection",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "preservedSelection",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ForceCursor",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "IsHitTestVisible",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "Cursors.None",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ResolveCurrentVisitValue",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VisitStateSignatureTracksAllValueAndPresentationInputs()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Services",
                    "Exploration",
                    "ExplorationVisitStateService.cs"));

        Assert.Contains(
            "body.EstimatedEfficientMappingValue",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "body.EstimatedScanValue",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "body.WasDiscovered",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "body.WasMapped",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "body.DistanceFromArrivalLs",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DssPresentationUsesSamePlanningMappingValueResolver()
    {
        string code =
            File.ReadAllText(
                FindProjectFile(
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.Dss.cs"));

        Assert.Contains(
            "ExplorationPresentationValueResolver",
            code,
            StringComparison.Ordinal);

        Assert.Contains(
            "ResolveMappingEstimate",
            code,
            StringComparison.Ordinal);
    }

    private static ExplorationBodySnapshot Body(
        bool mapped,
        bool efficient) =>
        new(
            7,
            "Test 7",
            "Water world",
            1_000,
            false,
            false,
            mapped,
            efficient,
            0,
            Array.Empty<string>(),
            ExplorationInterest.WaterWorld)
        {
            IsScanned =
                true,
            BodyType =
                "Planet",
            BodyClass =
                "Water world",
            EstimatedScanValue =
                40_000,
            EstimatedMappingValue =
                100_000,
            EstimatedEfficientMappingValue =
                125_000
        };

    private static string FindProjectFile(
        params string[] relative)
    {
        for (
            DirectoryInfo? directory =
                new(
                    AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
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
                return candidate;
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}

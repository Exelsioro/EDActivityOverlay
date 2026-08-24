using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationVisitQueueTests
{
    [Fact]
    public void CompletedInterestingBodyIsRemovedFromRecommendedQueue()
    {
        var engine = new ExplorationVisitQueueEngine();

        GameStateSnapshot state = State(
            "Test",
            42,
            BioBody(
                4,
                "Test 4",
                mapped: false,
                completedGenuses: 1),
            ValuableBody(
                5,
                "Test 5",
                mapped: true));

        ExplorationSystemCatalog catalog = Catalog(
            "Test",
            BioCatalogBody(4, "Test 4"),
            ValuableCatalogBody(5, "Test 5"));

        ExplorationVisitQueueSnapshot queue = engine.Update(
            state,
            catalog,
            ExplorationSystemHistorySnapshot.Empty);

        ExplorationVisitBodyState remaining =
            Assert.Single(queue.Recommended);

        Assert.Equal(4, remaining.BodyId);
        Assert.Equal(1, remaining.Progress.RemainingBiologicalSignals);

        ExplorationVisitBodyState completed =
            Assert.Single(queue.Completed);

        Assert.Equal(5, completed.BodyId);
        Assert.True(completed.IsComplete);
    }

    [Fact]
    public void SwitchingActiveBodyDefersPreviousIncompleteBody()
    {
        var engine = new ExplorationVisitQueueEngine();

        GameStateSnapshot state = State(
            "Test",
            42,
            BioBody(
                4,
                "Test 4",
                mapped: true,
                completedGenuses: 1),
            ValuableBody(
                6,
                "Test 6",
                mapped: false));

        ExplorationSystemCatalog catalog = Catalog(
            "Test",
            BioCatalogBody(4, "Test 4"),
            ValuableCatalogBody(6, "Test 6"));

        engine.Update(
            state,
            catalog,
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.ActivateBody(4));
        Assert.Equal(4, engine.Current.Active?.BodyId);

        Assert.True(engine.ActivateBody(6));

        Assert.Equal(6, engine.Current.Active?.BodyId);
        Assert.Equal(
            new[] { 4 },
            engine.Current.Deferred.Select(item => item.BodyId));
        Assert.DoesNotContain(
            engine.Current.Recommended,
            item => item.BodyId == 4);
    }

    [Fact]
    public void ManualDeferAndResumeDoNotChangeResearchFacts()
    {
        var engine = new ExplorationVisitQueueEngine();

        GameStateSnapshot state = State(
            "Test",
            42,
            BioBody(
                4,
                "Test 4",
                mapped: true,
                completedGenuses: 1));

        ExplorationSystemCatalog catalog = Catalog(
            "Test",
            BioCatalogBody(4, "Test 4"));

        engine.Update(
            state,
            catalog,
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.ActivateBody(4));

        ExplorationVisitBodyState active =
            engine.Current.Active
            ?? throw new Xunit.Sdk.XunitException("Expected active body.");

        BodyExplorationProgress before = active.Progress;

        Assert.True(engine.DeferBody(4));

        ExplorationVisitBodyState deferred =
            Assert.Single(engine.Current.Deferred);

        Assert.Equal(before, deferred.Progress);
        Assert.Null(engine.Current.Active);

        Assert.True(engine.ResumeBody(4));

        ExplorationVisitBodyState resumed =
            Assert.Single(engine.Current.Recommended);

        Assert.Equal(before, resumed.Progress);
        Assert.Empty(engine.Current.Deferred);
    }

    [Fact]
    public void DeferredStateIsClearedWhenEnteringAnotherSystem()
    {
        var engine = new ExplorationVisitQueueEngine();

        engine.Update(
            State(
                "System A",
                100,
                ValuableBody(
                    4,
                    "System A 4",
                    mapped: false)),
            Catalog(
                "System A",
                ValuableCatalogBody(
                    4,
                    "System A 4")),
            ExplorationSystemHistorySnapshot.Empty);

        Assert.True(engine.DeferBody(4));
        Assert.Single(engine.Current.Deferred);

        ExplorationVisitQueueSnapshot next = engine.Update(
            State(
                "System B",
                200,
                ValuableBody(
                    4,
                    "System B 4",
                    mapped: false)),
            Catalog(
                "System B",
                ValuableCatalogBody(
                    4,
                    "System B 4")),
            ExplorationSystemHistorySnapshot.Empty);

        Assert.Empty(next.Deferred);
        Assert.Single(next.Recommended);
        Assert.Equal("System B", next.SystemName);
    }

    [Fact]
    public void BiologyRequiresFssDssAndAllBiologicalSignals()
    {
        ExplorationCatalogBody catalogBody =
            BioCatalogBody(4, "Test 4");

        ExplorationRequiredObjectives objectives =
            ExplorationVisitPolicy.RequiredObjectives(catalogBody);

        Assert.True(
            objectives.HasFlag(
                ExplorationRequiredObjectives.FssScan));
        Assert.True(
            objectives.HasFlag(
                ExplorationRequiredObjectives.DssMap));
        Assert.True(
            objectives.HasFlag(
                ExplorationRequiredObjectives.Biology));
    }

    [Fact]
    public void OrdinaryLandableBodyIsNotAutomaticallyRecommended()
    {
        ExplorationCatalogBody ordinary = MakeCatalogBody(
            9,
            "Test 9",
            ExplorationBodyHighlights.Landable,
            mappingValue: 0);

        Assert.False(
            ExplorationVisitPolicy.IsInteresting(ordinary));
    }

    [Fact]
    public void DestinationMustRemainStableBeforeServiceActivation()
    {
        Assert.Equal(
            1_200,
            ExplorationVisitStateService
                .DestinationStabilityMilliseconds);
    }

    private static GameStateSnapshot State(
        string system,
        long address,
        params ExplorationBodySnapshot[] bodies)
    {
        OrganicScanProgressSnapshot[] organics = bodies
            .SelectMany(body =>
            {
                int completed = body.BodyId == 4
                    ? Math.Min(
                        body.BiologicalSignals,
                        body.Genuses.Count == 0 ? 0 : 1)
                    : 0;

                return Enumerable.Range(0, completed)
                    .Select(index =>
                        new OrganicScanProgressSnapshot(
                            "Cmdr",
                            address,
                            system,
                            body.BodyId,
                            body.Name,
                            body.Genuses[index],
                            body.Genuses[index] + " species",
                            string.Empty,
                            3,
                            true,
                            500,
                            null,
                            null,
                            DateTimeOffset.Parse(
                                "2026-08-22T12:00:00Z")));
            })
            .ToArray();

        return new GameStateSnapshot
        {
            Commander = "Cmdr",
            StarSystem = system,
            SystemAddress = address,
            ExplorationBodies = bodies,
            OrganicProgress = organics
        };
    }

    private static ExplorationBodySnapshot BioBody(
        int id,
        string name,
        bool mapped,
        int completedGenuses)
    {
        string[] genuses =
        [
            "Stratum",
            "Bacterium"
        ];

        return new ExplorationBodySnapshot(
            id,
            name,
            "Rocky body",
            800,
            false,
            false,
            mapped,
            mapped,
            2,
            genuses,
            ExplorationInterest.None)
        {
            IsScanned = true,
            BodyType = "Planet",
            BodyClass = "Rocky body",
            Landable = true
        };
    }

    private static ExplorationBodySnapshot ValuableBody(
        int id,
        string name,
        bool mapped) =>
        new(
            id,
            name,
            "Water world",
            1_200,
            false,
            false,
            mapped,
            mapped,
            0,
            Array.Empty<string>(),
            ExplorationInterest.WaterWorld)
        {
            IsScanned = true,
            BodyType = "Planet",
            BodyClass = "Water world",
            EstimatedMappingValue = 350_000
        };

    private static ExplorationSystemCatalog Catalog(
        string system,
        params ExplorationCatalogBody[] bodies) =>
        new(
            system,
            bodies.Length,
            ExplorationSpoilerModes.EnrichScanned,
            bodies);

    private static ExplorationCatalogBody BioCatalogBody(
        int id,
        string name) =>
        MakeCatalogBody(
            id,
            name,
            ExplorationBodyHighlights.Biological
            | ExplorationBodyHighlights.Landable,
            mappingValue: 100_000);

    private static ExplorationCatalogBody ValuableCatalogBody(
        int id,
        string name) =>
        MakeCatalogBody(
            id,
            name,
            ExplorationBodyHighlights.WaterWorld
            | ExplorationBodyHighlights.Valuable,
            mappingValue: 350_000);

    private static ExplorationCatalogBody MakeCatalogBody(
        int id,
        string name,
        ExplorationBodyHighlights highlights,
        long mappingValue) =>
        new(
            id,
            name,
            "Planet",
            highlights.HasFlag(
                ExplorationBodyHighlights.WaterWorld)
                ? "Water world"
                : "Rocky body",
            800,
            highlights.HasFlag(
                ExplorationBodyHighlights.Landable),
            0.2,
            250,
            "Thin atmosphere",
            string.Empty,
            highlights.HasFlag(
                ExplorationBodyHighlights.Terraformable),
            100_000,
            mappingValue,
            true,
            false,
            false,
            false,
            false,
            false,
            0,
            false,
            false,
            highlights.HasFlag(
                ExplorationBodyHighlights.Biological)
                ? 2
                : 0,
            highlights.HasFlag(
                ExplorationBodyHighlights.Biological)
                ? new[] { "Stratum", "Bacterium" }
                : Array.Empty<string>(),
            highlights,
            "Journal");
}
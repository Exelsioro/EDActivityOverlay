using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationSystemCatalogBuilderTests
{
    [Fact]
    public void JournalOnlyDoesNotRevealExternalBodies()
    {
        ExplorationSystemCatalog catalog = ExplorationSystemCatalogBuilder.Build(
            JournalState(), ExternalState(), ExplorationSpoilerModes.JournalOnly);

        ExplorationCatalogBody row = Assert.Single(catalog.Bodies);
        Assert.Equal("Test 2", row.Name);
        Assert.Equal("Journal", row.Source);
        Assert.Equal(string.Empty, row.Atmosphere);
    }

    [Fact]
    public void EnrichScannedFillsKnownBodyButDoesNotRevealOthers()
    {
        ExplorationSystemCatalog catalog = ExplorationSystemCatalogBuilder.Build(
            JournalState(), ExternalState(), ExplorationSpoilerModes.EnrichScanned);

        ExplorationCatalogBody row = Assert.Single(catalog.Bodies);
        Assert.Equal("Thin carbon dioxide", row.Atmosphere);
        Assert.Equal("Journal + Spansh", row.Source);
        Assert.True(row.ScannedThisVisit);
    }

    [Fact]
    public void FullCatalogAddsExternalBodiesWithoutClaimingPersonalProgress()
    {
        ExplorationSystemCatalog catalog = ExplorationSystemCatalogBuilder.Build(
            JournalState(), ExternalState(), ExplorationSpoilerModes.FullCatalog);

        Assert.Equal(2, catalog.Bodies.Count);
        ExplorationCatalogBody external = Assert.Single(catalog.Bodies, body => body.Name == "Test 3");
        Assert.False(external.ScannedThisVisit);
        Assert.False(external.MappedThisVisit);
        Assert.True(external.IsValuable);
        Assert.True(external.Highlights.HasFlag(ExplorationBodyHighlights.EarthLike));
    }

    private static GameStateSnapshot JournalState() => new()
    {
        StarSystem = "Test",
        SystemBodyCount = 3,
        ExplorationBodies =
        [
            new ExplorationBodySnapshot(
                2, "Test 2", "High metal content world", 120, false, false,
                false, false, 0, Array.Empty<string>(), ExplorationInterest.None)
            {
                BodyType = "Planet",
                BodyClass = "High metal content world",
                IsScanned = true,
                EstimatedEfficientMappingValue = 450_000
            }
        ]
    };

    private static ExplorationDataState ExternalState()
    {
        ExternalExplorationBodySnapshot[] bodies =
        [
            new(2, "Test 2", "Planet", "High metal content world", 120, true,
                0.2, 190, "Thin carbon dioxide", "None", "Terraformable", 50_000, 450_000, 0),
            new(3, "Test 3", "Planet", "Earth-like world", 600, false,
                1, 280, "Suitable for water-based life", "None", "", 650_000, 2_500_000, 0)
        ];
        return new ExplorationDataState(
            ExplorationDataStatus.Available,
            new ExplorationSystemDataSnapshot(
                42, "Test", "Spansh", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                false, false, 3, 700_000, 2_950_000, 0, 0, 0, false, bodies),
            string.Empty);
    }
}

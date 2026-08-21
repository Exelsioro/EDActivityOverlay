using ED_Inara_Overlay.Services.Exploration;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationEarningsTests
{
    [Fact]
    public void MappingReplacesScanEstimateAndSaleClearsUniversalCartographicsLedger()
    {
        string[] scans =
        [
            """{"timestamp":"2026-01-01T00:00:00Z","event":"Location","StarSystem":"Test","SystemAddress":42}""",
            """{"timestamp":"2026-01-01T00:01:00Z","event":"Scan","BodyID":2,"BodyName":"Test 2","PlanetClass":"Water world","MassEM":1,"WasDiscovered":false,"WasMapped":false}""",
            """{"timestamp":"2026-01-01T00:02:00Z","event":"SAAScanComplete","BodyID":2,"BodyName":"Test 2","ProbesUsed":5,"EfficiencyTarget":6}"""
        ];

        var beforeSale = ExplorationEarningsService.CalculateForJournalLines(scans);
        var estimate = ExplorationValueCalculator.Estimate("Planet", "Water world", false, 1, null);
        Assert.Equal(estimate.FirstDiscoveredAndMappedEfficientValue, beforeSale.UniversalCartographicsEstimate);

        var afterSale = ExplorationEarningsService.CalculateForJournalLines(scans.Append(
            """{"timestamp":"2026-01-01T00:03:00Z","event":"SellExplorationData","TotalEarnings":1000000}"""));
        Assert.Equal(0, afterSale.UniversalCartographicsEstimate);
        Assert.NotNull(afterSale.LastUniversalCartographicsSaleUtc);
    }

    [Fact]
    public void OrganicEstimateIsTrackedUntilVistaGenomicsSale()
    {
        string scan = """{"timestamp":"2026-01-01T00:01:00Z","event":"ScanOrganic","ScanType":"Analyse","Body":2,"Genus":"$Codex_Ent_Stratum_Genus_Name;","Genus_Localised":"Stratum","Species":"species"}""";

        var beforeSale = ExplorationEarningsService.CalculateForJournalLines([scan]);
        Assert.True(beforeSale.ExobiologyMinimumEstimate > 0);
        Assert.True(beforeSale.ExobiologyMaximumEstimate >= beforeSale.ExobiologyMinimumEstimate);

        var afterSale = ExplorationEarningsService.CalculateForJournalLines([
            scan,
            """{"timestamp":"2026-01-01T00:02:00Z","event":"SellOrganicData","BioData":[]}"""]);
        Assert.Equal(0, afterSale.ExobiologyMinimumEstimate);
        Assert.Equal(0, afterSale.ExobiologyMaximumEstimate);
    }
}

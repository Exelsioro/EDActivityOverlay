using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

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

    [Fact]
    public void MultiSellRemovesOnlyListedSystems()
    {
        var lines = new[]
        {
            "{\"event\":\"LoadGame\",\"Commander\":\"Cmdr\"}",
            "{\"event\":\"Location\",\"StarSystem\":\"Alpha\",\"SystemAddress\":1}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Alpha 1\",\"PlanetClass\":\"Water world\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
            "{\"event\":\"Location\",\"StarSystem\":\"Beta\",\"SystemAddress\":2}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Beta 1\",\"PlanetClass\":\"Water world\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
            "{\"event\":\"MultiSellExplorationData\",\"Discovered\":[{\"SystemName\":\"Alpha\",\"NumBodies\":1}]}",
        };

        var state = ExplorationEarningsService.CalculateForJournalLines(lines);
        var betaOnly = ExplorationEarningsService.CalculateForJournalLines(lines.Skip(4).Take(1));
        Assert.Equal(betaOnly.UniversalCartographicsEstimate, state.UniversalCartographicsEstimate);
    }

    [Fact]
    public void OrganicSaleRemovesOnlyMatchingCodexEntries()
    {
        var state = ExplorationEarningsService.CalculateForJournalLines(new[]
        {
            "{\"event\":\"Location\",\"StarSystem\":\"Alpha\",\"SystemAddress\":1}",
            "{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Body\":1,\"Genus\":\"$Codex_Ent_Stratum_Genus_Name;\",\"Species\":\"$Codex_Ent_Stratum_01_Name;\",\"Variant\":\"$Codex_Ent_Stratum_01_A_Name;\"}",
            "{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Body\":2,\"Genus\":\"$Codex_Ent_Bacterial_Genus_Name;\",\"Species\":\"$Codex_Ent_Bacterial_01_Name;\",\"Variant\":\"$Codex_Ent_Bacterial_01_A_Name;\"}",
            "{\"event\":\"SellOrganicData\",\"BioData\":[{\"Genus\":\"$Codex_Ent_Stratum_Genus_Name;\",\"Species\":\"$Codex_Ent_Stratum_01_Name;\",\"Variant\":\"$Codex_Ent_Stratum_01_A_Name;\"}]}",
        });

        var bacterialOnly = ExplorationEarningsService.CalculateForJournalLines(new[]
        {
            "{\"event\":\"Location\",\"StarSystem\":\"Alpha\",\"SystemAddress\":1}",
            "{\"event\":\"ScanOrganic\",\"ScanType\":\"Analyse\",\"Body\":2,\"Genus\":\"$Codex_Ent_Bacterial_Genus_Name;\",\"Species\":\"$Codex_Ent_Bacterial_01_Name;\",\"Variant\":\"$Codex_Ent_Bacterial_01_A_Name;\"}",
        });
        Assert.Equal(bacterialOnly.ExobiologyMinimumEstimate, state.ExobiologyMinimumEstimate);
    }

    [Fact]
    public void ScanAfterMappingDoesNotLowerEstimatedValue()
    {
        var state = ExplorationEarningsService.CalculateForJournalLines(new[]
        {
            "{\"event\":\"Location\",\"StarSystem\":\"Alpha\",\"SystemAddress\":1}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Alpha 1\",\"PlanetClass\":\"Water world\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
            "{\"event\":\"SAAScanComplete\",\"BodyID\":1,\"BodyName\":\"Alpha 1\",\"ProbesUsed\":5,\"EfficiencyTarget\":6}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Alpha 1\",\"PlanetClass\":\"Water world\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
        });
        var mapped = ExplorationEarningsService.CalculateForJournalLines(new[]
        {
            "{\"event\":\"Location\",\"StarSystem\":\"Alpha\",\"SystemAddress\":1}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Alpha 1\",\"PlanetClass\":\"Water world\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
            "{\"event\":\"SAAScanComplete\",\"BodyID\":1,\"BodyName\":\"Alpha 1\",\"ProbesUsed\":5,\"EfficiencyTarget\":6}",
        });
        Assert.Equal(mapped.UniversalCartographicsEstimate, state.UniversalCartographicsEstimate);
    }

    [Fact]
    public void EarningsLedgerResetsWhenCommanderChanges()
    {
        var state = ExplorationEarningsService.CalculateForJournalLines(new[]
        {
            "{\"event\":\"LoadGame\",\"Commander\":\"First\"}",
            "{\"event\":\"Location\",\"StarSystem\":\"Alpha\",\"SystemAddress\":1}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Alpha 1\",\"PlanetClass\":\"Water world\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
            "{\"event\":\"LoadGame\",\"Commander\":\"Second\"}",
            "{\"event\":\"Location\",\"StarSystem\":\"Beta\",\"SystemAddress\":2}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Beta 1\",\"PlanetClass\":\"Rocky body\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
        });
        var secondOnly = ExplorationEarningsService.CalculateForJournalLines(new[]
        {
            "{\"event\":\"LoadGame\",\"Commander\":\"Second\"}",
            "{\"event\":\"Location\",\"StarSystem\":\"Beta\",\"SystemAddress\":2}",
            "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Beta 1\",\"PlanetClass\":\"Rocky body\",\"WasDiscovered\":false,\"WasMapped\":false,\"MassEM\":1}",
        });
        Assert.Equal(secondOnly.UniversalCartographicsEstimate, state.UniversalCartographicsEstimate);
    }
}

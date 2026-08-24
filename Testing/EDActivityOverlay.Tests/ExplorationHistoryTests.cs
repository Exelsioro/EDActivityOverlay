using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationHistoryTests
{
    [Fact]
    public async Task HistoricalImporterBuildsIdempotentPersonalBodyState()
    {
        string root = Path.Combine(Path.GetTempPath(), "ed-overlay-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string database = Path.Combine(root, "history.db");
        string historical = Path.Combine(root, "Journal.2026-01-01T000000.01.log");
        string current = Path.Combine(root, "Journal.2026-01-02T000000.01.log");
        try
        {
            File.WriteAllLines(historical,
            [
                "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"event\":\"LoadGame\",\"Commander\":\"Test Cmdr\"}",
                "{\"timestamp\":\"2026-01-01T00:01:00Z\",\"event\":\"Location\",\"StarSystem\":\"Test System\",\"SystemAddress\":42}",
                "{\"timestamp\":\"2026-01-01T00:02:00Z\",\"event\":\"Scan\",\"BodyID\":2,\"BodyName\":\"Test System 2\",\"PlanetClass\":\"Water world\",\"WasDiscovered\":false,\"WasMapped\":false}",
                "{\"timestamp\":\"2026-01-01T00:03:00Z\",\"event\":\"SAAScanComplete\",\"BodyID\":2,\"BodyName\":\"Test System 2\",\"ProbesUsed\":4,\"EfficiencyTarget\":6}",
                "{\"timestamp\":\"2026-01-01T00:04:00Z\",\"event\":\"SAASignalsFound\",\"BodyID\":2,\"BodyName\":\"Test System 2\",\"Signals\":[{\"Type\":\"$SAA_SignalType_Biological;\",\"Type_Localised\":\"Biological\",\"Count\":2}]}",
                "{\"timestamp\":\"2026-01-01T00:05:00Z\",\"event\":\"ScanOrganic\",\"Body\":2,\"ScanType\":\"Analyse\",\"Species\":\"$Codex_Ent_Stratum_01_Name;\",\"Species_Localised\":\"Stratum Tectonicas\"}"
            ]);
            File.WriteAllText(current, "{\"event\":\"Fileheader\"}\n");
            File.SetLastWriteTimeUtc(historical, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(current, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            var repository = new ExplorationHistoryRepository(database);
            var importer = new ExplorationJournalImporter(repository);
            ExplorationHistoryImportState final = ExplorationHistoryImportState.Idle;
            await importer.ImportAsync(root, value => final = value, CancellationToken.None);
            await importer.ImportAsync(root, value => final = value, CancellationToken.None);

            ExplorationSystemHistorySnapshot system = repository.LoadSystem("Test Cmdr", 42, "Test System");
            ExplorationHistoryBodySnapshot body = Assert.Single(system.Bodies);
            Assert.True(system.WasVisited);
            Assert.True(body.Scanned);
            Assert.True(body.Mapped);
            Assert.True(body.EfficientlyMapped);
            Assert.True(body.FirstDiscovered);
            Assert.True(body.FirstMapped);
            Assert.Equal(2, body.BiologicalSignals);
            Assert.Equal(1, body.CompletedOrganics);
            Assert.False(final.IsRunning);
            Assert.Equal(1, final.ProcessedFiles);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CatalogCarriesHistoricalProgressWithoutClaimingCurrentVisit()
    {
        GameStateSnapshot game = new() { Commander = "Test", StarSystem = "S", SystemAddress = 7 };
        ExplorationSystemHistorySnapshot history = new(
            "Test", 7, "S", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [new ExplorationHistoryBodySnapshot(
                1, "S 1", "Water world", true, true, true, true, true, 0, 0,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]);
        ExternalExplorationBodySnapshot externalBody = new(
            1, "S 1", "Planet", "Water world", 10, false, 1, 280,
            "", "", "", 100, 1_000_000, 0);
        ExplorationDataState external = new(
            ExplorationDataStatus.Available,
            new ExplorationSystemDataSnapshot(
                7, "S", "Spansh", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                false, false, 1, 100, 1_000_000, 0, 0, 0, false, [externalBody]),
            string.Empty);

        ExplorationCatalogBody row = Assert.Single(ExplorationSystemCatalogBuilder.Build(
            game, external, ExplorationSpoilerModes.FullCatalog, history).Bodies);

        Assert.True(row.ScannedPreviously);
        Assert.True(row.MappedPreviously);
        Assert.False(row.ScannedThisVisit);
        Assert.False(row.MappedThisVisit);
    }
}

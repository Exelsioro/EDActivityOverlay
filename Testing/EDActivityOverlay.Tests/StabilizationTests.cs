using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Ardent;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
using EDActivityOverlay.Services.Exploration;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class StabilizationTests
{
    [Fact]
    public void RouteVerificationRequiresNewDestinationNotOldIntermediateStar()
    {
        var state = GameStateSnapshot.Empty with
        {
            NavRouteRevision = 2,
            NavRoute = [new("Origin", "K"), new("Target", "G"), new("Other", "M")]
        };
        Assert.False(EliteRouteNavigationService.IsNewRouteToTarget(state, "Target", 1));
        state = state with { NavRoute = [new("Origin", "K"), new("Target", "G")] };
        Assert.False(EliteRouteNavigationService.IsNewRouteToTarget(state, "Target", 2));
        Assert.True(EliteRouteNavigationService.IsNewRouteToTarget(state, "target", 1));
    }

    [Fact]
    public void OnlyNavRouteReadsAdvanceRouteRevision()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyNavRouteJson("{\"Route\":[{\"StarSystem\":\"Target\",\"StarClass\":\"G\"}]}");
        Assert.Equal(1, reducer.Current.NavRouteRevision);
        reducer.ApplyStatusJson("{\"Flags\":0,\"Flags2\":0}");
        Assert.Equal(1, reducer.Current.NavRouteRevision);
        reducer.ApplyNavRouteJson("{\"Route\":[]}");
        Assert.Equal(2, reducer.Current.NavRouteRevision);
        Assert.False(EliteRouteNavigationService.IsNewRouteToTarget(reducer.Current, "Target", 1));
    }

    [Fact]
    public void CacheBoundsUniqueSearchesAndKeepsRecentEntry()
    {
        var cache = new ArdentRequestCache();
        for (int i = 0; i < 150; i++) cache.Set("system-" + i, "[]", TimeSpan.FromMinutes(2));
        Assert.True(cache.TryGet("system-149", out _));
        Assert.InRange(Enumerable.Range(0, 150).Count(i => cache.TryGet("system-" + i, out _)), 1, 128);
    }

    [Fact]
    public void AtomicReplacementKeepsBackupAndPreservesOriginalWhenLocked()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EDActivityOverlay.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            AtomicFileStorage.WriteAllText(path, "old");
            AtomicFileStorage.WriteAllText(path, "current");
            Assert.Equal("old", File.ReadAllText(path + ".bak"));
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => AtomicFileStorage.WriteAllText(path, "failed"));
            Assert.Equal("current", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MissingDiscoveryFlagsRemainUnknownUntilJournalStatesThem()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""
            {"event":"Location","StarSystem":"Alpha","SystemAddress":1}
            """);
        reducer.ApplyJournalLine("""
            {"event":"Scan","BodyID":1,"BodyName":"Alpha 1","PlanetClass":"Water world"}
            """);
        ExplorationBodySnapshot unknown = Assert.Single(reducer.Current.ExplorationBodies);
        Assert.False(unknown.DiscoveryStatusKnown);
        Assert.False(unknown.MappingStatusKnown);
        Assert.False(unknown.WasDiscovered);
        Assert.False(unknown.WasMapped);

        reducer.ApplyJournalLine("""
            {"event":"Scan","BodyID":1,"BodyName":"Alpha 1","PlanetClass":"Water world","WasDiscovered":false,"WasMapped":true}
            """);
        ExplorationBodySnapshot known = Assert.Single(reducer.Current.ExplorationBodies);
        Assert.True(known.DiscoveryStatusKnown);
        Assert.True(known.MappingStatusKnown);
        Assert.False(known.WasDiscovered);
        Assert.True(known.WasMapped);
    }

    [Fact]
    public void FsdTargetIsMarkedAsSystemAndDoesNotReuseStaleAddress()
    {
        var reducer = new JournalStateReducer();
        reducer.ApplyJournalLine("""
            {"event":"FSDTarget","Name":"Target System","SystemAddress":42}
            """);
        Assert.True(reducer.Current.DestinationIsSystemTarget);
        Assert.Equal(42, reducer.Current.DestinationSystemAddress);

        reducer.ApplyJournalLine("""
            {"event":"FSDTarget","Name":"Another System"}
            """);
        Assert.True(reducer.Current.DestinationIsSystemTarget);
        Assert.Equal(0, reducer.Current.DestinationSystemAddress);
    }

    [Fact]
    public void BootstrapAccumulatorRestoresCommanderAndCurrentJournalContext()
    {
        string file = Path.Combine(Path.GetTempPath(), "ed-overlay-bootstrap-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var repository = new ExplorationHistoryRepository(file);
            var accumulator = new ExplorationHistoryAccumulator(repository);
            foreach (string line in new[]
            {
                "{\"event\":\"LoadGame\",\"Commander\":\"Cmdr\"}",
                "{\"event\":\"Location\",\"StarSystem\":\"Current\",\"SystemAddress\":9}",
                "{\"event\":\"Scan\",\"BodyID\":1,\"BodyName\":\"Current 1\",\"PlanetClass\":\"Water world\"}"
            })
            {
                using JsonDocument document = JsonDocument.Parse(line);
                accumulator.Apply(document.RootElement);
            }

            ExplorationSystemHistorySnapshot history = repository.LoadSystem("Cmdr", 9, "Current");
            Assert.True(history.WasVisited);
            Assert.Single(history.Bodies);
            Assert.False(history.Bodies[0].FirstDiscovered);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public void SchemaThreeReindexKeepsCanonicalBiologyHistory()
    {
        string file = Path.Combine(Path.GetTempPath(), "ed-overlay-schema3-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var initial = new ExplorationHistoryRepository(file);
            initial.RecordOrganic("Cmdr", 9, "Current", 1, "Current 1",
                "$Codex_Ent_Stratum_01_Name;", "Stratum Tectonicas", true,
                DateTimeOffset.UtcNow,
                "$Codex_Ent_Stratum_Genus_Name;", "Stratum");
            using (var connection = new SqliteConnection($"Data Source={file}"))
            {
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "UPDATE exploration_meta SET value = '2' WHERE key = 'schema_version';";
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            var migrated = new ExplorationHistoryRepository(file);
            ExplorationHistoryBodySnapshot body = Assert.Single(migrated.LoadSystem("Cmdr", 9, "Current").Bodies);
            Assert.Single(body.Organics);
            Assert.True(body.Organics[0].Completed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }
}

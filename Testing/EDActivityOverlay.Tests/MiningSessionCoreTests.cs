using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningSessionCoreTests
{
    [Fact]
    public void ReplayProducesDeterministicActiveSession()
    {
        var live = new MiningSessionAccumulator();
        var bootstrap = new MiningSessionAccumulator();

        foreach (string json in ActiveSessionLines())
        {
            live.Apply(Event(json, JournalEventOrigin.Live));
            bootstrap.Apply(Event(json, JournalEventOrigin.Bootstrap));
        }

        MiningSessionSnapshot left = live.Current;
        MiningSessionSnapshot right = bootstrap.Current;

        Assert.Equal(MiningSessionState.Active, left.State);
        Assert.Equal(left.SessionId, right.SessionId);
        Assert.Equal(left.StartedUtc, right.StartedUtc);
        Assert.Equal(left.SystemAddress, right.SystemAddress);
        Assert.Equal(left.SystemName, right.SystemName);
        Assert.Equal(left.ProspectorsLaunched, right.ProspectorsLaunched);
        Assert.Equal(left.ProspectedAsteroids, right.ProspectedAsteroids);
        Assert.Equal(left.RefinedTons, right.RefinedTons);
        Assert.Equal(left.CrackedAsteroids, right.CrackedAsteroids);
        Assert.Equal(
            left.Prospects.Single().Materials.Select(item => (item.CommodityId, item.Proportion)),
            right.Prospects.Single().Materials.Select(item => (item.CommodityId, item.Proportion)));
    }

    [Fact]
    public void BootstrapCompletedSessionDoesNotWriteHistory()
    {
        WithService((service, _) =>
        {
            foreach (string json in ActiveSessionLines().Append(SupercruiseEntryLine()))
            {
                service.OnJournalEvent(Event(json, JournalEventOrigin.Bootstrap));
            }

            Assert.Equal(MiningSessionState.Idle, service.Current.State);
            Assert.Empty(service.LoadRecentSessions());
        });
    }

    [Fact]
    public void BootstrapActiveSessionPersistsOnceWhenLiveBoundaryArrives()
    {
        WithService((service, _) =>
        {
            foreach (string json in ActiveSessionLines())
            {
                service.OnJournalEvent(Event(json, JournalEventOrigin.Bootstrap));
            }

            Guid reconstructedId = service.Current.SessionId;
            service.OnJournalEvent(Event(SupercruiseEntryLine(), JournalEventOrigin.Live));
            service.OnJournalEvent(Event(
                """{"timestamp":"2026-08-31T00:13:00Z","event":"FSDJump","StarSystem":"Next","SystemAddress":43}""",
                JournalEventOrigin.Live));

            MiningSessionSnapshot saved = Assert.Single(service.LoadRecentSessions());
            Assert.Equal(reconstructedId, saved.SessionId);
            Assert.Equal(MiningSessionEndReason.SupercruiseEntry, saved.EndReason);
            Assert.Equal(1, saved.ProspectedAsteroids);
            Assert.Equal(1, saved.ProspectorsLaunched);
            Assert.Equal(1, saved.CollectorsLaunched);
            Assert.Equal(2, saved.RefinedTons);
            Assert.Equal(1, saved.CrackedAsteroids);
            Assert.Equal("platinum", saved.Prospects[0].Materials[0].CommodityId);
            Assert.Equal(31.4, saved.Prospects[0].Materials[0].Proportion, 3);
            Assert.Equal(2, saved.RefinedByCommodity["platinum"]);
        });
    }

    [Fact]
    public void ProspectorOnlySessionIsDiscarded()
    {
        WithService((service, _) =>
        {
            service.OnJournalEvent(Event(
                """{"timestamp":"2026-08-31T00:00:00Z","event":"LoadGame","Commander":"Test Cmdr"}"""));
            service.OnJournalEvent(Event(
                """{"timestamp":"2026-08-31T00:01:00Z","event":"Location","StarSystem":"Test","SystemAddress":42}"""));
            service.OnJournalEvent(Event(
                """{"timestamp":"2026-08-31T00:02:00Z","event":"LaunchDrone","Type":"Prospector"}"""));
            service.OnJournalEvent(Event(SupercruiseEntryLine()));

            Assert.Equal(MiningSessionState.Idle, service.Current.State);
            Assert.Empty(service.LoadRecentSessions());
        });
    }

    [Fact]
    public void CargoCompanionUpdatesActiveSessionWithoutChangingProduction()
    {
        var accumulator = new MiningSessionAccumulator();
        accumulator.Apply(Event(
            """{"timestamp":"2026-08-31T00:00:00Z","event":"LoadGame","Commander":"Test Cmdr"}"""));
        accumulator.Apply(Event(
            """{"timestamp":"2026-08-31T00:01:00Z","event":"Location","StarSystem":"Test","SystemAddress":42}"""));
        accumulator.Apply(Event(
            """{"timestamp":"2026-08-31T00:02:00Z","event":"Loadout","CargoCapacity":256}"""));
        accumulator.Apply(Event(
            """{"timestamp":"2026-08-31T00:03:00Z","event":"ProspectedAsteroid","Content":"High","Remaining":100,"Materials":[{"Name":"Platinum","Proportion":31.4}]}"""));

        accumulator.ApplyCompanion(Companion(
            """{"timestamp":"2026-08-31T00:04:00Z","event":"Cargo","Count":101,"Inventory":[{"Name":"Drones","Count":70},{"Name":"Platinum","Count":31}]}"""));

        MiningSessionSnapshot snapshot = accumulator.Current;
        Assert.Equal(256, snapshot.CargoCapacity);
        Assert.Equal(101, snapshot.CargoUsed);
        Assert.Equal(70, snapshot.LimpetsRemaining);
        Assert.Equal(0, snapshot.RefinedTons);
    }

    [Fact]
    public void RepositoryRoundTripPreservesProspectAndRefinementDetails()
    {
        WithService((service, _) =>
        {
            foreach (string json in ActiveSessionLines())
            {
                service.OnJournalEvent(Event(json));
            }
            service.OnCompanionFile(Companion(
                """{"timestamp":"2026-08-31T00:11:30Z","event":"Cargo","Count":82,"Inventory":[{"Name":"Drones","Count":70},{"Name":"Platinum","Count":12}]}"""));
            service.OnJournalEvent(Event(SupercruiseEntryLine()));

            MiningSessionSnapshot saved = Assert.Single(service.LoadRecentSessions());
            Assert.Equal(82, saved.CargoUsed);
            Assert.Equal(256, saved.CargoCapacity);
            Assert.Equal(70, saved.LimpetsRemaining);
            Assert.Equal("High", saved.Prospects[0].Content);
            Assert.Equal("platinum", saved.Refinements[0].CommodityId);
            Assert.Equal("Platinum", saved.Refinements[0].DisplayName);
            Assert.Equal("osmium", saved.Prospects[0].Materials[1].CommodityId);
        });
    }

    private static IEnumerable<string> ActiveSessionLines()
    {
        yield return """{"timestamp":"2026-08-31T00:00:00Z","event":"LoadGame","Commander":"Test Cmdr"}""";
        yield return """{"timestamp":"2026-08-31T00:01:00Z","event":"Location","StarSystem":"Test","SystemAddress":42}""";
        yield return """{"timestamp":"2026-08-31T00:02:00Z","event":"Loadout","CargoCapacity":256}""";
        yield return """{"timestamp":"2026-08-31T00:03:00Z","event":"SupercruiseExit","StarSystem":"Test","SystemAddress":42,"Body":"Test 5 A Ring","BodyID":7,"BodyType":"PlanetaryRing"}""";
        yield return """{"timestamp":"2026-08-31T00:04:00Z","event":"LaunchDrone","Type":"Prospector"}""";
        yield return """{"timestamp":"2026-08-31T00:05:00Z","event":"ProspectedAsteroid","Content":"High","Remaining":100,"Materials":[{"Name":"Platinum","Name_Localised":"Platinum","Proportion":31.4},{"Name":"Osmium","Name_Localised":"Osmium","Proportion":8.2}]}""";
        yield return """{"timestamp":"2026-08-31T00:06:00Z","event":"LaunchDrone","Type":"Collection"}""";
        yield return """{"timestamp":"2026-08-31T00:07:00Z","event":"MiningRefined","Type":"Platinum","Type_Localised":"Platinum"}""";
        yield return """{"timestamp":"2026-08-31T00:08:00Z","event":"MiningRefined","Type":"Platinum","Type_Localised":"Platinum"}""";
        yield return """{"timestamp":"2026-08-31T00:09:00Z","event":"AsteroidCracked"}""";
    }

    private static string SupercruiseEntryLine() =>
        """{"timestamp":"2026-08-31T00:12:00Z","event":"SupercruiseEntry","StarSystem":"Test","SystemAddress":42}""";

    private static JournalEventReceivedEventArgs Event(
        string json,
        JournalEventOrigin origin = JournalEventOrigin.Live)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement.Clone();
        return new JournalEventReceivedEventArgs(
            root.GetProperty("event").GetString() ?? string.Empty,
            DateTimeOffset.Parse(root.GetProperty("timestamp").GetString() ?? string.Empty),
            root,
            origin);
    }

    private static CompanionFileReceivedEventArgs Companion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement.Clone();
        return new CompanionFileReceivedEventArgs(
            "Cargo.json",
            DateTimeOffset.Parse(root.GetProperty("timestamp").GetString() ?? string.Empty),
            root);
    }

    private static void WithService(Action<MiningSessionService, string> test)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "EDActivityOverlay.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "companion.db");

        try
        {
            var repository = new MiningSessionRepository(databasePath);
            using var service = new MiningSessionService(repository);
            test(service, databasePath);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }
}

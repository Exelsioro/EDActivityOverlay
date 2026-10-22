using System.Text.Json;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class OrganicSampleNavigationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-19T12:00:00Z");

    [Fact]
    public void ReturningNearFirstSampleIsNotReadyDespiteDistanceFromSecond()
    {
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(Position(0.005), TwoSamples());
        Assert.True(result.HasCompleteHistory);
        Assert.False(result.IsReady);
        Assert.InRange(result.NearestSample!.DistanceMeters, 87, 88);
    }

    [Fact]
    public void ReadyRequiresSpacingFromBothSamples()
    {
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(Position(0.03), TwoSamples());
        Assert.True(result.IsReady);
        Assert.Equal(2, result.KnownSampleCount);
        Assert.InRange(result.NearestSample!.DistanceMeters, 523, 524);
    }

    [Fact]
    public void LegacySecondSampleCannotProveSpacingFromLostFirstPosition()
    {
        OrganicScanProgressSnapshot legacy = TwoSamples() with { SampleLocations = [] };
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(Position(-0.06), legacy);
        Assert.Equal(1, result.KnownSampleCount);
        Assert.False(result.HasCompleteHistory);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void LegacyFirstSampleRemainsUsable()
    {
        OrganicScanProgressSnapshot legacy = TwoSamples() with
        {
            Stage = 1, SampleLocations = [], LastSampleLatitude = 0, LastSampleLongitude = 0
        };
        Assert.True(OrganicSampleNavigation.Assess(Position(0.06), legacy).IsReady);
    }

    [Fact]
    public void UnknownRadiusDoesNotInventAnEarthSizedPlanet()
    {
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(
            Position(0.03) with { PlanetRadiusMeters = null }, TwoSamples());
        Assert.Null(result.NearestSample);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void PositionsOnAnotherBodyCannotConfirmSeparation()
    {
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(
            Position(0.03) with { CurrentBody = "Test 5" }, TwoSamples());
        Assert.Null(result.NearestSample);
        Assert.False(result.IsReady);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public void InvalidPositionNeverClaimsReady(double latitude, double longitude)
    {
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(
            Position(longitude) with { Latitude = latitude }, TwoSamples());
        Assert.Null(result.NearestSample);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void MissingSampleNumbersCannotBeReplacedWithDuplicateSecondPositions()
    {
        OrganicScanProgressSnapshot organic = TwoSamples() with
        {
            SampleLocations = [new(2, 0, 0, Start), new(2, 0, 0.06, Start)]
        };
        Assert.False(OrganicSampleNavigation.Assess(Position(0.03), organic).IsReady);
    }

    [Fact]
    public void ReducerRecordsBothSamplesAndResetsOnNewLog()
    {
        var reducer = CreateReducer();
        reducer.ApplyStatusJson(Status(0));
        reducer.ApplyJournalLine(Scan("Log", Start));
        reducer.ApplyStatusJson(Status(0.06));
        reducer.ApplyJournalLine(Scan("Sample", Start.AddMinutes(1)));

        OrganicScanProgressSnapshot progress = Assert.Single(reducer.Current.OrganicProgress);
        Assert.Equal(new[] { 1, 2 }, progress.SampleLocations.Select(point => point.SampleNumber));
        Assert.Equal(0, progress.SampleLocations[0].Longitude);
        Assert.Equal(0.06, progress.SampleLocations[1].Longitude);

        reducer.ApplyStatusJson(Status(0.12));
        reducer.ApplyJournalLine(Scan("Log", Start.AddMinutes(2)));
        progress = Assert.Single(reducer.Current.OrganicProgress);
        Assert.Equal(1, progress.Stage);
        Assert.Equal(0.12, Assert.Single(progress.SampleLocations).Longitude);
    }

    [Fact]
    public void BootstrapDoesNotAttachCurrentPositionToHistoricalSamples()
    {
        var reducer = CreateReducer();
        reducer.ApplyStatusJson(Status(0.5));
        reducer.ApplyJournalLine(Scan("Log", Start), JournalEventOrigin.Bootstrap);
        reducer.ApplyJournalLine(Scan("Sample", Start.AddMinutes(1)), JournalEventOrigin.Bootstrap);
        OrganicScanProgressSnapshot progress = Assert.Single(reducer.Current.OrganicProgress);
        Assert.Empty(progress.SampleLocations);
        Assert.Null(progress.LastSampleLatitude);
        Assert.False(OrganicSampleNavigation.Assess(reducer.Current, progress).IsReady);
    }

    [Fact]
    public void PersistedLocationsSurviveRestartAndJournalBootstrap()
    {
        string file = Path.Combine(Path.GetTempPath(), $"edao-organic-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ExplorationProgressStore(file);
            var first = CreateReducer(store);
            first.ApplyStatusJson(Status(0));
            first.ApplyJournalLine(Scan("Log", Start));
            first.ApplyStatusJson(Status(0.06));
            first.ApplyJournalLine(Scan("Sample", Start.AddMinutes(1)));

            var restarted = CreateReducer(store);
            restarted.ApplyStatusJson(Status(0.5));
            restarted.ApplyJournalLine(Scan("Log", Start), JournalEventOrigin.Bootstrap);
            restarted.ApplyJournalLine(Scan("Sample", Start.AddMinutes(1)), JournalEventOrigin.Bootstrap);
            OrganicScanProgressSnapshot progress = Assert.Single(restarted.Current.OrganicProgress);
            Assert.Equal(new[] { 0d, 0.06 }, progress.SampleLocations.Select(point => point.Longitude));
            Assert.Equal(0.06, progress.LastSampleLongitude);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void LegacyJsonStillDeserializesWithoutSampleLocations()
    {
        string json = JsonSerializer.Serialize(TwoSamples());
        using JsonDocument doc = JsonDocument.Parse(json);
        var fields = doc.RootElement.EnumerateObject()
            .Where(field => field.Name != nameof(OrganicScanProgressSnapshot.SampleLocations))
            .ToDictionary(field => field.Name, field => field.Value.Clone());
        OrganicScanProgressSnapshot restored = JsonSerializer.Deserialize<OrganicScanProgressSnapshot>(
            JsonSerializer.Serialize(fields))!;
        Assert.Empty(restored.SampleLocations);
        Assert.Equal(0.06, restored.LastSampleLongitude);
        Assert.False(OrganicSampleNavigation.Assess(Position(-0.06), restored).IsReady);
    }

    private static GameStateSnapshot Position(double longitude) => new()
    {
        CurrentBody = "Test 4", Latitude = 0, Longitude = longitude,
        HeadingDegrees = 90, PlanetRadiusMeters = 1_000_000
    };

    private static OrganicScanProgressSnapshot TwoSamples() => new(
        "Cmdr", 42, "Test", 4, "Test 4", "Stratum", "Stratum Tectonicas", "",
        2, false, 500, 0, 0.06, Start.AddMinutes(1))
    {
        SpeciesKey = "stratumtectonicas",
        SampleLocations = [new(1, 0, 0, Start), new(2, 0, 0.06, Start.AddMinutes(1))]
    };

    private static JournalStateReducer CreateReducer(ExplorationProgressStore? store = null)
    {
        var reducer = new JournalStateReducer(store);
        reducer.ApplyJournalLine("""{"event":"LoadGame","Commander":"Cmdr"}""");
        reducer.ApplyJournalLine("""{"event":"Location","StarSystem":"Test","SystemAddress":42}""");
        return reducer;
    }

    private static string Status(double longitude) => JsonSerializer.Serialize(new
    {
        Flags = 2097152, Latitude = 0, Longitude = longitude, Heading = 90,
        PlanetRadius = 1_000_000, BodyName = "Test 4"
    });

    private static string Scan(string type, DateTimeOffset timestamp) => JsonSerializer.Serialize(new
    {
        @event = "ScanOrganic", timestamp, ScanType = type, Body = 4,
        Genus = "$Codex_Ent_Stratum_Genus_Name;", Species = "stratumtectonicas"
    });
}

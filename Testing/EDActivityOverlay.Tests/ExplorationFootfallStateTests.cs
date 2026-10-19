using EDActivityOverlay.Services.Journal;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class ExplorationFootfallStateTests
{
    [Fact]
    public void ScanTracksAuthoritativeFirstFootfallStateWhenJournalProvidesIt()
    {
        var reducer = new JournalStateReducer();

        reducer.ApplyJournalLine(
            """
            {
              "timestamp":"2026-09-14T10:00:00Z",
              "event":"Scan",
              "BodyName":"Test 1 A",
              "BodyID":4,
              "PlanetClass":"Rocky body",
              "Landable":true,
              "WasDiscovered":true,
              "WasMapped":false,
              "WasFootfalled":false
            }
            """);

        var body = Assert.Single(reducer.Current.ExplorationBodies);

        Assert.True(body.FootfallStatusKnown);
        Assert.False(body.WasFootfalled);
    }

    [Fact]
    public void MissingWasFootfalledRemainsUnknown()
    {
        var reducer = new JournalStateReducer();

        reducer.ApplyJournalLine(
            """
            {
              "timestamp":"2026-09-14T10:00:00Z",
              "event":"Scan",
              "BodyName":"Test 1 A",
              "BodyID":4,
              "PlanetClass":"Rocky body",
              "Landable":true,
              "WasDiscovered":true,
              "WasMapped":false
            }
            """);

        var body = Assert.Single(reducer.Current.ExplorationBodies);

        Assert.False(body.FootfallStatusKnown);
        Assert.False(body.WasFootfalled);
    }
}

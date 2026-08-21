using ED_Inara_Overlay.Models;
using ED_Inara_Overlay.Services.Exploration;
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class ExplorationPoiTests
{
    [Fact]
    public void ParsesCanonnPublicSiteFormats()
    {
        string tsv = "type\traw system\tx\ty\tz\tinstructions\turl\nTHARGOIDTOUR\tHIP 1\t1.5\t-2\t3\tActive site\thttps://example.test\n";
        string json = """[{"system":"Synuefe Test","x":"4","y":"5","z":"6","instructions":"Guardian Beacon","url":null}]""";

        CanonnPoiProvider.CanonnSite thargoid = Assert.Single(CanonnPoiProvider.ParseThargoids(tsv));
        CanonnPoiProvider.CanonnSite guardian = Assert.Single(CanonnPoiProvider.ParseGuardians(json));

        Assert.Equal("HIP 1", thargoid.System);
        Assert.Equal(-2, thargoid.Y);
        Assert.Equal("Synuefe Test", guardian.System);
        Assert.Contains("Guardian", guardian.Description);
    }

    [Fact]
    public void PoiCacheRoundTripsBothSources()
    {
        string path = Path.Combine(Path.GetTempPath(), $"poi-cache-{Guid.NewGuid():N}.json");
        try
        {
            var gec = new ExplorationPoiSnapshot("EDAstro", "1", "GEC", "Sol", "Historical", "Region", "", "", 5, 1, 0, 0, 0, DateTimeOffset.UtcNow);
            var canonn = gec with { Source = "Canonn", Name = "Guardian", DistanceLy = 2 };
            var expected = new ExplorationPoiState(ExplorationPoiStatus.Available, gec, string.Empty) { NearestCanonn = canonn };
            var cache = new ExplorationPoiCache(path);
            cache.Put("sol", expected);

            var reloaded = new ExplorationPoiCache(path);
            Assert.True(reloaded.TryGet("sol", TimeSpan.FromDays(1), out ExplorationPoiState actual));
            Assert.Equal("Guardian", actual.NearestCanonn?.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

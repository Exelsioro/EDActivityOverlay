using EDActivityOverlay.Models;
using EDActivityOverlay.Services;
using EDActivityOverlay.Services.Ardent;
using EDActivityOverlay.Services.Journal;
using EDActivityOverlay.Services.Navigation;
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
}

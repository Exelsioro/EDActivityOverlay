using System.IO;
using Xunit;

namespace EDActivityOverlay.Tests;

public sealed class MiningV1cWorkspaceTests
{
    [Fact]
    public void FullAnalyticsWorkspaceIsIntegratedIntoMiningHost()
    {
        string host = Read("EDActivityOverlay", "Windows", "ActivityWorkspaceOverlayWindow.Mining.cs");
        string position = Read("EDActivityOverlay", "Windows", "ActivityWorkspaceOverlayWindow.xaml.cs");
        string compact = Read("EDActivityOverlay", "UserControls", "MiningWorkspaceControl.xaml");
        string full = Read("EDActivityOverlay", "UserControls", "MiningAnalyticsWorkspaceControl.xaml");

        Assert.Contains("MiningAnalyticsWorkspaceControl", host);
        Assert.Contains("IsMiningFullWorkspace", host);
        Assert.Contains("IsMiningFullWorkspace", position);
        Assert.Contains("FullAnalyticsButton", compact);
        Assert.Contains("HistoryGrid", full);
        Assert.Contains("YieldItemsControl", full);
    }

    [Fact]
    public void FullWorkspaceUsesPersistedMiningHistoryAndStableRateRules()
    {
        string code = Read("EDActivityOverlay", "UserControls", "MiningAnalyticsWorkspaceControl.xaml.cs");
        string analytics = Read("EDActivityOverlay", "Services", "Mining", "MiningSessionAnalytics.cs");

        Assert.Contains("LoadRecentSessions(100)", code);
        Assert.Contains("MinimumRateDuration = TimeSpan.FromMinutes(5)", analytics);
        Assert.Contains("MinimumRateTons = 5", analytics);
        Assert.Contains("EstimatedTimeToFull", analytics);
        Assert.Contains("TargetP75", analytics);
    }

    private static string Read(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. relative]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}

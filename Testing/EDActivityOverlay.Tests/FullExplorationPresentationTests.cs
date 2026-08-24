using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class FullExplorationPresentationTests
{
    [Fact]
    public void FullCatalogUsesWrappedIdentityCellsAndSixColumns()
    {
        string xaml = File.ReadAllText(FindProjectFile("Windows", "ActivityWorkspaceOverlayWindow.xaml"));
        Assert.Contains("MaxHeight=\"40\"", xaml);
        Assert.Contains("x:Name=\"FullPoiPanel\"", xaml);
        Assert.Contains("x:Name=\"SelectedBodyPhysicalText\"", xaml);
        Assert.Contains("x:Name=\"SelectedBodyBiologyPanel\"", xaml);
        Assert.DoesNotContain("x:Name=\"DssGuideSelectedBodyButton\"", xaml);
        Assert.Contains("x:Name=\"PlotPoiRouteButton\"", xaml);
        Assert.Contains("x:Name=\"OpenPoiDetailsButton\"", xaml);
        Assert.Contains("x:Name=\"CopyPoiSystemButton\"", xaml);
        Assert.Contains("x:Name=\"DeferSelectedBodyButton\"", xaml);
        Assert.Contains("x:Name=\"BookmarkSelectedBodyButton\"", xaml);
        Assert.Contains("x:Name=\"CopySelectedBodyButton\"", xaml);
        Assert.DoesNotContain("Header=\"{DynamicResource Loc_EXPLORATION_VISIT_STATE}\"", xaml);
    }

    private static string FindProjectFile(params string[] relative)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string path = Path.Combine(new[] { d.FullName, "EDActivityOverlay" }.Concat(relative).ToArray());
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException();
    }
}
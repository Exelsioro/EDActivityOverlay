using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

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
        Assert.Contains("x:Name=\"DssGuideSelectedBodyButton\"", xaml);
        Assert.DoesNotContain("Header=\"{DynamicResource Loc_EXPLORATION_VISIT_STATE}\"", xaml);
    }

    private static string FindProjectFile(params string[] relative)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string path = Path.Combine(new[] { d.FullName, "ED_Inara_Overlay" }.Concat(relative).ToArray());
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException();
    }
}
using Xunit;

namespace ED_Inara_Overlay.LayoutTests;

public sealed class PinnedRouteLayoutTests
{
    [Fact]
    public void PinnedRouteShowsAndCopiesBothEndpoints()
    {
        string repository = FindRepositoryRoot();
        string markup = File.ReadAllText(Path.Combine(
            repository, "ED_Inara_Overlay", "Windows", "PinnedRouteOverlay.xaml"));

        Assert.Contains("x:Name=\"FromPointText\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToPointText\"", markup, StringComparison.Ordinal);
        Assert.Contains("FromPointText_MouseLeftButtonUp", markup, StringComparison.Ordinal);
        Assert.Contains("ClickableTextStyle", markup, StringComparison.Ordinal);
        Assert.Contains("CopyFromStationButton_Click", markup, StringComparison.Ordinal);
        Assert.Contains("ToPointText_MouseLeftButtonUp", markup, StringComparison.Ordinal);
        Assert.Contains("CopyToStationButton_Click", markup, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ED_Inara_Overlay", "ED_Inara_Overlay.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class TradeV10StabilizationTests
{
    [Fact]
    public void FreshSearchDoesNotAutoSelectAResult()
    {
        string workspace =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        string cargo =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.CargoSale.cs");

        string compactWorkspace =
            RemoveWhitespace(
                workspace);

        string compactCargo =
            RemoveWhitespace(
                cargo);

        Assert.Contains(
            "RefreshCurrentPage(selectFirstWhenEmpty:false);",
            compactWorkspace,
            StringComparison.Ordinal);

        Assert.Contains(
            "RefreshCargoSalePage(selectFirstWhenEmpty:false);",
            compactCargo,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SearchResultsPersistAcrossWorkspaceRecreation()
    {
        string stability =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.Stability.cs");

        string workspace =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        Assert.Contains(
            "private static readonly SearchResultSession ResultSession",
            stability,
            StringComparison.Ordinal);

        Assert.Contains(
            "RestoreResultSnapshot();",
            workspace,
            StringComparison.Ordinal);

        Assert.Contains(
            "CaptureResultSnapshot();",
            workspace,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingFiltersKeepsOldResultsVisibleUntilSearch()
    {
        string workspace =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.xaml.cs");

        string stability =
            ReadProjectFile(
                "EDActivityOverlay",
                "UserControls",
                "TradeWorkspaceControl.Stability.cs");

        Assert.Contains(
            "MarkSearchInputsDirty();",
            workspace,
            StringComparison.Ordinal);

        Assert.Contains(
            "Keep the previous result set visible",
            stability,
            StringComparison.Ordinal);

        Assert.Contains(
            "RefreshCurrentPage(",
            stability,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalButtonHoverControlsPresenterForeground()
    {
        string styles =
            ReadProjectFile(
                "EDActivityOverlay",
                "Resources",
                "UIStyles.xaml");

        Assert.Contains(
            "x:Name=\"buttonContentPresenter\"",
            styles,
            StringComparison.Ordinal);

        Assert.Contains(
            "TargetName=\"buttonContentPresenter\"",
            styles,
            StringComparison.Ordinal);

        Assert.Contains(
            "Property=\"TextElement.Foreground\"",
            styles,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContinuousPlannerDoesNotShadowFinalResultVariable()
    {
        string planner =
            ReadProjectFile(
                "EDActivityOverlay",
                "Services",
                "Trading",
                "TradeContinuousSearchService.cs");

        Assert.DoesNotContain(
            "EnrichmentResult result =",
            planner,
            StringComparison.Ordinal);

        Assert.Contains(
            "EnrichmentResult enrichment =",
            planner,
            StringComparison.Ordinal);
    }

    private static string RemoveWhitespace(
        string value) =>
        string.Concat(
            value.Where(character =>
                !char.IsWhiteSpace(character)));

    private static string ReadProjectFile(
        params string[] relative)
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            string candidate =
                directory.FullName;

            foreach (string part in relative)
            {
                candidate =
                    Path.Combine(
                        candidate,
                        part);
            }

            if (File.Exists(candidate))
            {
                return File.ReadAllText(
                    candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(
                Path.DirectorySeparatorChar,
                relative));
    }
}

using System.Xml.Linq;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class ExplorationWorkspaceUiTests
{
    [Fact]
    public void RouteWorkspaceExposesSafeAndExperimentalNavigationActions()
    {
        string xaml = File.ReadAllText(FindWorkspaceXaml());

        Assert.Contains("PrepareRouteNavigationButton_Click", xaml);
        Assert.Contains("AutomaticRouteNavigationButton_Click", xaml);
        Assert.Contains("Loc_PREPARE_IN_GALAXY_MAP", xaml);
        Assert.Contains("Loc_AUTO_PLOT_EXPERIMENTAL", xaml);
    }

    [Fact]
    public void CompactWorkspaceHasAdaptiveHudAndFullWorkspaceContainsCatalogLogAndRoute()
    {
        string path = FindWorkspaceXaml();
        XDocument document = XDocument.Load(path);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement legacyScroll = Assert.Single(
            document.Descendants(wpf + "ScrollViewer"),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "LegacyCompactScrollViewer");

        Assert.Equal(
            "Auto",
            (string?)legacyScroll.Attribute(
                "VerticalScrollBarVisibility"));

        XElement adaptiveHud = Assert.Single(
            document.Descendants(),
            element =>
                (string?)element.Attribute(x + "Name")
                    == "AdaptiveExplorationPanel");

        Assert.DoesNotContain(
            adaptiveHud.Descendants(),
            element => element.Name == wpf + "ScrollViewer");
        Assert.NotNull(document.Descendants().FirstOrDefault(element =>
            (string?)element.Attribute(x + "Name") == "FullOverviewText"));
        Assert.NotNull(document.Descendants().FirstOrDefault(element =>
            (string?)element.Attribute(x + "Name") == "ExplorationLogGrid"));
        Assert.NotNull(document.Descendants().FirstOrDefault(element =>
            (string?)element.Attribute(x + "Name") == "CalculateSpanshRouteButton"));
        Assert.NotNull(document.Descendants().FirstOrDefault(element =>
            (string?)element.Attribute(x + "Name") == "RouteStopsItemsControl"));
        Assert.Contains(document.Descendants(wpf + "TextBlock"), element =>
            (string?)element.Attribute("MouseLeftButtonUp") == "CopySystemText_MouseLeftButtonUp");
        Assert.NotNull(document.Descendants().FirstOrDefault(element =>
            (string?)element.Attribute(x + "Name") == "ToggleRouteFormButton"));
        Assert.NotNull(document.Descendants().FirstOrDefault(element =>
            (string?)element.Attribute(x + "Name") == "ToggleRouteListButton"));
        XElement dssCanvas = Assert.Single(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "DssPatternCanvas");
        Assert.Equal("460", (string?)dssCanvas.Attribute("Width"));
        Assert.Equal("460", (string?)dssCanvas.Attribute("Height"));
    }

    [Fact]
    public void SharedStatusAndPinnedTradeRouteExposeNavigationWithoutCrowdingExploration()
    {
        string workspace = File.ReadAllText(FindWorkspaceXaml());
        string status = File.ReadAllText(FindProjectFile("Windows", "ShipStatusOverlayWindow.xaml"));
        string pinned = File.ReadAllText(FindProjectFile("Windows", "PinnedRouteOverlay.xaml"));

        Assert.Contains("CurrentSystemText", status);
        Assert.Contains("NextSystemText", status);
        Assert.Contains("AdvisoryText", status);
        Assert.Contains("PrepareNavigationButton_Click", pinned);
        Assert.Contains("AutomaticNavigationButton_Click", pinned);
        Assert.Contains("Visibility=\"Collapsed\"", workspace);
    }

    [Fact]
    public void ShipStatusWidgetRemainsIndependentWhileTradeWorkspaceIsVisible()
    {
        string main = File.ReadAllText(FindProjectFile("Windows", "MainWindow.xaml.cs"));
        string orchestration = File.ReadAllText(FindProjectFile("Windows", "MainWindow.OverlayOrchestration.cs"));
        string status = File.ReadAllText(FindProjectFile("Windows", "ShipStatusOverlayWindow.xaml.cs"));

        Assert.Contains("shipStatusOverlayWindow.SetContextSuppression(null);", main, StringComparison.Ordinal);
        Assert.DoesNotContain("SetContextSuppression(IsTradeSurfaceVisible)", main, StringComparison.Ordinal);
        Assert.Contains("currentActivity == ActivityType.Trade", orchestration, StringComparison.Ordinal);
        Assert.Contains("activityWorkspaceWindow?.IsVisible == true", orchestration, StringComparison.Ordinal);
        Assert.Contains("pinnedRouteOverlay?.IsVisible == true", orchestration, StringComparison.Ordinal);
        Assert.Contains("!contextSuppressed", status, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityToggleSuppressionPreventsWindowTimersFromShowingHiddenWidgetsAgain()
    {
        string navigation = File.ReadAllText(FindProjectFile("Windows", "MainWindow.ActivityNavigation.cs"));
        string visibility = File.ReadAllText(FindProjectFile("Utils", "OverlayVisibilityState.cs"));
        string[] timedWindows =
        [
            "TradeRouteWindow.xaml.cs",
            "ResultsOverlayWindow.xaml.cs",
            "PinnedRouteOverlay.xaml.cs",
            "ActivityWorkspaceOverlayWindow.xaml.cs"
        ];

        Assert.Contains("SuppressActivity = true", navigation, StringComparison.Ordinal);
        Assert.Contains("RestoreCurrentActivityWindows", navigation, StringComparison.Ordinal);
        Assert.Contains("SuppressActivity", visibility, StringComparison.Ordinal);
        foreach (string window in timedWindows)
        {
            Assert.Contains("OverlayVisibilityState.SuppressActivity",
                File.ReadAllText(FindProjectFile("Windows", window)), StringComparison.Ordinal);
        }
    }

    private static string FindWorkspaceXaml()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "EDActivityOverlay", "Windows", "ActivityWorkspaceOverlayWindow.xaml");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Activity workspace XAML was not found.");
    }

    private static string FindProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, "EDActivityOverlay", .. relative]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, relative));
    }
}

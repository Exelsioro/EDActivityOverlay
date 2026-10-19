using System;
using System.IO;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class CompositeActivityHostTests
{
    [Fact]
    public void CompositeOwnsActivityHostWithoutLegacyActivityWindows()
    {
        string xaml = ReadProjectFile(
            "EDActivityOverlay",
            "Windows",
            "CompositeOverlayWindow.xaml");
        string code = ReadProjectFile(
            "EDActivityOverlay",
            "Windows",
            "CompositeOverlayWindow.xaml.cs");
        string host = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "CompositeActivityHostControl.cs");

        Assert.Contains("x:Name=\"ActivityHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("activityHost.SetActivity(controller.CompositeCurrentActivity)", code, StringComparison.Ordinal);
        Assert.Contains("new ExplorationWorkspaceControl", host, StringComparison.Ordinal);
        Assert.Contains("new EngineeringWorkspaceControl", host, StringComparison.Ordinal);
        Assert.Contains("new TradeWorkspaceControl()", host, StringComparison.Ordinal);
        Assert.Contains("new MiningWorkspaceControl()", host, StringComparison.Ordinal);
        Assert.Contains("new MiningAnalyticsWorkspaceControl()", host, StringComparison.Ordinal);
        Assert.Contains("new MiningLocationWorkspaceControl()", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new ActivityWorkspaceOverlayWindow", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new EngineeringWindow", host, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositePreservesActivityInteractionContracts()
    {
        string host = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "CompositeActivityHostControl.cs");

        Assert.Contains("BeginExclusiveOverlayInteraction", host, StringComparison.Ordinal);
        Assert.Contains("EndExclusiveOverlayInteraction", host, StringComparison.Ordinal);
        Assert.Contains("PinTradeRouteRequested", host, StringComparison.Ordinal);
        Assert.Contains("PinCargoSaleRouteRequested", host, StringComparison.Ordinal);
        Assert.Contains("PinRoundTripRouteRequested", host, StringComparison.Ordinal);
        Assert.Contains("NavigateTradeSystemRequested", host, StringComparison.Ordinal);
        Assert.Contains("OpenMiningAnalyticsRequested", host, StringComparison.Ordinal);
        Assert.Contains("OpenMiningLocationsRequested", host, StringComparison.Ordinal);
        Assert.Contains("SellMiningCargoRequested", host, StringComparison.Ordinal);
        Assert.Contains("EngineeringViewModeChanged", host, StringComparison.Ordinal);
        Assert.Contains("NavigateEngineeringSystemAsync", host, StringComparison.Ordinal);
        Assert.Contains("CompactDragRequested", host, StringComparison.Ordinal);
        Assert.Contains("ResetActivityPresentationForExit(previousActivity);", host, StringComparison.Ordinal);
        Assert.Contains("ResetActivityPresentationForExit(currentActivity);", host, StringComparison.Ordinal);
        Assert.Contains("engineeringWorkspaceControl.SetCompactMode();", host, StringComparison.Ordinal);
        Assert.Contains("miningSurface =", host, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeExplorationConsumesDssPresentationRuntimeOutsideVr()
    {
        string host = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "CompositeActivityHostControl.cs");
        string exploration = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "ExplorationWorkspaceControl.xaml.cs");
        string dssPresentation = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "ExplorationWorkspaceControl.Dss.cs");
        string xaml = ReadProjectFile(
            "EDActivityOverlay",
            "UserControls",
            "ExplorationWorkspaceControl.xaml");

        Assert.Contains(
            "ActivityType.Exploration",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExplorationCompactDssHeight",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "explorationWorkspaceControl.IsCompactDssMode",
            host,
            StringComparison.Ordinal);
        Assert.Contains(
            "state.GuiFocus == 10",
            exploration,
            StringComparison.Ordinal);
        Assert.Contains(
            "live!.TargetingStep",
            dssPresentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DssProbePatternCatalog.Get",
            exploration,
            StringComparison.Ordinal);
        Assert.Contains(
            "DssAssistantStateService",
            exploration,
            StringComparison.Ordinal);
        Assert.Contains(
            "DssNativeScanProgressRuntime",
            dssPresentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "DssNativeEfficiencyTargetRuntime",
            dssPresentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "VrOverlaySupport.IsEnabled",
            exploration,
            StringComparison.Ordinal);
        Assert.Contains(
            "RefreshCompactDssDetails",
            exploration,
            StringComparison.Ordinal);
        Assert.Contains(
            "GenerateOrderedSphericalPlan",
            dssPresentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "BuildCompactAngularSummary",
            dssPresentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "BuildCompactDistanceSummary",
            dssPresentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"DssReadinessTitleText\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"DssPlanCanvas\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Width=\"108\" Height=\"96\" Visibility=\"Collapsed\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeBootstrapsControllerHotkeysAndMiningTradeBridge()
    {
        string coordinator = ReadProjectFile(
            "EDActivityOverlay",
            "Utils",
            "OverlayRenderCoordinator.cs");
        string navigation = ReadProjectFile(
            "EDActivityOverlay",
            "Windows",
            "MainWindow.ActivityNavigation.cs");

        Assert.Contains("EnsureControllerLoadedForComposite", coordinator, StringComparison.Ordinal);
        Assert.Contains("BeginCargoSaleFromMiningAsync", coordinator, StringComparison.Ordinal);
        Assert.Contains("OverlayRenderCoordinator.BeginCargoSaleFromMiningAsync", navigation, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(
                [
                    directory.FullName,
                    .. relative
                ]);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException(
            string.Join(Path.DirectorySeparatorChar, relative));
    }
}

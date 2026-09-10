using EDActivityOverlay.Services;
using EDActivityOverlay.Utils;
using Xunit;

namespace EDActivityOverlay.Tests;

public class VrOverlaySupportTests
{
    [Theory]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, false, true, false, false)]
    public void TargetReadyPreservesDesktopRulesAndAllowsVrCapture(
        bool vr,
        bool exists,
        bool visible,
        bool minimized,
        bool expected)
    {
        Assert.Equal(
            expected,
            OverlayVisibilityPolicy.ResolveTargetReady(
                vr,
                exists,
                visible,
                minimized));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, true)]
    public void FocusPolicyIsBypassedOnlyForVrPresentation(
        bool vr,
        bool targetFocused,
        bool overlayFocused,
        bool expected)
    {
        Assert.Equal(
            expected,
            OverlayVisibilityPolicy.ResolveFocusAllowsPresentation(
                vr,
                targetFocused,
                overlayFocused));
    }

    [Fact]
    public void VrSupportIsOptInByDefault()
    {
        Assert.False(new AppSettings().EnableVrOverlaySupport);
    }

    [Theory]
    [InlineData("MainWindow", "Main")]
    [InlineData("ActivityWorkspaceOverlayWindow", "ActivityWorkspace")]
    [InlineData("ShipStatusOverlayWindow", "ShipStatus")]
    [InlineData("NotificationOverlayWindow", "Notifications")]
    [InlineData("PinnedRouteOverlay", "PinnedRoute")]
    [InlineData("ResultsOverlayWindow", "Results")]
    [InlineData("TradeRouteWindow", "TradeRoute")]
    [InlineData("EngineeringWindow", "Engineering")]
    [InlineData("SettingsWindow", "Settings")]
    [InlineData("DssPrototypeOverlayWindow", "Dss")]
    [InlineData("WaitingWindow", "Waiting")]
    [InlineData("X52ControlHelpWindow", "X52Help")]
    [InlineData("VrCompositeOverlayWindow", "Composite")]
    public void KnownTopLevelWindowsHaveStableVrRoles(
        string typeName,
        string expectedRole)
    {
        Assert.Equal(
            expectedRole,
            VrOverlaySupport.DescribeWindow(typeName).Role.ToString());
    }
    [Theory]
    [InlineData("ShipStatusOverlayWindow", true)]
    [InlineData("NotificationOverlayWindow", true)]
    [InlineData("PinnedRouteOverlay", true)]
    [InlineData("ActivityWorkspaceOverlayWindow", false)]
    public void CompositeMigrationOwnsOnlyExtractedPanels(
        string typeName,
        bool expected)
    {
        VrOverlayWindowRole role =
            VrOverlaySupport.DescribeWindow(typeName).Role;

        Assert.Equal(
            expected,
            VrOverlaySupport.IsCompositeOwnedRole(role));
    }
}

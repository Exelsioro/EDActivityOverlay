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
    public void RenderAndVrSupportAreOptInByDefault()
    {
        var settings = new AppSettings();
        Assert.Equal(OverlayRenderModes.Individual, settings.OverlayRenderMode);
        Assert.False(settings.EnableVrOverlaySupport);
    }

    [Theory]
    [InlineData(null, "Individual")]
    [InlineData("", "Individual")]
    [InlineData("individual", "Individual")]
    [InlineData("COMPOSITE", "Composite")]
    [InlineData("unknown", "Individual")]
    public void RendererModeNormalizationIsStable(
        string? value,
        string expected) =>
        Assert.Equal(expected, OverlayRenderModes.Normalize(value));
    [Theory]
    [InlineData("Individual", false, false)]
    [InlineData("Individual", true, false)]
    [InlineData("Composite", false, false)]
    [InlineData("Composite", true, true)]
    public void VrCompatibilityRequiresCompositeRenderer(
        string renderMode,
        bool requested,
        bool expected) =>
        Assert.Equal(
            expected,
            VrOverlaySupport.ResolveEnabled(renderMode, requested));

}

using System.Windows;
using EDActivityOverlay.Utils;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public class OverlayLayoutHelperTests
{
    [Theory]
    [InlineData("Compact", "Compact")]
    [InlineData("Minimal", "Minimal")]
    [InlineData("EliteHud", "Compact")]
    [InlineData(null, "Compact")]
    public void OverlayChrome_NormalizesAvailableStyles(string? style, string expected)
    {
        Assert.Equal(expected, OverlayChromeStyles.Normalize(style));
    }

    [Fact]
    public void ComputeAdaptiveScale_ReturnsOneAtBaselineResolution()
    {
        double scale = OverlayLayoutHelper.ComputeAdaptiveScale(
            OverlayLayoutSettings.BaselineWidth,
            OverlayLayoutSettings.BaselineHeight,
            OverlayLayoutSettings.MainMinScale,
            OverlayLayoutSettings.MainMaxScale);

        Assert.Equal(1.0, scale, 6);
    }

    [Fact]
    public void ComputeAdaptiveScale_ClampsToMinScale()
    {
        double scale = OverlayLayoutHelper.ComputeAdaptiveScale(
            400,
            300,
            OverlayLayoutSettings.MainMinScale,
            OverlayLayoutSettings.MainMaxScale);

        Assert.Equal(OverlayLayoutSettings.MainMinScale, scale, 6);
    }

    [Fact]
    public void ComputeAdaptiveScale_ClampsToMaxScale()
    {
        double scale = OverlayLayoutHelper.ComputeAdaptiveScale(
            8000,
            4000,
            OverlayLayoutSettings.MainMinScale,
            OverlayLayoutSettings.MainMaxScale);

        Assert.Equal(OverlayLayoutSettings.MainMaxScale, scale, 6);
    }

    [Fact]
    public void GetRelativePosition_BottomLeft_AnchorsToTargetBottomLeft()
    {
        var rect = new WindowsAPI.RECT { Left = 100, Top = 200, Right = 1100, Bottom = 800 };
        var (left, top) = OverlayLayoutHelper.GetRelativePosition(rect, 250, 120, RelativePosition.BottomLeft);

        Assert.Equal(100, left);
        Assert.Equal(680, top);
    }

    [Fact]
    public void GetRelativePosition_RightCenter_UsesConfiguredGap()
    {
        var rect = new WindowsAPI.RECT { Left = 10, Top = 20, Right = 510, Bottom = 420 };
        var (left, top) = OverlayLayoutHelper.GetRelativePosition(rect, 200, 100, RelativePosition.RightCenter);

        Assert.Equal(rect.Right + OverlayLayoutSettings.DefaultGap, left);
        Assert.Equal(170, top);
    }

    [Fact]
    public void ClampX_ClampsIntoWorkAreaWithMargin()
    {
        Rect workArea = new(0, 0, 1000, 800);
        double clamped = OverlayLayoutHelper.ClampX(950, 200, workArea, 10);
        Assert.Equal(790, clamped);
    }

    [Fact]
    public void ClampY_ClampsIntoWorkAreaWithMargin()
    {
        Rect workArea = new(0, 0, 1000, 800);
        double clamped = OverlayLayoutHelper.ClampY(-100, 200, workArea, 10);
        Assert.Equal(10, clamped);
    }

    [Fact]
    public void ClampPosition_ClampsBothCoordinates()
    {
        Rect workArea = new(100, 50, 400, 300); // Right=500, Bottom=350
        double left = 20;
        double top = 500;

        OverlayLayoutHelper.ClampPosition(ref left, ref top, 200, 100, workArea, 10, 20);

        Assert.Equal(110, left);
        Assert.Equal(230, top);
    }

    [Theory]
    [InlineData("MiddleLeft", 110, 400)]
    [InlineData("MiddleRight", 790, 400)]
    [InlineData("TopCenter", 450, 210)]
    [InlineData("BottomCenter", 450, 590)]
    public void GetPinnedPosition_AnchorsInsideTarget(
        string placement,
        double expectedLeft,
        double expectedTop)
    {
        var rect = new WindowsAPI.RECT { Left = 100, Top = 200, Right = 1100, Bottom = 800 };

        var (left, top) = OverlayLayoutHelper.GetPinnedPosition(rect, 300, 200, placement, 10);

        Assert.Equal(expectedLeft, left);
        Assert.Equal(expectedTop, top);
    }

    [Theory]
    [InlineData("MiddleLeft", "MiddleRight")]
    [InlineData("MiddleRight", "MiddleLeft")]
    [InlineData("TopCenter", "MiddleRight")]
    public void GetOppositeSidePlacement_AvoidsOccupiedSide(string occupied, string expected)
    {
        Assert.Equal(expected, OverlayLayoutHelper.GetOppositeSidePlacement(occupied));
    }
}

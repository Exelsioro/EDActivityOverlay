using System.Windows;
using EDActivityOverlay.Utils;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class OverlayResolutionSupportTests
{
    [Theory]
    [InlineData(1280, 720, 0.85)]
    [InlineData(1600, 900, 0.85)]
    [InlineData(1920, 1080, 1.00)]
    [InlineData(2560, 1440, 1.30)]
    [InlineData(3440, 1440, 1.30)]
    [InlineData(3840, 2160, 1.30)]
    public void TradeAdaptiveScaleHasDefinedBoundsAcrossCommonResolutions(
        double width,
        double height,
        double expected)
    {
        double actual =
            OverlayLayoutHelper.ComputeAdaptiveScale(
                width,
                height,
                OverlayLayoutSettings.TradeWindowMinScale,
                OverlayLayoutSettings.TradeWindowMaxScale);

        Assert.Equal(
            expected,
            actual,
            precision:
                2);
    }

    [Fact]
    public void ClampPositionSupportsSecondaryMonitorWithNegativeCoordinates()
    {
        var workArea =
            new Rect(
                -1920,
                0,
                1920,
                1040);

        double left =
            -2500;

        double top =
            1000;

        OverlayLayoutHelper.ClampPosition(
            ref left,
            ref top,
            width:
                420,
            height:
                305,
            workArea,
            marginX:
                12,
            marginY:
                12);

        Assert.InRange(
            left,
            workArea.Left + 12,
            workArea.Right - 420 - 12);

        Assert.InRange(
            top,
            workArea.Top + 12,
            workArea.Bottom - 305 - 12);
    }

    [Fact]
    public void ManifestAndWindowsApiUsePerMonitorDpi()
    {
        string repository =
            FindRepositoryRoot();

        string manifest =
            File.ReadAllText(
                Path.Combine(
                    repository,
                    "app.manifest"));

        string api =
            File.ReadAllText(
                Path.Combine(
                    repository,
                    "EDActivityOverlay",
                    "Utils",
                    "WindowsAPI.cs"));

        Assert.Contains(
            "PerMonitorV2",
            manifest,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetDpiForWindow",
            api,
            StringComparison.Ordinal);

        Assert.Contains(
            "MonitorFromWindow",
            api,
            StringComparison.Ordinal);

        Assert.Contains(
            "PhysicalToLogicalPointForPerMonitorDPI",
            api,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullTradeSizingUsesTheEliteMonitorInsteadOfPrimaryWorkArea()
    {
        string source =
            File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    "EDActivityOverlay",
                    "Windows",
                    "ActivityWorkspaceOverlayWindow.Trade.cs"));

        Assert.Contains(
            "WindowsAPI.GetMonitorWorkArea(",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "SystemParameters.WorkArea.Width",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "SystemParameters.WorkArea.Height",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                 new(
                     AppContext.BaseDirectory);
             directory is not null;
             directory =
                 directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "EDActivityOverlay",
                        "EDActivityOverlay.csproj")))
            {
                return
                    directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Repository root was not found.");
    }
}

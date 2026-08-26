using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssCoverageObserverTests
{
    [Fact]
    public void BlueCoverageOnLeft_SelectsUncoveredRightSide()
    {
        var observer = new DssCoverageObserver();
        DssHudGeometry geometry = CreateGeometry();

        for (int i = 0; i < 4; i++)
        {
            _ = observer.Process(
                CreateFrame(
                    DateTimeOffset.UtcNow.AddMilliseconds(i * 70),
                    paintCoverageLeft: false,
                    naturallyBlue: false),
                geometry,
                enabled: false,
                excludedCandidateMask: 0);
        }

        DssCoverageObservation observation =
            observer.Process(
                CreateFrame(
                    DateTimeOffset.UtcNow.AddSeconds(1),
                    paintCoverageLeft: true,
                    naturallyBlue: false),
                geometry,
                enabled: true,
                excludedCandidateMask: 0);

        Assert.True(observation.Available);
        Assert.True(observation.SuggestedCandidateId > 0);
        Assert.True(
            observation.SuggestedNormalizedX > 0.25d,
            $"expected an uncovered right-side point, got x={observation.SuggestedNormalizedX:0.###}");
    }

    [Fact]
    public void UnchangedNaturallyBlueBody_IsNotInventedAsCoverage()
    {
        var observer = new DssCoverageObserver();
        DssHudGeometry geometry = CreateGeometry();

        for (int i = 0; i < 5; i++)
        {
            _ = observer.Process(
                CreateFrame(
                    DateTimeOffset.UtcNow.AddMilliseconds(i * 70),
                    paintCoverageLeft: false,
                    naturallyBlue: true),
                geometry,
                enabled: false,
                excludedCandidateMask: 0);
        }

        DssCoverageObservation observation =
            observer.Process(
                CreateFrame(
                    DateTimeOffset.UtcNow.AddSeconds(1),
                    paintCoverageLeft: false,
                    naturallyBlue: true),
                geometry,
                enabled: true,
                excludedCandidateMask: 0);

        Assert.False(observation.Available);
    }

    private static DssHudGeometry CreateGeometry() =>
        new(
            960,
            540,
            true,
            960,
            700,
            0.98,
            true,
            true,
            960,
            500,
            0.9,
            0,
            200,
            0,
            9);

    private static DssCapturedFrame CreateFrame(
        DateTimeOffset timestamp,
        bool paintCoverageLeft,
        bool naturallyBlue)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;
        byte[] pixels =
            new byte[stride * height];

        const int cx = 960;
        const int cy = 700;
        const int radius = 200;

        for (int y = cy - radius;
             y <= cy + radius;
             y++)
        {
            for (int x = cx - radius;
                 x <= cx + radius;
                 x++)
            {
                int dx = x - cx;
                int dy = y - cy;

                if (dx * dx + dy * dy
                    > radius * radius)
                {
                    continue;
                }

                int index =
                    y * stride
                    + x * 4;

                if (paintCoverageLeft
                    && x < cx)
                {
                    // BGRA: strongly DSS-like blue/cyan overlay.
                    pixels[index] = 72;
                    pixels[index + 1] = 38;
                    pixels[index + 2] = 10;
                    pixels[index + 3] = 255;
                }
                else if (naturallyBlue)
                {
                    pixels[index] = 90;
                    pixels[index + 1] = 50;
                    pixels[index + 2] = 20;
                    pixels[index + 3] = 255;
                }
                else
                {
                    pixels[index] = 80;
                    pixels[index + 1] = 80;
                    pixels[index + 2] = 80;
                    pixels[index + 3] = 255;
                }
            }
        }

        return new DssCapturedFrame(
            timestamp,
            0,
            0,
            width,
            height,
            stride,
            pixels);
    }
}

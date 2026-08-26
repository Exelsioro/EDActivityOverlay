using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssAimMissIndicatorDetectorTests
{
    [Fact]
    public void EmptyReticleAreaIsNotMiss()
    {
        DssCapturedFrame frame =
            CreateFrame(
                withMissGlyph: false);

        DssAimMissObservation result =
            DssAimMissIndicatorDetector.Detect(
                frame);

        Assert.False(
            result.Visible);
    }

    [Fact]
    public void NeutralGlyphUnderReticleIsMiss()
    {
        DssCapturedFrame frame =
            CreateFrame(
                withMissGlyph: true);

        DssAimMissObservation result =
            DssAimMissIndicatorDetector.Detect(
                frame);

        Assert.True(
            result.Visible);

        Assert.True(
            result.ActiveRatio
            >= DssAimMissIndicatorDetector
                .VisibleRatioThreshold);
    }

    [Fact]
    public void SingleBrightGuideThroughRoiIsNotMiss()
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[stride * height];

        // Reproduces the v21 failure family where the real centre/guide passes
        // through the fixed MISS ROI. Raw active ratio is high enough for the
        // old detector, but this is one connected structure rather than text.
        DrawRect(
            pixels,
            stride,
            946,
            580,
            8,
            40);

        DssCapturedFrame frame =
            new(
                DateTimeOffset.UtcNow,
                0,
                0,
                width,
                height,
                stride,
                pixels);

        DssAimMissObservation result =
            DssAimMissIndicatorDetector.Detect(
                frame);

        Assert.False(result.Visible);
        Assert.True(
            result.ActiveRatio
            >= DssAimMissIndicatorDetector
                .VisibleRatioThreshold);
    }

    [Theory]
    // Recorded v21 real Russian MISS word families.
    [InlineData(0.0572, 4, 42, 14, 1.0, true)]
    [InlineData(0.0616, 3, 43, 14, 1.0, true)]
    // Recorded v21 false families caused by centre marker / guide geometry.
    [InlineData(0.0700, 1, 18, 15, 1.0, false)]
    [InlineData(0.0156, 1, 14, 40, 1.0, false)]
    [InlineData(0.0131, 3, 24, 4, 1.0, false)]
    [InlineData(0.0938, 3, 55, 40, 1.0, false)]
    public void IndicatorShape_SeparatesRecordedV21Families(
        double activeRatio,
        int components,
        int spanWidth,
        int spanHeight,
        double scale,
        bool expected)
    {
        bool actual =
            DssAimMissIndicatorDetector
                .IsIndicatorShapeAccepted(
                    activeRatio,
                    components,
                    spanWidth,
                    spanHeight,
                    scale);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShallowScatteredComponentsAreNotMiss()
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[stride * height];

        // Reproduces the v22 false family: several disconnected bright HUD /
        // scene fragments span enough horizontal space to look text-like to
        // the v22 topology test, but none has real glyph height.
        foreach (int x in new[] { 925, 939, 953, 967, 981 })
        {
            DrawRect(
                pixels,
                stride,
                x,
                592,
                10,
                4);
        }

        DssCapturedFrame frame =
            new(
                DateTimeOffset.UtcNow,
                0,
                0,
                width,
                height,
                stride,
                pixels);

        DssAimMissObservation result =
            DssAimMissIndicatorDetector.Detect(
                frame);

        Assert.False(result.Visible);
        Assert.True(
            result.ActiveRatio
            >= DssAimMissIndicatorDetector
                .VisibleRatioThreshold);
    }

    private static DssCapturedFrame CreateFrame(
        bool withMissGlyph)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        if (withMissGlyph)
        {
            // Synthetic neutral text-like strokes inside the production ROI.
            for (int x = 930; x < 990; x += 10)
            {
                DrawRect(
                    pixels,
                    stride,
                    x,
                    592,
                    4,
                    14);
            }
        }

        return new DssCapturedFrame(
            DateTimeOffset.UtcNow,
            0,
            0,
            width,
            height,
            stride,
            pixels);
    }

    private static void DrawRect(
        byte[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height)
    {
        for (int yy = y;
             yy < y + height;
             yy++)
        {
            for (int xx = x;
                 xx < x + width;
                 xx++)
            {
                int index =
                    yy * stride
                    + xx * 4;

                pixels[index] = 150;
                pixels[index + 1] = 150;
                pixels[index + 2] = 150;
                pixels[index + 3] = 255;
            }
        }
    }
}

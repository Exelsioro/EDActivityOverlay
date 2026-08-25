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

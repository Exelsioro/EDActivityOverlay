using System;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Windows;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssImageMotionTests
{
    [Fact]
    public void CenterImageTracker_RecoversKnownTranslation()
    {
        DssCapturedFrame first =
            CreateTexturedFrame(
                shiftX: 0,
                shiftY: 0);

        DssCapturedFrame second =
            CreateTexturedFrame(
                shiftX: 8,
                shiftY: -6);

        var tracker =
            new DssCenterImageTracker();

        tracker.CaptureTemplate(
            first,
            100,
            90);

        bool found =
            tracker.TryTrack(
                second,
                predictedCenterX: 108,
                predictedCenterY: 84,
                out DssImageTrackResult? result);

        Assert.True(found);
        Assert.NotNull(result);

        Assert.InRange(
            result!.CenterX,
            107,
            109);

        Assert.InRange(
            result.CenterY,
            83,
            85);

        Assert.True(
            result.Confidence >= 0.58d);
    }

    [Fact]
    public void DynamicTranslation_UsesVelocityWithoutPositionEma()
    {
        (
            double x,
            double y) =
            DssPrototypeOverlayWindow
                .CalculateDynamicHudTranslation(
                    velocityX: 300,
                    velocityY: -100,
                    ageSeconds: 0.08);

        Assert.InRange(
            x,
            23.99,
            24.01);

        Assert.InRange(
            y,
            -8.01,
            -7.99);
    }

    [Fact]
    public void DynamicTranslation_IsDistanceBounded()
    {
        (
            double x,
            double y) =
            DssPrototypeOverlayWindow
                .CalculateDynamicHudTranslation(
                    velocityX: 1800,
                    velocityY: 0,
                    ageSeconds: 1);

        Assert.InRange(
            x,
            95.99,
            96.01);

        Assert.Equal(
            0d,
            y);
    }

    private static DssCapturedFrame CreateTexturedFrame(
        int shiftX,
        int shiftY)
    {
        const int width = 220;
        const int height = 180;
        int stride =
            width * 4;

        byte[] pixels =
            new byte[stride * height];

        for (int y = 0;
             y < height;
             y++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                int sourceX =
                    x - shiftX;

                int sourceY =
                    y - shiftY;

                int luma =
                    sourceX < 0
                    || sourceX >= width
                    || sourceY < 0
                    || sourceY >= height
                        ? 0
                        : Pattern(
                            sourceX,
                            sourceY);

                int index =
                    y * stride
                    + x * 4;

                pixels[index] =
                    (byte)luma;

                pixels[index + 1] =
                    (byte)luma;

                pixels[index + 2] =
                    (byte)luma;

                pixels[index + 3] =
                    255;
            }
        }

        return
            new DssCapturedFrame(
                DateTimeOffset.UtcNow,
                0,
                0,
                width,
                height,
                stride,
                pixels);
    }

    private static int Pattern(
        int x,
        int y) =>
        (
            x * 37
            + y * 61
            + (x * y) % 251
            + ((x ^ y) * 17)
        ) & 0xFF;
}

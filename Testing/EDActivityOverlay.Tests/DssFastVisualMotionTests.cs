using System;
using EDActivityOverlay.Services.Dss;
using EDActivityOverlay.Windows;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssFastVisualMotionTests
{
    [Fact]
    public void FastMatcher_RecoversKnownTranslation()
    {
        DssCapturedFrame first =
            CreateTexturedFrame(
                shiftX: 0,
                shiftY: 0);

        DssCapturedFrame second =
            CreateTexturedFrame(
                shiftX: 19,
                shiftY: -11);

        var tracker =
            new DssFastCenterImageTracker();

        Assert.True(
            tracker.CaptureTemplate(
                first,
                110,
                90));

        bool found =
            tracker.TryTrack(
                second,
                predictedCenterX: 126,
                predictedCenterY: 81,
                searchRadiusPixels: 24,
                out DssImageTrackResult? result);

        Assert.True(found);
        Assert.NotNull(result);

        Assert.InRange(
            result!.CenterX,
            128,
            130);

        Assert.InRange(
            result.CenterY,
            78,
            80);

        Assert.True(
            result.Confidence >= 0.56d);
    }

    [Fact]
    public void FastResidual_IsShortAndBounded()
    {
        (
            double x,
            double y) =
            DssPrototypeOverlayWindow
                .CalculateFastVisualResidual(
                    velocityX: 1000,
                    velocityY: 0,
                    ageSeconds: 1);

        Assert.InRange(
            x,
            47.99,
            48.01);

        Assert.Equal(
            0d,
            y);
    }

    private static DssCapturedFrame CreateTexturedFrame(
        int shiftX,
        int shiftY)
    {
        const int width = 240;
        const int height = 190;

        int stride =
            width * 4;

        byte[] pixels =
            new byte[
                stride * height];

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

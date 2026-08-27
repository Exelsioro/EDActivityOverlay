using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssNativeCenterMarkerTrackerTests
{
    [Fact]
    public void NativeMarkerTracker_FindsFilledDiskWithGuide()
    {
        DssCapturedFrame frame =
            CreateMarkerFrame(
                width: 640,
                height: 360,
                markerX: 430,
                markerY: 220);

        bool found =
            DssFastVisualMotionTracker
                .TryFindNativeCenterMarker(
                    frame,
                    predictedCenterX: 422,
                    predictedCenterY: 216,
                    searchRadiusPixels: 24,
                    out DssFastVisualMotionTracker
                        .NativeCenterMarker marker);

        Assert.True(found);

        Assert.InRange(
            marker.X,
            429.0,
            431.0);

        Assert.InRange(
            marker.Y,
            219.0,
            221.0);

        Assert.True(
            marker.Confidence >= 0.72d);
    }

    [Fact]
    public void NativeMarkerTracker_RejectsIsolatedStar()
    {
        DssCapturedFrame frame =
            CreateMarkerFrame(
                width: 640,
                height: 360,
                markerX: 430,
                markerY: 220,
                drawGuide: false,
                radius: 3);

        bool found =
            DssFastVisualMotionTracker
                .TryFindNativeCenterMarker(
                    frame,
                    predictedCenterX: 430,
                    predictedCenterY: 220,
                    searchRadiusPixels: 20,
                    out _);

        Assert.False(found);
    }

    private static DssCapturedFrame CreateMarkerFrame(
        int width,
        int height,
        int markerX,
        int markerY,
        bool drawGuide = true,
        int radius = 7)
    {
        int stride =
            width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        // Dark brown body-like background.
        for (int y = 0;
             y < height;
             y++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                SetPixel(
                    pixels,
                    stride,
                    x,
                    y,
                    45,
                    35,
                    30);
            }
        }

        int reticleX =
            width / 2;

        int reticleY =
            height / 2;

        if (drawGuide)
        {
            double dx =
                markerX - reticleX;

            double dy =
                markerY - reticleY;

            double length =
                Math.Sqrt(
                    dx * dx
                    + dy * dy);

            double ux =
                dx / length;

            double uy =
                dy / length;

            for (double t = 25;
                 t < length - 8;
                 t += 1)
            {
                int x =
                    (int)Math.Round(
                        reticleX
                        + ux * t);

                int y =
                    (int)Math.Round(
                        reticleY
                        + uy * t);

                SetPixel(
                    pixels,
                    stride,
                    x,
                    y,
                    220,
                    220,
                    220);
            }
        }

        for (int oy = -radius;
             oy <= radius;
             oy++)
        {
            for (int ox = -radius;
                 ox <= radius;
                 ox++)
            {
                if (ox * ox
                    + oy * oy
                    > radius * radius)
                {
                    continue;
                }

                SetPixel(
                    pixels,
                    stride,
                    markerX + ox,
                    markerY + oy,
                    245,
                    245,
                    245);
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

    private static void SetPixel(
        byte[] pixels,
        int stride,
        int x,
        int y,
        byte red,
        byte green,
        byte blue)
    {
        int index =
            y * stride
            + x * 4;

        pixels[index] =
            blue;

        pixels[index + 1] =
            green;

        pixels[index + 2] =
            red;

        pixels[index + 3] =
            255;
    }
}

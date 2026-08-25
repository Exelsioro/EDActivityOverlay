using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssImpactCounterChangeDetectorTests
{
    [Fact]
    public void StableHudDoesNotProduceImpact()
    {
        var detector =
            new DssImpactCounterChangeDetector();

        DssCapturedFrame first =
            CreateFrame(
                alternateDigits: false,
                movingStarOffset: 0);

        DssCapturedFrame second =
            CreateFrame(
                alternateDigits: false,
                movingStarOffset: 5);

        Assert.False(
            detector.Process(first).Changed);

        DssImpactCounterObservation observation =
            detector.Process(second);

        Assert.False(
            observation.Changed);
    }

    [Fact]
    public void ChangedImpactGlyphProducesImpactEvent()
    {
        var detector =
            new DssImpactCounterChangeDetector();

        DssCapturedFrame first =
            CreateFrame(
                alternateDigits: false,
                movingStarOffset: 0);

        DssCapturedFrame second =
            CreateFrame(
                alternateDigits: true,
                movingStarOffset: 2);

        detector.Process(first);

        DssImpactCounterObservation observation =
            detector.Process(second);

        Assert.True(
            observation.Changed);

        Assert.True(
            observation.ChangeRatio
            >= DssImpactCounterChangeDetector.ChangeThreshold);
    }

    private static DssCapturedFrame CreateFrame(
        bool alternateDigits,
        int movingStarOffset)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        int left =
            (int)Math.Round(
                width * 0.916);

        int top =
            (int)Math.Round(
                height * 0.768);

        // Static gray HUD glyph block.
        DrawRect(
            pixels,
            width,
            height,
            stride,
            left + 4,
            top + 3,
            5,
            10,
            150);

        DrawRect(
            pixels,
            width,
            height,
            stride,
            left + 14,
            top + 3,
            5,
            10,
            150);

        // "Impact digit" changes enough pixels to cross the calibrated
        // threshold.
        if (alternateDigits)
        {
            DrawRect(
                pixels,
                width,
                height,
                stride,
                left + 28,
                top + 7,
                8,
                10,
                145);
        }
        else
        {
            DrawRect(
                pixels,
                width,
                height,
                stride,
                left + 28,
                top + 7,
                3,
                10,
                145);
        }

        // One moving white star should be far below the event threshold.
        SetPixel(
            pixels,
            width,
            height,
            stride,
            left + 20 + movingStarOffset,
            top + 18,
            220,
            220,
            220);

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
        int width,
        int height,
        int stride,
        int x,
        int y,
        int w,
        int h,
        byte value)
    {
        for (int yy = y;
             yy < y + h;
             yy++)
        {
            for (int xx = x;
                 xx < x + w;
                 xx++)
            {
                SetPixel(
                    pixels,
                    width,
                    height,
                    stride,
                    xx,
                    yy,
                    value,
                    value,
                    value);
            }
        }
    }

    private static void SetPixel(
        byte[] pixels,
        int width,
        int height,
        int stride,
        int x,
        int y,
        byte r,
        byte g,
        byte b)
    {
        if (x < 0
            || y < 0
            || x >= width
            || y >= height)
        {
            return;
        }

        int index =
            y * stride
            + x * 4;

        pixels[index] = b;
        pixels[index + 1] = g;
        pixels[index + 2] = r;
        pixels[index + 3] = 255;
    }
}

using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

[Collection("DssNativeEfficiencyTargetRuntime")]
public sealed class DssNativeTargetAuthorityTests
{
    [Fact]
    public void Real58EridaniNativeEight_IsRecognized()
    {
        DssCapturedFrame frame =
            BuildNativeEightFrame(
                DateTimeOffset.UtcNow);

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(
            observation.Available);

        Assert.Equal(
            8,
            observation.Target);

        Assert.True(
            observation.Confidence >= 0.42d);
    }

    [Fact]
    public void SamePhysicalFrame_IsObservedOnlyOnce()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DateTimeOffset start =
            DateTimeOffset.UtcNow;

        DssCapturedFrame frame1 =
            BuildNativeEightFrame(
                start);

        DssNativeEfficiencyTargetRuntime.Observe(
            frame1);

        // The controller may see the same object after a constructor/boundary
        // observer has already processed it. It must not count as another
        // stable frame.
        DssNativeEfficiencyTargetRuntime.Observe(
            frame1);

        DssNativeEfficiencyTargetRuntime.Observe(
            BuildNativeEightFrame(
                start.AddMilliseconds(100)));

        DssNativeEfficiencyTargetRuntime.Observe(
            BuildNativeEightFrame(
                start.AddMilliseconds(200)));

        Assert.False(
            DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out _));

        DssNativeEfficiencyTargetRuntime.Observe(
            BuildNativeEightFrame(
                start.AddMilliseconds(300)));

        Assert.True(
            DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out DssNativeEfficiencyTargetSnapshot snapshot));

        Assert.Equal(
            8,
            snapshot.Target);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    private static DssCapturedFrame BuildNativeEightFrame(
        DateTimeOffset timestamp)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        // Native cyan target-label guard.
        for (int y = 792;
             y < 814;
             y++)
        {
            for (int x = 1545;
                 x < 1695;
                 x++)
            {
                SetBgra(
                    pixels,
                    stride,
                    x,
                    y,
                    blue: 170,
                    green: 105,
                    red: 15);
            }
        }

        byte[] glyph =
        {
            0,0,64,143,180,154,63,0,0,0,59,160,156,117,161,178,49,0,0,121,155,48,0,0,162,101,0,0,133,130,32,0,0,144,141,0,0,116,146,53,0,29,160,103,0,0,56,148,123,80,129,156,40,0,0,25,117,172,173,182,114,25,0,0,95,153,100,58,91,164,94,0,31,147,113,0,0,0,118,171,26,38,152,106,0,0,0,107,176,27,0,125,149,75,38,60,155,138,0,0,59,138,149,137,150,143,56,0,0,0,46,90,105,94,47,0,0
        };

        const int glyphLeft = 1771;
        const int glyphTop = 819;
        const int glyphWidth = 9;
        const int glyphHeight = 13;

        Assert.Equal(
            glyphWidth * glyphHeight,
            glyph.Length);

        for (int y = 0;
             y < glyphHeight;
             y++)
        {
            for (int x = 0;
                 x < glyphWidth;
                 x++)
            {
                byte value =
                    glyph[
                        y * glyphWidth + x];

                if (value == 0)
                {
                    continue;
                }

                SetBgra(
                    pixels,
                    stride,
                    glyphLeft + x,
                    glyphTop + y,
                    value,
                    value,
                    value);
            }
        }

        return
            new DssCapturedFrame(
                timestamp,
                0,
                0,
                width,
                height,
                stride,
                pixels);
    }

    private static void SetBgra(
        byte[] pixels,
        int stride,
        int x,
        int y,
        byte blue,
        byte green,
        byte red)
    {
        int index =
            y * stride
            + x * 4;

        pixels[index] = blue;
        pixels[index + 1] = green;
        pixels[index + 2] = red;
        pixels[index + 3] = 255;
    }
}

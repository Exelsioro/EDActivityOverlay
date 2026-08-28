using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssMergedNativeDigitsTests
{
    [Fact]
    public void TargetCv_RecognizesRealMergedN20Component()
    {
        DssCapturedFrame frame =
            BuildFrame(
                top: 819,
                includeTargetLabel: true);

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(observation.Available);
        Assert.Equal(20, observation.Target);
        Assert.True(observation.Confidence >= 0.42d);
    }

    [Fact]
    public void ProgressCv_RecognizesMergedNativeHits20()
    {
        DssCapturedFrame frame =
            BuildFrame(
                top: 836,
                includeTargetLabel: false);

        DssNativeScanProgressObservation observation =
            DssNativeScanProgressDetector.Detect(
                frame);

        Assert.True(observation.HitCountAvailable);
        Assert.Equal(20, observation.HitCount);
    }

    private static DssCapturedFrame BuildFrame(
        int top,
        bool includeTargetLabel)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        if (includeTargetLabel)
        {
            for (int y = 796;
                 y < 803;
                 y++)
            {
                for (int x = 1580;
                     x < 1710;
                     x++)
                {
                    SetBgra(
                        pixels,
                        stride,
                        x,
                        y,
                        230,
                        150,
                        10);
                }
            }
        }

        byte[] glyph =
        {
            48,129,178,191,158,76,0,0,0,0,62,147,186,162,70,0,0,100,160,112,111,167,175,79,0,0,53,157,146,94,144,179,55,0,39,25,0,0,69,166,115,0,0,126,167,56,0,0,162,135,0,0,0,0,0,31,131,131,30,28,145,123,29,0,0,118,182,40,0,0,0,0,32,130,125,25,46,155,98,0,0,0,84,187,55,0,0,0,0,66,151,98,0,62,163,88,0,0,0,67,186,68,0,0,0,27,115,141,50,0,67,165,86,0,0,0,60,185,73,0,0,0,82,148,85,0,0,54,159,92,0,0,0,76,187,61,0,0,58,137,110,0,0,0,36,150,109,0,0,0,100,185,47,0,43,123,125,39,0,0,0,0,137,144,43,0,0,139,161,32,35,120,157,84,45,46,45,0,0,91,166,100,48,82,173,96,0,96,183,174,141,140,143,134,60,0,28,112,151,142,158,129,31,0,70,105,105,105,105,106,97,45,0,0,32,81,104,90,36,0,0
        };

        const int glyphLeft = 1763;
        const int glyphWidth = 17;
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
                    top + y,
                    value,
                    value,
                    value);
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

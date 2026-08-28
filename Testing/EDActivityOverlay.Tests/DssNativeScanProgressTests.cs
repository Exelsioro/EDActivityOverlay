using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

[Collection("DssNativeEfficiencyTargetRuntime")]
public sealed class DssNativeScanProgressTests : IDisposable
{
    public DssNativeScanProgressTests()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
    }

    public void Dispose()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
    }

    [Theory]
    [InlineData(29)]
    [InlineData(79)]
    [InlineData(83)]
    [InlineData(88)]
    [InlineData(100)]
    public void CoverageDetector_ReadsLargeNativePercent(
        int expected)
    {
        DssCapturedFrame frame =
            BuildSyntheticFrame(
                expected,
                hits: 6);

        DssNativeScanProgressObservation observation =
            DssNativeScanProgressDetector.Detect(
                frame);

        Assert.True(
            observation.CoverageAvailable);

        Assert.Equal(
            expected,
            observation.CoveragePercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(12)]
    public void HitDetector_ReadsNativeHitsCounter(
        int expected)
    {
        DssCapturedFrame frame =
            BuildSyntheticFrame(
                coverage: 83,
                hits: expected);

        DssNativeScanProgressObservation observation =
            DssNativeScanProgressDetector.Detect(
                frame);

        Assert.True(
            observation.HitCountAvailable);

        Assert.Equal(
            expected,
            observation.HitCount);
    }

    [Fact]
    public void HitDetector_ReadsRecordedRealSevenGlyph()
    {
        DssCapturedFrame frame =
            BuildRecordedSevenHitFrame();

        DssNativeScanProgressObservation observation =
            DssNativeScanProgressDetector.Detect(
                frame);

        Assert.True(
            observation.HitCountAvailable);

        Assert.Equal(
            7,
            observation.HitCount);
    }

    [Fact]
    public void CorrectionGate_BlocksWhileNativeHitsLag()
    {
        DssNativeScanProgressRuntime.ResetForTests();

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 83,
            hits: 5,
            stableAge: TimeSpan.FromSeconds(5));

        Assert.False(
            DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredHitCount: 6,
                correctionIndex: 1,
                out _));
    }

    [Fact]
    public void CorrectionGate_BlocksAtNativeHundredPercent()
    {
        DssNativeScanProgressRuntime.ResetForTests();

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 100,
            hits: 6,
            stableAge: TimeSpan.FromSeconds(5));

        Assert.False(
            DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredHitCount: 6,
                correctionIndex: 1,
                out _));
    }

    [Fact]
    public void CorrectionGate_BlocksUntilCoverageAndHitCounterSettle()
    {
        DssNativeScanProgressRuntime.ResetForTests();

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 88,
            hits: 6,
            stableAge: TimeSpan.FromMilliseconds(500));

        Assert.False(
            DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredHitCount: 6,
                correctionIndex: 1,
                out _));

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 88,
            hits: 6,
            stableAge: TimeSpan.FromSeconds(3));

        Assert.True(
            DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredHitCount: 6,
                correctionIndex: 1,
                out _));
    }

    [Fact]
    public void Planner_DoesNotExposeFalseSeventhTargetWhenNativeHitsAreFive()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();

        DssNativeEfficiencyTargetRuntime.SetForTests(
            7);

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 83,
            hits: 5,
            stableAge: TimeSpan.FromSeconds(5));

        var module =
            new DssModuleSnapshot(
                "dss",
                "DSS",
                26d,
                20d,
                "expanded",
                3);

        DssSphericalAimTarget target =
            DssSphericalPlacementPlanner.Resolve(
                sequentialStep: 7,
                requestedTarget: 7,
                targetSource: "HUD_CV",
                angularDiameterDegrees: 26d,
                dssModule: module,
                bodyRadiusMeters: 8_000_000d,
                confirmedImpactCount: 99,
                coverageObservation: null,
                usedCoverageCandidates: 0);

        Assert.False(
            target.Available);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
    }

    private static DssCapturedFrame BuildRecordedSevenHitFrame()
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        PaintCoverage(
            pixels,
            stride,
            83);

        byte[] recordedSeven =
        {
            27,138,189,188,187,187,190,192,99,
            0,68,92,91,91,92,151,179,37,
            0,0,0,0,0,0,155,100,0,
            0,0,0,0,0,80,160,40,0,
            0,0,0,0,0,152,114,0,0,
            0,0,0,0,73,162,55,0,0,
            0,0,0,0,134,120,0,0,0,
            0,0,0,65,155,59,0,0,0,
            0,0,0,135,129,30,0,0,0,
            0,0,63,159,78,0,0,0,0,
            0,0,121,135,38,0,0,0,0,
            0,67,156,81,0,0,0,0,0,
            0,59,90,32,0,0,0,0,0
        };

        PaintRawNeutralGlyph(
            pixels,
            stride,
            1771,
            836,
            9,
            13,
            recordedSeven);

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

    private static void PaintRawNeutralGlyph(
        byte[] pixels,
        int stride,
        int left,
        int top,
        int width,
        int height,
        byte[] glyph)
    {
        Assert.Equal(
            width * height,
            glyph.Length);

        for (int y = 0;
             y < height;
             y++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                byte value =
                    glyph[
                        y * width + x];

                if (value == 0)
                {
                    continue;
                }

                int index =
                    (top + y)
                    * stride
                    + (left + x)
                      * 4;

                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
                pixels[index + 3] = 255;
            }
        }
    }

    private static DssCapturedFrame BuildSyntheticFrame(
        int coverage,
        int hits)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        PaintCoverage(
            pixels,
            stride,
            coverage);

        PaintNativeHits(
            pixels,
            stride,
            hits);

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

    private static void PaintCoverage(
        byte[] pixels,
        int stride,
        int value)
    {
        string text =
            value.ToString();

        int glyphHeight = 35;
        int glyphGap = 4;

        int[] widths =
            new int[
                text.Length];

        int totalWidth = 0;

        for (int i = 0;
             i < text.Length;
             i++)
        {
            int digit =
                text[i] - '0';

            widths[i] =
                EstimateScaledWidth(
                    digit,
                    glyphHeight);

            totalWidth +=
                widths[i];

            if (i + 1 < text.Length)
            {
                totalWidth +=
                    glyphGap;
            }
        }

        // Match the real HUD numeric run: 2-digit values start near x=144,
        // while 100% shifts left to keep the percent sign aligned.
        int left =
            207 - totalWidth;

        bool complete =
            value >= 100;

        int x = left;

        for (int i = 0;
             i < text.Length;
             i++)
        {
            int digit =
                text[i] - '0';

            PaintScaledDigit(
                pixels,
                stride,
                x,
                931,
                digit,
                widths[i],
                glyphHeight,
                complete);

            x +=
                widths[i]
                + glyphGap;
        }
    }

    private static void PaintNativeHits(
        byte[] pixels,
        int stride,
        int value)
    {
        string text =
            value.ToString();

        int rightEdge = 1780;
        int glyphGap = 3;
        int glyphHeight = 13;

        int[] widths =
            new int[
                text.Length];

        int totalWidth = 0;

        for (int i = 0;
             i < text.Length;
             i++)
        {
            int digit =
                text[i] - '0';

            widths[i] =
                EstimateScaledWidth(
                    digit,
                    glyphHeight);

            totalWidth +=
                widths[i];

            if (i + 1 < text.Length)
            {
                totalWidth +=
                    glyphGap;
            }
        }

        int x =
            rightEdge
            - totalWidth;

        for (int i = 0;
             i < text.Length;
             i++)
        {
            int digit =
                text[i] - '0';

            PaintScaledDigit(
                pixels,
                stride,
                x,
                836,
                digit,
                widths[i],
                glyphHeight,
                neutralWhite: true);

            x +=
                widths[i]
                + glyphGap;
        }
    }

    private static int EstimateScaledWidth(
        int digit,
        int targetHeight)
    {
        ReadOnlySpan<byte> template =
            DssNativeEfficiencyTargetDetector
                .GetDigitTemplateForTests(
                    digit);

        const int sourceWidth = 12;
        const int sourceHeight = 14;

        int minX = sourceWidth;
        int maxX = -1;

        for (int y = 0;
             y < sourceHeight;
             y++)
        {
            for (int x = 0;
                 x < sourceWidth;
                 x++)
            {
                if (template[
                        y * sourceWidth + x]
                    < 25)
                {
                    continue;
                }

                minX =
                    Math.Min(
                        minX,
                        x);

                maxX =
                    Math.Max(
                        maxX,
                        x);
            }
        }

        int sourceGlyphWidth =
            Math.Max(
                1,
                maxX - minX + 1);

        return
            Math.Max(
                2,
                (int)Math.Round(
                    sourceGlyphWidth
                    * targetHeight
                    / (double)sourceHeight));
    }

    private static void PaintScaledDigit(
        byte[] pixels,
        int stride,
        int left,
        int top,
        int digit,
        int targetWidth,
        int targetHeight,
        bool neutralWhite)
    {
        ReadOnlySpan<byte> template =
            DssNativeEfficiencyTargetDetector
                .GetDigitTemplateForTests(
                    digit);

        const int sourceWidth = 12;
        const int sourceHeight = 14;

        int minX = sourceWidth;
        int maxX = -1;

        for (int y = 0;
             y < sourceHeight;
             y++)
        {
            for (int x = 0;
                 x < sourceWidth;
                 x++)
            {
                if (template[
                        y * sourceWidth + x]
                    < 25)
                {
                    continue;
                }

                minX =
                    Math.Min(
                        minX,
                        x);

                maxX =
                    Math.Max(
                        maxX,
                        x);
            }
        }

        int sourceGlyphWidth =
            Math.Max(
                1,
                maxX - minX + 1);

        for (int y = 0;
             y < targetHeight;
             y++)
        {
            int sourceY =
                Math.Min(
                    sourceHeight - 1,
                    (int)(
                        (2L * y + 1L)
                        * sourceHeight
                        / (2L * targetHeight)));

            for (int x = 0;
                 x < targetWidth;
                 x++)
            {
                int sourceLocalX =
                    Math.Min(
                        sourceGlyphWidth - 1,
                        (int)(
                            (2L * x + 1L)
                            * sourceGlyphWidth
                            / (2L * targetWidth)));

                int sourceX =
                    minX
                    + sourceLocalX;

                byte value =
                    template[
                        sourceY
                        * sourceWidth
                        + sourceX];

                if (value < 25)
                {
                    continue;
                }

                int index =
                    (top + y)
                    * stride
                    + (left + x)
                      * 4;

                if (neutralWhite)
                {
                    pixels[index] = value;
                    pixels[index + 1] = value;
                    pixels[index + 2] = value;
                }
                else
                {
                    pixels[index] =
                        (byte)Math.Max(
                            (int)value,
                            110);

                    pixels[index + 1] =
                        (byte)Math.Max(
                            75,
                            value * 2 / 3);

                    pixels[index + 2] = 5;
                }

                pixels[index + 3] = 255;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

[Collection("DssNativeEfficiencyTargetRuntime")]
public sealed class DssLargeBodyRangeTests : IDisposable
{
    public DssLargeBodyRangeTests()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
    }

    public void Dispose()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
    }

    [Fact]
    public void NativeTargetCv_AcceptsObservedN21Body()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssCapturedFrame frame =
            BuildSyntheticTargetFrame(
                21);

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(
            observation.Available);

        Assert.Equal(
            21,
            observation.Target);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Fact]
    public void NativeTargetCv_RecognizesRecordedRealN21Glyph()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssCapturedFrame frame =
            BuildRecordedN21Frame();

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(
            observation.Available);

        Assert.Equal(
            21,
            observation.Target);

        Assert.True(
            observation.Confidence >= 0.42d);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Fact]
    public void NativeTargetCv_RecognizesRecordedRealN7Glyph()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssCapturedFrame frame =
            BuildRecordedN7Frame();

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(
            observation.Available);

        Assert.Equal(
            7,
            observation.Target);

        Assert.True(
            observation.Confidence >= 0.42d);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Theory]
    [InlineData(21)]
    [InlineData(24)]
    [InlineData(32)]
    public void Planner_PreservesAuthoritativeTargetsAboveOldN18Limit(
        int nativeTarget)
    {
        Assert.Equal(
            nativeTarget,
            DssSphericalPlacementPlanner.ResolveTargetCount(
                nativeTarget,
                "HUD_CV"));

        IReadOnlyList<SphericalPoint> points =
            DssSphericalPlacementPlanner.GenerateOptimalSphericalPoints(
                nativeTarget);

        Assert.Equal(
            nativeTarget,
            points.Count);
    }

    [Fact]
    public void WholeSphereOptimizer_CanBuildN21Layout()
    {
        IReadOnlyList<SphericalPoint> points =
            DssSphericalCapCoverage.GenerateCoverageOptimizedLayout(
                21,
                0.50d);

        Assert.Equal(
            21,
            points.Count);

        double coverage =
            DssSphericalCapCoverage.EvaluateUnionCoverage(
                points,
                0.50d);

        Assert.True(
            coverage > 0.80d);
    }

    [Fact]
    public void LargeReadyBody_UsesControlledProjectionExtrapolation()
    {
        const double diameter = 30.86d;

        Assert.False(
            DssSphericalProjection.IsWithinCalibration(
                diameter));

        Assert.True(
            DssSphericalProjection.IsWithinOperationalRange(
                diameter));

        Assert.True(
            DssSphericalProjection.UsesExtrapolatedBoundary(
                diameter));

        double boundaryAt28 =
            DssSphericalProjection.EstimateBoundaryNormalizedRadius(
                28d);

        double boundaryAtLargeBody =
            DssSphericalProjection.EstimateBoundaryNormalizedRadius(
                diameter);

        Assert.True(
            boundaryAtLargeBody
            < boundaryAt28);

        Assert.InRange(
            boundaryAtLargeBody,
            1.67d,
            1.70d);
    }

    [Fact]
    public void ProjectionStillCapsUnboundedExtrapolation()
    {
        double at36 =
            DssSphericalProjection.EstimateBoundaryNormalizedRadius(
                36d);

        double at80 =
            DssSphericalProjection.EstimateBoundaryNormalizedRadius(
                80d);

        Assert.Equal(
            at36,
            at80,
            12);
    }

    private static DssCapturedFrame BuildSyntheticTargetFrame(
        int target)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        for (int y = 796;
             y < 803;
             y++)
        {
            for (int x = width - 340;
                 x < width - 210;
                 x++)
            {
                SetBgra(
                    pixels,
                    stride,
                    x,
                    y,
                    blue: 230,
                    green: 150,
                    red: 10);
            }
        }

        int ones =
            target % 10;

        int onesLeft =
            PaintDigitRightAligned(
                pixels,
                stride,
                rightEdgeExclusive:
                    width - 140,
                top: 819,
                digit: ones);

        if (target >= 10)
        {
            int tens =
                target / 10;

            _ =
                PaintDigitRightAligned(
                    pixels,
                    stride,
                    rightEdgeExclusive:
                        onesLeft - 2,
                    top: 819,
                    digit: tens);
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

    private static DssCapturedFrame BuildRecordedN7Frame()
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        for (int y = 796;
             y < 803;
             y++)
        {
            for (int x = width - 340;
                 x < width - 210;
                 x++)
            {
                SetBgra(
                    pixels,
                    stride,
                    x,
                    y,
                    blue: 230,
                    green: 150,
                    red: 10);
            }
        }

        // Raw neutral-luma component extracted from the supplied real
        // Algol A 3 frame where Elite visibly showed "Зондов: 7" but
        // v54-r1 falsely locked N=2.
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
            819,
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

    private static DssCapturedFrame BuildRecordedN21Frame()
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        // Same cyan label guard used by the production detector.
        for (int y = 796;
             y < 803;
             y++)
        {
            for (int x = width - 340;
                 x < width - 210;
                 x++)
            {
                SetBgra(
                    pixels,
                    stride,
                    x,
                    y,
                    blue: 230,
                    green: 150,
                    red: 10);
            }
        }

        // Raw neutral-luma glyphs taken from the supplied real 1920x1080
        // "Зондов: 21" frame, before the detector's 12x14 normalization.
        byte[] recordedTwo =
        {
            45,127,177,190,157,74,0,0,
            98,159,110,109,166,174,77,0,
            36,0,0,0,67,165,113,0,
            0,0,0,0,28,129,129,27,
            0,0,0,0,29,128,123,0,
            0,0,0,0,64,150,96,0,
            0,0,0,0,113,140,47,0,
            0,0,0,80,147,83,0,0,
            0,0,56,135,108,0,0,0,
            0,40,121,123,36,0,0,0,
            32,118,155,82,42,43,42,0,
            94,182,173,140,139,142,132,58,
            68,103,103,103,103,104,95,42
        };

        byte[] recordedOne =
        {
            0,37,118,167,39,
            98,174,196,180,39,
            77,59,130,171,39,
            0,0,123,171,39,
            0,0,123,171,39,
            0,0,123,171,39,
            0,0,123,171,39,
            0,0,123,171,39,
            0,0,123,171,39,
            0,0,123,171,39,
            0,0,123,171,39,
            0,0,125,173,39,
            0,0,67,93,0
        };

        PaintRawNeutralGlyph(
            pixels,
            stride,
            1763,
            819,
            8,
            13,
            recordedTwo);

        PaintRawNeutralGlyph(
            pixels,
            stride,
            1773,
            819,
            5,
            13,
            recordedOne);

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

                SetBgra(
                    pixels,
                    stride,
                    left + x,
                    top + y,
                    value,
                    value,
                    value);
            }
        }
    }

    private static int PaintDigitRightAligned(
        byte[] pixels,
        int stride,
        int rightEdgeExclusive,
        int top,
        int digit)
    {
        ReadOnlySpan<byte> template =
            DssNativeEfficiencyTargetDetector
                .GetDigitTemplateForTests(
                    digit);

        const int glyphWidth = 12;
        const int glyphHeight = 14;

        int firstColumn = glyphWidth;
        int lastColumn = -1;

        for (int y = 0;
             y < glyphHeight;
             y++)
        {
            for (int x = 0;
                 x < glyphWidth;
                 x++)
            {
                if (template[
                        y * glyphWidth + x]
                    < 25)
                {
                    continue;
                }

                firstColumn =
                    Math.Min(
                        firstColumn,
                        x);

                lastColumn =
                    Math.Max(
                        lastColumn,
                        x);
            }
        }

        int visibleWidth =
            lastColumn >= firstColumn
                ? lastColumn - firstColumn + 1
                : 1;

        int visibleLeft =
            rightEdgeExclusive
            - visibleWidth;

        for (int y = 0;
             y < glyphHeight;
             y++)
        {
            for (int x = firstColumn;
                 x <= lastColumn;
                 x++)
            {
                byte value =
                    template[
                        y * glyphWidth + x];

                if (value < 25)
                {
                    continue;
                }

                SetBgra(
                    pixels,
                    stride,
                    visibleLeft
                    + (x - firstColumn),
                    top + y,
                    value,
                    value,
                    value);
            }
        }

        return visibleLeft;
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

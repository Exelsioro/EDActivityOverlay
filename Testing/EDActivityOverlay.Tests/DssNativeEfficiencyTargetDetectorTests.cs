using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

[CollectionDefinition(
    "DssNativeEfficiencyTargetRuntime",
    DisableParallelization = true)]
public sealed class DssNativeEfficiencyTargetRuntimeCollection
{
}

[Collection("DssNativeEfficiencyTargetRuntime")]
public sealed class DssNativeEfficiencyTargetDetectorTests
{
    [Theory]
    [InlineData(6)]
    [InlineData(18)]
    public void Detector_RecognizesSyntheticEliteHudTarget(
        int expectedTarget)
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssCapturedFrame frame =
            BuildSyntheticFrame(
                expectedTarget);

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(
            observation.Available);

        Assert.Equal(
            expectedTarget,
            observation.Target);

        Assert.True(
            observation.Confidence >= 0.42d);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Theory]
    [InlineData(6)]
    [InlineData(18)]
    public void Detector_RecognizesEnglishPrefixTarget(
        int expectedTarget)
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssCapturedFrame frame =
            BuildSyntheticEnglishFrame(
                expectedTarget);

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(
            observation.Available);

        Assert.Equal(
            expectedTarget,
            observation.Target);

        Assert.True(
            observation.Confidence >= 0.42d);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Theory]
    [InlineData(6)]
    [InlineData(18)]
    public void Detector_RecognizesObservedEnglishPrefixLayout1919x1079(
        int expectedTarget)
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssCapturedFrame frame =
            BuildSyntheticEnglishFrame(
                expectedTarget,
                width: 1919,
                height: 1079);

        DssNativeEfficiencyTargetObservation observation =
            DssNativeEfficiencyTargetDetector.Detect(
                frame);

        Assert.True(
            observation.Available);

        Assert.Equal(
            expectedTarget,
            observation.Target);

        Assert.True(
            observation.Confidence >= 0.42d);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Fact]
    public void Detector_RecognizesRecordedEnglishN21Layout()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssCapturedFrame frame =
            BuildRecordedEnglishN21Frame();

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
    public void Runtime_BodyChangeDropsPreviousNativeTarget()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssNativeEfficiencyTargetRuntime.SetForTests(
            4);

        Assert.True(
            DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out DssNativeEfficiencyTargetSnapshot before));

        Assert.Equal(
            4,
            before.Target);

        DssNativeEfficiencyTargetRuntime.ResetForBodyChange();

        Assert.False(
            DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out _));

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }
    [Fact]
    public void Runtime_LatchesOnlyAfterStableRepeatedFrames()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DateTimeOffset start =
            DateTimeOffset.UtcNow
            - TimeSpan.FromMilliseconds(900);

        for (int i = 0;
             i < 3;
             i++)
        {
            _ =
                BuildSyntheticFrame(
                    18,
                    start
                    + TimeSpan.FromMilliseconds(
                        i * 60d));

            Assert.False(
                DssNativeEfficiencyTargetRuntime.TryGetFresh(
                    out _));
        }

        _ =
            BuildSyntheticFrame(
                18,
                start
                + TimeSpan.FromMilliseconds(
                    180d));

        Assert.True(
            DssNativeEfficiencyTargetRuntime.TryGetFresh(
                out DssNativeEfficiencyTargetSnapshot snapshot));

        Assert.Equal(
            18,
            snapshot.Target);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Fact]
    public void Planner_UsesHudCvOfficialTargetAndAreaEngineeringBeforeFirstShot()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssNativeEfficiencyTargetRuntime.SetForTests(
            18);

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
                sequentialStep: 1,
                requestedTarget: 13,
                targetSource: "SETTINGS",
                angularDiameterDegrees: 30d,
                dssModule: module,
                bodyRadiusMeters: 1_000_000d,
                confirmedImpactCount: 0,
                coverageObservation: null,
                usedCoverageCandidates: 0);

        Assert.True(
            target.Available);

        // Current high-N reserve keeps one additional probe in the predictive
        // base batch so a useful rear probe is not delayed into correction mode.
        // Native N18 with PatchRadius 26/20 therefore resolves to N16.
        Assert.Equal(
            16,
            target.TotalPlanCount);

        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Fact]
    public void Planner_DoesNotBuildBatchFromSettingsBeforeNativeCvLocks()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();

        DssSphericalAimTarget target =
            DssSphericalPlacementPlanner.Resolve(
                sequentialStep: 1,
                requestedTarget: 13,
                targetSource: "SETTINGS",
                angularDiameterDegrees: 30d,
                dssModule: DssModuleSnapshot.Empty,
                bodyRadiusMeters: 1_000_000d,
                confirmedImpactCount: 0,
                coverageObservation: null,
                usedCoverageCandidates: 0);

        Assert.False(
            target.Available);
    }

    private static DssCapturedFrame BuildRecordedEnglishN21Frame()
    {
        const int width = 1919;
        const int height = 1079;
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
                    230,
                    150,
                    10);
            }
        }

        // Raw neutral components sampled from the supplied live "21 PROBES"
        // frame before the detector normalizes them to 12x14.
        byte[] digit2 =
        {
            0,47,84,95,95,82,37,0,
            65,169,160,147,144,165,161,52,
            119,148,0,0,0,0,167,95,
            118,115,0,0,0,0,147,100,
            41,37,0,0,0,0,156,94,
            0,0,0,32,71,121,188,74,
            36,137,184,183,175,163,88,0,
            114,178,76,50,28,0,0,0,
            132,136,0,0,0,0,0,0,
            136,168,116,116,116,116,115,57,
            85,129,129,129,129,129,128,63
        };

        byte[] digit1 =
        {
            0,0,47,98,86,
            0,32,168,206,178,
            32,187,168,169,177,
            133,167,54,137,177,
            44,43,0,137,177,
            0,0,0,137,177,
            0,0,0,137,177,
            0,0,0,137,177,
            0,0,0,137,177,
            0,0,0,137,177,
            0,0,0,85,110
        };

        PaintRecordedGlyph(
            pixels,
            stride,
            1699,
            819,
            8,
            11,
            digit2);

        PaintRecordedGlyph(
            pixels,
            stride,
            1710,
            819,
            5,
            11,
            digit1);

        // Neutral unit-word stub begins at the observed x=1723, leaving an
        // 8 px separator after the visible digit 1.
        for (int glyph = 0;
             glyph < 6;
             glyph++)
        {
            int glyphLeft =
                1723
                + glyph * 9;

            for (int y = 819;
                 y < 830;
                 y++)
            {
                for (int x = glyphLeft;
                     x < glyphLeft + 6;
                     x++)
                {
                    if (y == 819
                        || y == 829
                        || x == glyphLeft
                        || x == glyphLeft + 5)
                    {
                        SetBgra(
                            pixels,
                            stride,
                            x,
                            y,
                            170,
                            170,
                            170);
                    }
                }
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

    private static void PaintRecordedGlyph(
        byte[] pixels,
        int stride,
        int left,
        int top,
        int width,
        int height,
        byte[] luma)
    {
        Assert.Equal(
            width * height,
            luma.Length);

        for (int y = 0;
             y < height;
             y++)
        {
            for (int x = 0;
                 x < width;
                 x++)
            {
                byte value =
                    luma[
                        y * width + x];

                if (value < 25)
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
    private static DssCapturedFrame BuildSyntheticFrame(
        int target,
        DateTimeOffset? timestampUtc = null)
    {
        const int width = 1920;
        const int height = 1080;
        const int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        // Cyan label guard in the expected "Optimal probes" line.
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

        return new DssCapturedFrame(
            timestampUtc
                ?? DateTimeOffset.UtcNow,
            0,
            0,
            width,
            height,
            stride,
            pixels);
    }

    private static DssCapturedFrame BuildSyntheticEnglishFrame(
        int target,
        DateTimeOffset? timestampUtc = null,
        int width = 1920,
        int height = 1080)
    {
        int stride = width * 4;

        byte[] pixels =
            new byte[
                stride * height];

        // Cyan guard for the native "EFFICIENCY TARGET" header.
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

        // Matches the supplied 1920x1080 English placement: "6 PROBES".
        int cursor =
            width - 212;

        if (target >= 10)
        {
            cursor =
                PaintDigitLeftAligned(
                    pixels,
                    stride,
                    cursor,
                    819,
                    target / 10)
                + 2;
        }

        cursor =
            PaintDigitLeftAligned(
                pixels,
                stride,
                cursor,
                819,
                target % 10);

        // Neutral unit-word stub. The detector does not OCR this content; it
        // only uses the wider separator after the numeric token.
        // Observed live English HUD uses about a 6 px separator at ~1080p.
        int unitLeft =
            cursor + 6;

        for (int glyph = 0;
             glyph < 6;
             glyph++)
        {
            int glyphLeft =
                unitLeft
                + glyph * 8;

            for (int y = 819;
                 y < 833;
                 y++)
            {
                for (int x = glyphLeft;
                     x < glyphLeft + 5;
                     x++)
                {
                    if (y == 819
                        || y == 832
                        || x == glyphLeft
                        || x == glyphLeft + 4)
                    {
                        SetBgra(
                            pixels,
                            stride,
                            x,
                            y,
                            170,
                            170,
                            170);
                    }
                }
            }
        }

        return new DssCapturedFrame(
            timestampUtc
                ?? DateTimeOffset.UtcNow,
            0,
            0,
            width,
            height,
            stride,
            pixels);
    }

    private static int PaintDigitLeftAligned(
        byte[] pixels,
        int stride,
        int left,
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
                    left
                    + (x - firstColumn),
                    top + y,
                    value,
                    value,
                    value);
            }
        }

        return
            left + visibleWidth;
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

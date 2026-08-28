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

        // Live v48 calibration case:
        // native Elite target 18, PatchRadius 26/20 = 1.30x area,
        // completed in 15 probes. The area-scaled model resolves the same N.
        Assert.Equal(
            15,
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

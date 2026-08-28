using System;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssAdaptiveTargetingTests
{
    [Fact]
    public void UnknownSettingsTarget_UsesSixShotFirstWave()
    {
        DssPredictiveAimTarget stepSix =
            DssPredictiveBatchPlanner.Resolve(
                6,
                12,
                "SETTINGS",
                24,
                confirmedImpactCount: 0,
                coverageObservation: null,
                usedCoverageCandidates: 0);

        DssPredictiveAimTarget stepSeven =
            DssPredictiveBatchPlanner.Resolve(
                7,
                12,
                "SETTINGS",
                24,
                confirmedImpactCount: 0,
                coverageObservation: null,
                usedCoverageCandidates: 0);

        Assert.True(stepSix.Available);
        Assert.False(stepSeven.Available);
    }

    [Fact]
    public void UnknownCorrectionTail_ReleasesOnlyOneShotPerImpact()
    {
        DssPredictiveAimTarget seven =
            DssPredictiveBatchPlanner.Resolve(
                7,
                12,
                "SETTINGS",
                24,
                confirmedImpactCount: 6,
                coverageObservation: DssCoverageObservation.Empty,
                usedCoverageCandidates: 0);

        DssPredictiveAimTarget eightWhileSevenFlying =
            DssPredictiveBatchPlanner.Resolve(
                8,
                12,
                "SETTINGS",
                24,
                confirmedImpactCount: 6,
                coverageObservation: DssCoverageObservation.Empty,
                usedCoverageCandidates: 0);

        DssPredictiveAimTarget eightAfterSevenImpact =
            DssPredictiveBatchPlanner.Resolve(
                8,
                12,
                "SETTINGS",
                24,
                confirmedImpactCount: 7,
                coverageObservation: DssCoverageObservation.Empty,
                usedCoverageCandidates: 0);

        Assert.True(seven.Available);
        Assert.False(eightWhileSevenFlying.Available);
        Assert.True(eightAfterSevenImpact.Available);
    }

    [Fact]
    public void SeventhShot_UsesObservedCoverageDirectionNearLimb()
    {
        var coverage =
            new DssCoverageObservation(
                true,
                false,
                0.66,
                1.0,
                3,
                0,
                0.68,
                0.61);

        DssPredictiveAimTarget target =
            DssPredictiveBatchPlanner.Resolve(
                7,
                12,
                "SETTINGS",
                26.6,
                confirmedImpactCount: 6,
                coverageObservation: coverage,
                usedCoverageCandidates: 0);

        Assert.True(target.Available);
        Assert.Equal(
            "CORRECTION_COVERAGE_NEAR",
            target.Role);

        Assert.InRange(
            target.NormalizedX,
            -0.000001,
            0.000001);

        Assert.InRange(
            target.NormalizedY,
            0.919,
            0.921);
    }

    [Fact]
    public void ThirteenPointKnownBatch_ReservesCenterAndMultipleFarShots()
    {
        int far = 0;
        bool hasCenter = false;

        for (int step = 1;
             step <= 13;
             step++)
        {
            DssPredictiveAimTarget target =
                DssPredictiveBatchPlanner.Resolve(
                    step,
                    13,
                    "BODY",
                    24,
                    confirmedImpactCount: 0,
                    coverageObservation: null,
                    usedCoverageCandidates: 0);

            Assert.True(target.Available);

            double radius =
                Math.Sqrt(
                    target.NormalizedX
                    * target.NormalizedX
                    + target.NormalizedY
                      * target.NormalizedY);

            if (radius > 1.0d)
                far++;

            if (radius < 0.0001d)
                hasCenter = true;
        }

        Assert.True(hasCenter);
        Assert.True(far >= 6);
    }
}

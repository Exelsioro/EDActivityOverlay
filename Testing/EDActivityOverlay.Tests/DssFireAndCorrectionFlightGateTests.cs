using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

[Collection("DssNativeEfficiencyTargetRuntime")]
public sealed class DssFireAndCorrectionFlightGateTests : IDisposable
{
    public DssFireAndCorrectionFlightGateTests()
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
    public void NonCenterVisualTarget_IsNotHeldAcrossSolverDropout()
    {
        var latch =
            new DssAimVisualTargetLatch();

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        DssProjectedAimPlan live =
            Plan(
                sequence: 11,
                x: 0.65d,
                y: -0.75d);

        Assert.True(
            latch.Resolve(
                    now,
                    10,
                    false,
                    live)
                .IsAvailable);

        Assert.False(
            latch.Resolve(
                    now.AddMilliseconds(1),
                    10,
                    false,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);
    }

    [Fact]
    public void CenterVisualTarget_RemainsHeldUntilStepChanges()
    {
        var latch =
            new DssAimVisualTargetLatch();

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        DssProjectedAimPlan center =
            Plan(
                sequence: 18,
                x: 0d,
                y: 0d);

        Assert.True(
            latch.Resolve(
                    now,
                    18,
                    false,
                    center)
                .IsAvailable);

        Assert.True(
            latch.Resolve(
                    now.AddSeconds(5),
                    18,
                    false,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);

        Assert.False(
            latch.Resolve(
                    now.AddSeconds(5),
                    19,
                    false,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);
    }

    [Fact]
    public void ExtraEarlierHit_CannotAuthorizeNextCorrectionWhilePreviousIsInFlight()
    {
        DssNativeScanProgressRuntime.ResetForTests();

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 87,
            hits: 19,
            stableAge: TimeSpan.FromSeconds(5));

        DssNativeScanProgressRuntime.ObserveTargetingStep(
            targetN: 18,
            sequentialStep: 19);

        Assert.True(
            DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredHitCount: 18,
                correctionIndex: 1,
                out _));

        // Fire correction #19: fire-owned state advances 19 -> 20 while native
        // hits is still 19. That 19 may include an older duplicate shot.
        DssNativeScanProgressRuntime.ObserveTargetingStep(
            targetN: 18,
            sequentialStep: 20);

        Assert.False(
            DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredHitCount: 19,
                correctionIndex: 2,
                out _));

        DssNativeScanProgressRuntime.ResetForTests();
    }

    [Fact]
    public void NativeHundredPercent_SuppressesCorrectionAfterPreviousLands()
    {
        DssNativeScanProgressRuntime.ResetForTests();

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 87,
            hits: 19,
            stableAge: TimeSpan.FromSeconds(5));

        DssNativeScanProgressRuntime.ObserveTargetingStep(
            targetN: 18,
            sequentialStep: 19);

        DssNativeScanProgressRuntime.ObserveTargetingStep(
            targetN: 18,
            sequentialStep: 20);

        DssNativeScanProgressRuntime.SetForTests(
            coverage: 100,
            hits: 20,
            stableAge: TimeSpan.FromSeconds(3));

        Assert.False(
            DssNativeScanProgressRuntime.CanOfferCorrection(
                requiredHitCount: 19,
                correctionIndex: 2,
                out _));

        DssNativeScanProgressRuntime.ResetForTests();
    }

    private static DssProjectedAimPlan Plan(
        int sequence,
        double x,
        double y) =>
        new(
            18,
            "TARGETING_TEST",
            new[]
            {
                new DssProjectedAimPoint(
                    sequence,
                    x,
                    y,
                    0d,
                    0d,
                    DssAimZone.Disc)
            });
}

using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssActionableTargetLeaseTests
{
    [Fact]
    public void SameStep_RawDropout_KeepsActionableTarget()
    {
        DssActionableTargetLeaseRuntime.ResetForTests();

        GameStateSnapshot state =
            Context(
                systemAddress: 123,
                bodyId: 7,
                bodyName: "Test 7");

        DssHudGeometry geometry =
            Geometry(
                centerX: 900d,
                centerY: 500d,
                horizonRadius: 240d);

        DssProjectedAimPlan original =
            Plan(
                step: 15,
                x: -0.64d,
                y: 0.41d);

        DssProjectedAimPlan first =
            DssActionableTargetLeaseRuntime.Resolve(
                state,
                sequentialStep: 15,
                scanComplete: false,
                geometry,
                original);

        Assert.True(
            first.IsAvailable);

        DssHudGeometry movedGeometry =
            Geometry(
                centerX: 1000d,
                centerY: 600d,
                horizonRadius: 250d);

        DssProjectedAimPlan held =
            DssActionableTargetLeaseRuntime.Resolve(
                state,
                sequentialStep: 15,
                scanComplete: false,
                movedGeometry,
                DssProjectedAimPlan.Empty);

        Assert.True(
            held.IsAvailable);

        Assert.Equal(
            15,
            held.Points[0].Sequence);

        Assert.Equal(
            -0.64d,
            held.Points[0].NormalizedX,
            12);

        Assert.Equal(
            0.41d,
            held.Points[0].NormalizedY,
            12);

        Assert.Equal(
            1000d - 0.64d * 250d,
            held.Points[0].ScreenX,
            12);

        Assert.Equal(
            600d + 0.41d * 250d,
            held.Points[0].ScreenY,
            12);

        DssActionableTargetLeaseRuntime.ResetForTests();
    }

    [Fact]
    public void StepChange_DoesNotLeakPreviousTarget()
    {
        DssActionableTargetLeaseRuntime.ResetForTests();

        GameStateSnapshot state =
            Context(
                123,
                7,
                "Test 7");

        DssHudGeometry geometry =
            Geometry(
                900d,
                500d,
                240d);

        Assert.True(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    15,
                    false,
                    geometry,
                    Plan(
                        15,
                        -0.64d,
                        0.41d))
                .IsAvailable);

        Assert.False(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    16,
                    false,
                    geometry,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);

        DssActionableTargetLeaseRuntime.ResetForTests();
    }

    [Fact]
    public void GeometryLoss_HidesButDoesNotDestroyLease()
    {
        DssActionableTargetLeaseRuntime.ResetForTests();

        GameStateSnapshot state =
            Context(
                123,
                7,
                "Test 7");

        DssHudGeometry good =
            Geometry(
                900d,
                500d,
                240d);

        DssProjectedAimPlan plan =
            Plan(
                15,
                -0.64d,
                0.41d);

        Assert.True(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    15,
                    false,
                    good,
                    plan)
                .IsAvailable);

        DssHudGeometry bad =
            good with
            {
                BodyCenterFound = false
            };

        Assert.False(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    15,
                    false,
                    bad,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);

        Assert.True(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    15,
                    false,
                    good,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);

        DssActionableTargetLeaseRuntime.ResetForTests();
    }

    [Fact]
    public void ScanComplete_ClearsLease()
    {
        DssActionableTargetLeaseRuntime.ResetForTests();

        GameStateSnapshot state =
            Context(
                123,
                7,
                "Test 7");

        DssHudGeometry geometry =
            Geometry(
                900d,
                500d,
                240d);

        Assert.True(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    15,
                    false,
                    geometry,
                    Plan(
                        15,
                        -0.64d,
                        0.41d))
                .IsAvailable);

        Assert.False(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    15,
                    true,
                    geometry,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);

        Assert.False(
            DssActionableTargetLeaseRuntime.Resolve(
                    state,
                    15,
                    false,
                    geometry,
                    DssProjectedAimPlan.Empty)
                .IsAvailable);

        DssActionableTargetLeaseRuntime.ResetForTests();
    }

    private static DssProjectedAimPlan Plan(
        int step,
        double x,
        double y) =>
        new(
            17,
            "TARGETING_TEST",
            new[]
            {
                new DssProjectedAimPoint(
                    step,
                    x,
                    y,
                    0d,
                    0d,
                    DssAimZone.Disc)
            });

    private static DssHudGeometry Geometry(
        double centerX,
        double centerY,
        double horizonRadius) =>
        new(
            ReticleX: 960,
            ReticleY: 540,
            BodyCenterFound: true,
            BodyCenterX: centerX,
            BodyCenterY: centerY,
            BodyCenterConfidence: 1d,
            HorizonMarkerFound: true,
            HorizonMarkerObserved: true,
            HorizonMarkerX: centerX,
            HorizonMarkerY: centerY + horizonRadius,
            HorizonMarkerConfidence: 1d,
            HorizonObservationAgeMilliseconds: 0d,
            HorizonRadiusPixels: horizonRadius,
            HorizonAimErrorPixels: 0d,
            AimOffsetDegrees: 0d);

    private static GameStateSnapshot Context(
        long systemAddress,
        int bodyId,
        string bodyName) =>
        GameStateSnapshot.Empty with
        {
            SystemAddress = systemAddress,
            DestinationBodyId = bodyId,
            DestinationName = bodyName
        };
}

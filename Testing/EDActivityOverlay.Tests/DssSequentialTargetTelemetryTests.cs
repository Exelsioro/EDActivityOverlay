using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

[Collection("DssNativeEfficiencyTargetRuntime")]
public sealed class DssSequentialTargetTelemetryTests : IDisposable
{
    public DssSequentialTargetTelemetryTests()
    {
        DssNativeEfficiencyTargetRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
        DssActionableTargetLeaseRuntime.ResetForTests();

        DssNativeEfficiencyTargetRuntime.SetForTests(
            3);
    }

    public void Dispose()
    {
        DssActionableTargetLeaseRuntime.ResetForTests();
        DssNativeScanProgressRuntime.ResetForTests();
        DssNativeEfficiencyTargetRuntime.ResetForTests();
    }

    [Fact]
    public void StepTwo_CapturesResolvedTargetAndPixelError()
    {
        DssProbeLaunchFrameSnapshot frame =
            CreateFrameSnapshot(24d);

        DssProjectedAimPlan expectedPlan =
            DssProbeAimSolver.Solve(
                frame.State,
                frame.Readiness,
                frame.Geometry,
                sequentialStep: 2,
                scanComplete: false,
                frame.DssModule
                    ?? DssModuleSnapshot.Empty,
                frame.ConfirmedImpactCount,
                frame.CoverageObservation,
                frame.UsedCoverageCandidates);

        Assert.True(
            expectedPlan.IsAvailable);

        DssProjectedAimPoint expected =
            expectedPlan.Points[0];

        DssProbeLaunchRecord launch =
            CreateLaunch(
                geometryValid: true,
                aimX: expected.NormalizedX + 0.02d,
                aimY: expected.NormalizedY,
                horizonRadiusPixels: 200);

        DssSequentialTargetTelemetry telemetry =
            DssSequentialTargetTelemetryBuilder.Build(
                2,
                scanComplete: false,
                frame,
                launch);

        Assert.True(
            telemetry.Available);

        Assert.Equal(
            2,
            telemetry.Step);

        Assert.Equal(
            expected.NormalizedX,
            telemetry.NormalizedX,
            8);

        Assert.Equal(
            expected.NormalizedY,
            telemetry.NormalizedY,
            8);

        Assert.Equal(
            Math.Sqrt(
                expected.NormalizedX * expected.NormalizedX
                + expected.NormalizedY * expected.NormalizedY),
            telemetry.NormalizedRadius,
            8);

        Assert.InRange(
            telemetry.ErrorPixels,
            3.999,
            4.001);
    }
    [Fact]
    public void CenterStep_RemainsIdentifiableWhenLaunchGeometryIsInvalid()
    {
        DssProbeLaunchFrameSnapshot frame =
            CreateFrameSnapshot(24d);

        DssProbeLaunchRecord launch =
            CreateLaunch(
                geometryValid: false,
                aimX: 0,
                aimY: 0,
                horizonRadiusPixels: 0);

        DssSequentialTargetTelemetry telemetry =
            DssSequentialTargetTelemetryBuilder.Build(
                3,
                scanComplete: false,
                frame,
                launch);

        Assert.True(telemetry.Available);
        Assert.Equal(3, telemetry.Step);
        Assert.Equal(0d, telemetry.NormalizedX);
        Assert.Equal(0d, telemetry.NormalizedY);
        Assert.Equal(0d, telemetry.NormalizedRadius);
        Assert.Equal(-1d, telemetry.ErrorPixels);
    }

    [Fact]
    public void ZeroAngularDiameter_IsNotReportedAsAvailableTarget()
    {
        DssProbeLaunchFrameSnapshot frame =
            CreateFrameSnapshot(0d);

        DssSequentialTargetTelemetry telemetry =
            DssSequentialTargetTelemetryBuilder.Build(
                1,
                scanComplete: false,
                frame,
                CreateLaunch(
                    true,
                    0,
                    -1,
                    200));

        Assert.False(
            telemetry.Available);

        Assert.Equal(
            -1d,
            telemetry.ErrorPixels);
    }
    private static DssProbeLaunchFrameSnapshot CreateFrameSnapshot(
        double angularDiameterDegrees) =>
        new(
            1,
            DateTimeOffset.UtcNow,
            GameStateSnapshot.Empty,
            new DssAssistantReadinessSnapshot(
                DssAssistantReadinessState.Ready,
                true,
                0,
                angularDiameterDegrees / 2d,
                angularDiameterDegrees,
                0,
                0,
                0,
                0,
                0),
            new DssHudGeometry(
                960,
                540,
                true,
                960,
                540,
                0.9,
                true,
                true,
                960,
                740,
                0.9,
                0,
                200,
                0,
                0));

    private static DssProbeLaunchRecord CreateLaunch(
        bool geometryValid,
        double aimX,
        double aimY,
        double horizonRadiusPixels)
    {
        double radius =
            Math.Sqrt(aimX * aimX + aimY * aimY);

        return new DssProbeLaunchRecord(
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            1,
            "PrimaryFire",
            "Primary",
            "Keyboard",
            "Space",
            geometryValid,
            "Ready",
            24,
            960,
            540,
            horizonRadiusPixels,
            960,
            540,
            aimX,
            aimY,
            radius,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            false,
            0);
    }
}

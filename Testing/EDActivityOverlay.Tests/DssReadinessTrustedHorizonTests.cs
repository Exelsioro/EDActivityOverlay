using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssReadinessTrustedHorizonTests
{
    [Fact]
    public void TrustedTrackedHorizonKeepsReadyAfterFrontierMarkerBlink()
    {
        var evaluator =
            new DssAssistantReadinessEvaluator();

        GameStateSnapshot state =
            GameStateSnapshot.Empty with
            {
                SystemAddress = 123,
                DestinationSystemAddress = 123,
                DestinationBodyId = 10,
                DestinationName = "Test 10"
            };

        var context =
            new DssPrototypeSessionContext(
                "Commander",
                "Test",
                123,
                "Test 10",
                10,
                11_064_317,
                56.817001,
                26,
                20,
                "Sensor_Expanded",
                3,
                1920,
                1080);

        DateTimeOffset t0 =
            DateTimeOffset.UtcNow;

        DssCapturedFrame directFrame =
            CreateFrame(t0);

        DssHudGeometry directGeometry =
            GeometryForAngularRadius(
                directFrame,
                14,
                observed: true);

        DssAssistantReadinessSnapshot first =
            evaluator.Evaluate(
                state,
                context,
                directFrame,
                directGeometry);

        Assert.Equal(
            DssAssistantReadinessState.Ready,
            first.State);

        // More than the old 2.5 s readiness timeout later Frontier's white
        // horizon triplet is absent, but the tracker still owns trusted Rh and
        // reconstructs the horizon from the live centre.
        DssCapturedFrame blinkFrame =
            CreateFrame(
                t0 + TimeSpan.FromSeconds(4));

        DssHudGeometry trackedGeometry =
            GeometryForAngularRadius(
                blinkFrame,
                14,
                observed: false);

        DssAssistantReadinessSnapshot second =
            evaluator.Evaluate(
                state,
                context,
                blinkFrame,
                trackedGeometry);

        Assert.Equal(
            DssAssistantReadinessState.Ready,
            second.State);

        Assert.True(
            second.MeasurementAgeMilliseconds > 2500);

        Assert.True(
            second.HasAngularMeasurement);
    }

    private static DssCapturedFrame CreateFrame(
        DateTimeOffset timestamp) =>
        new(
            timestamp,
            0,
            0,
            1920,
            1080,
            1920 * 4,
            new byte[1920 * 1080 * 4]);

    private static DssHudGeometry GeometryForAngularRadius(
        DssCapturedFrame frame,
        double angularRadiusDegrees,
        bool observed)
    {
        double focal =
            DssHudGeometryDetector.GetFocalPixels(
                frame.Height,
                56.817001);

        double horizonY =
            frame.Height / 2d
            + focal
              * Math.Tan(
                  angularRadiusDegrees
                  * Math.PI / 180d);

        return new DssHudGeometry(
            frame.Width / 2,
            frame.Height / 2,
            true,
            frame.Width / 2d,
            frame.Height / 2d,
            0.98,
            true,
            observed,
            frame.Width / 2d,
            horizonY,
            0.82,
            observed ? 0 : 4000,
            Math.Abs(
                horizonY
                - frame.Height / 2d),
            0,
            0);
    }
}

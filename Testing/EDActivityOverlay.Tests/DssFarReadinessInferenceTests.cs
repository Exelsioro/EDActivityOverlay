using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssFarReadinessInferenceTests
{
    [Fact]
    public void StableCenterWithoutHorizon_InfersTooFarAfterDelay()
    {
        var evaluator = new DssAssistantReadinessEvaluator();
        GameStateSnapshot state = CreateTargetState();
        DssPrototypeSessionContext context = CreateContext();

        DateTimeOffset start =
            new(2026, 8, 26, 14, 0, 0, TimeSpan.Zero);

        DssAssistantReadinessSnapshot initial =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start),
                CenterOnlyGeometry());

        Assert.Equal(
            DssAssistantReadinessState.Calibrating,
            initial.State);

        DssAssistantReadinessSnapshot beforeDelay =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddMilliseconds(2900)),
                CenterOnlyGeometry());

        Assert.Equal(
            DssAssistantReadinessState.Calibrating,
            beforeDelay.State);

        DssAssistantReadinessSnapshot inferred =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddMilliseconds(3100)),
                CenterOnlyGeometry());

        Assert.Equal(
            DssAssistantReadinessState.TooFar,
            inferred.State);

        Assert.False(inferred.HasAngularMeasurement);
        Assert.False(inferred.HasDistanceEstimate);
        Assert.Equal(0d, inferred.AngularDiameterDegrees);
    }

    [Fact]
    public void RealHorizonBeforeDelay_WinsAndBecomesReady()
    {
        var evaluator = new DssAssistantReadinessEvaluator();
        GameStateSnapshot state = CreateTargetState();
        DssPrototypeSessionContext context = CreateContext();

        DateTimeOffset start =
            new(2026, 8, 26, 14, 0, 0, TimeSpan.Zero);

        _ = evaluator.Evaluate(
            state,
            context,
            CreateFrame(start),
            CenterOnlyGeometry());

        DssCapturedFrame measuredFrame =
            CreateFrame(start.AddMilliseconds(2500));

        DssAssistantReadinessSnapshot measured =
            evaluator.Evaluate(
                state,
                context,
                measuredFrame,
                GeometryForAngularRadius(
                    measuredFrame,
                    14d));

        Assert.Equal(
            DssAssistantReadinessState.Ready,
            measured.State);

        Assert.InRange(
            measured.AngularDiameterDegrees,
            27.99,
            28.01);
    }

    private static GameStateSnapshot CreateTargetState() =>
        GameStateSnapshot.Empty with
        {
            SystemAddress = 123,
            DestinationSystemAddress = 123,
            DestinationBodyId = 10,
            DestinationName = "Test 10"
        };

    private static DssPrototypeSessionContext CreateContext() =>
        new(
            "Test",
            "Test",
            123,
            "Test 10",
            10,
            11_064_317d,
            56.817001,
            26,
            20,
            "Sensor_Expanded",
            3,
            1920,
            1080);

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

    private static DssHudGeometry CenterOnlyGeometry() =>
        new(
            960,
            540,
            true,
            760,
            540,
            0.98,
            false,
            false,
            0,
            0,
            0,
            -1,
            0,
            0,
            0);

    private static DssHudGeometry GeometryForAngularRadius(
        DssCapturedFrame frame,
        double angularRadiusDegrees)
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
            0.9,
            true,
            true,
            frame.Width / 2d,
            horizonY,
            0.9,
            0,
            Math.Abs(
                horizonY
                - frame.Height / 2d),
            0,
            0);
    }
}

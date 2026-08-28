using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssFarReadinessUnknownRadiusTests
{
    [Fact]
    public void StableCenterWithoutHorizon_InfersTooFarWhenRadiusIsUnknown()
    {
        var evaluator = new DssAssistantReadinessEvaluator();

        GameStateSnapshot state =
            GameStateSnapshot.Empty with
            {
                SystemAddress = 123,
                DestinationSystemAddress = 123,
                DestinationBodyId = 10,
                DestinationName = "Unknown radius body"
            };

        DssPrototypeSessionContext context =
            new(
                "Test",
                "Test",
                123,
                "Unknown radius body",
                10,
                0,
                56.817001,
                26,
                20,
                "Sensor_Expanded",
                3,
                1920,
                1080);

        DateTimeOffset start =
            new(2026, 8, 26, 15, 0, 0, TimeSpan.Zero);

        _ = evaluator.Evaluate(
            state,
            context,
            CreateFrame(start),
            CenterOnlyGeometry());

        DssAssistantReadinessSnapshot inferred =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddMilliseconds(3100)),
                CenterOnlyGeometry());

        Assert.Equal(
            DssAssistantReadinessState.TooFar,
            inferred.State);

        Assert.Equal(0d, inferred.BodyRadiusMeters);
        Assert.False(inferred.HasAngularMeasurement);
        Assert.False(inferred.HasDistanceEstimate);
        Assert.Equal(0d, inferred.AngularDiameterDegrees);
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
}

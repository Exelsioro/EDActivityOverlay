using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssFarReadinessAimOffsetGuardTests
{
    [Fact]
    public void LargeAimOffset_DoesNotInferTooFarAfterDelay()
    {
        var evaluator = new DssAssistantReadinessEvaluator();
        GameStateSnapshot state = CreateTargetState();
        DssPrototypeSessionContext context = CreateContext();

        DateTimeOffset start =
            new(2026, 8, 26, 16, 0, 0, TimeSpan.Zero);

        _ = evaluator.Evaluate(
            state,
            context,
            CreateFrame(start),
            CenterOnlyGeometry(15.5));

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddSeconds(6)),
                CenterOnlyGeometry(21.5));

        Assert.Equal(
            DssAssistantReadinessState.Calibrating,
            result.State);

        Assert.False(result.HasAngularMeasurement);
    }

    [Fact]
    public void Recentering_RestartsThreeSecondInferenceClock()
    {
        var evaluator = new DssAssistantReadinessEvaluator();
        GameStateSnapshot state = CreateTargetState();
        DssPrototypeSessionContext context = CreateContext();

        DateTimeOffset start =
            new(2026, 8, 26, 16, 10, 0, TimeSpan.Zero);

        _ = evaluator.Evaluate(
            state,
            context,
            CreateFrame(start),
            CenterOnlyGeometry(18));

        _ = evaluator.Evaluate(
            state,
            context,
            CreateFrame(start.AddSeconds(4)),
            CenterOnlyGeometry(18));

        DssAssistantReadinessSnapshot recentered =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddSeconds(4.1)),
                CenterOnlyGeometry(12.2));

        Assert.Equal(
            DssAssistantReadinessState.Calibrating,
            recentered.State);

        DssAssistantReadinessSnapshot beforeDelay =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddSeconds(7.0)),
                CenterOnlyGeometry(12.2));

        Assert.Equal(
            DssAssistantReadinessState.Calibrating,
            beforeDelay.State);

        DssAssistantReadinessSnapshot afterDelay =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddSeconds(7.2)),
                CenterOnlyGeometry(12.2));

        Assert.Equal(
            DssAssistantReadinessState.TooFar,
            afterDelay.State);
    }

    [Fact]
    public void RecordedFarFamily_StillInfersTooFar()
    {
        var evaluator = new DssAssistantReadinessEvaluator();
        GameStateSnapshot state = CreateTargetState();
        DssPrototypeSessionContext context = CreateContext();

        DateTimeOffset start =
            new(2026, 8, 26, 16, 20, 0, TimeSpan.Zero);

        _ = evaluator.Evaluate(
            state,
            context,
            CreateFrame(start),
            CenterOnlyGeometry(12.23));

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                CreateFrame(start.AddMilliseconds(3100)),
                CenterOnlyGeometry(12.23));

        Assert.Equal(
            DssAssistantReadinessState.TooFar,
            result.State);
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
            0,
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

    private static DssHudGeometry CenterOnlyGeometry(
        double aimOffsetDegrees) =>
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
            aimOffsetDegrees);
}

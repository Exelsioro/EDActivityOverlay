using System;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Dss;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class DssAssistantReadinessTests
{
    [Fact]
    public void ScreenRayAngleIsIndependentOfPixelRadiusShortcut()
    {
        const int width = 1920;
        const int height = 1080;
        const double fov = 56.817001;
        const double expectedDegrees = 14;

        double focal =
            DssHudGeometryDetector.GetFocalPixels(
                height,
                fov);

        double horizonY =
            height / 2d
            + focal
              * Math.Tan(
                  expectedDegrees
                  * Math.PI / 180d);

        double actual =
            DssAssistantReadinessEvaluator
                .CalculateAngularSeparationDegrees(
                    width,
                    height,
                    fov,
                    width / 2d,
                    height / 2d,
                    width / 2d,
                    horizonY);

        Assert.InRange(
            actual,
            13.99,
            14.01);
    }

    [Fact]
    public void RecommendedDistanceForRecordedBodyMatchesCurrentReadyBand()
    {
        const double radiusMeters =
            11_064_317d;

        (double near,
         double target,
         double far) =
            DssAssistantReadinessEvaluator
                .CalculateRecommendedCenterDistancesMeters(
                    radiusMeters);

        // Current production band:
        // near = 28° diameter, target = 23°, far = 21.5°.
        Assert.InRange(
            near / 1_000_000d,
            45.6,
            45.9);

        Assert.InRange(
            target / 1_000_000d,
            55.3,
            55.7);

        Assert.InRange(
            far / 1_000_000d,
            59.1,
            59.5);
    }
    [Fact]
    public void MissingBodyTargetBlocksReadiness()
    {
        var evaluator =
            new DssAssistantReadinessEvaluator();

        var state =
            GameStateSnapshot.Empty with
            {
                SystemAddress = 123
            };

        DssPrototypeSessionContext context =
            CreateContext(
                bodyId: 10,
                radiusMeters: 11_064_317);

        DssCapturedFrame frame =
            CreateFrame();

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                frame,
                GeometryForAngularRadius(
                    frame,
                    14));

        Assert.Equal(
            DssAssistantReadinessState.SelectBodyTarget,
            result.State);
    }

    [Fact]
    public void TwentyEightDegreeBodyIsReady()
    {
        var evaluator =
            new DssAssistantReadinessEvaluator();

        GameStateSnapshot state =
            CreateTargetState();

        DssPrototypeSessionContext context =
            CreateContext(
                bodyId: 10,
                radiusMeters: 11_064_317);

        DssCapturedFrame frame =
            CreateFrame();

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                frame,
                GeometryForAngularRadius(
                    frame,
                    14));

        Assert.Equal(
            DssAssistantReadinessState.Ready,
            result.State);

        Assert.InRange(
            result.AngularDiameterDegrees,
            27.99,
            28.01);

        Assert.InRange(
            result.EstimatedCenterDistanceMeters
            / 1_000_000d,
            45.6,
            45.9);
    }

    [Fact]
    public void FiftyFiveDegreeBodyIsTooClose()
    {
        var evaluator =
            new DssAssistantReadinessEvaluator();

        GameStateSnapshot state =
            CreateTargetState();

        DssPrototypeSessionContext context =
            CreateContext(
                bodyId: 10,
                radiusMeters: 11_064_317);

        DssCapturedFrame frame =
            CreateFrame();

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                frame,
                GeometryForAngularRadius(
                    frame,
                    27.5));

        Assert.Equal(
            DssAssistantReadinessState.TooClose,
            result.State);
    }

    [Fact]
    public void TwentyThreeDegreeBodyIsReadyAtFarEdge()
    {
        var evaluator =
            new DssAssistantReadinessEvaluator();

        GameStateSnapshot state =
            CreateTargetState();

        DssPrototypeSessionContext context =
            CreateContext(
                bodyId: 10,
                radiusMeters: 11_064_317);

        DssCapturedFrame frame =
            CreateFrame();

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                frame,
                GeometryForAngularRadius(
                    frame,
                    11.5));

        Assert.Equal(
            DssAssistantReadinessState.Ready,
            result.State);

        Assert.True(
            result.IsFarReadyEdge);
    }

    [Fact]
    public void EighteenDegreeBodyIsTooFar()
    {
        var evaluator =
            new DssAssistantReadinessEvaluator();

        GameStateSnapshot state =
            CreateTargetState();

        DssPrototypeSessionContext context =
            CreateContext(
                bodyId: 10,
                radiusMeters: 11_064_317);

        DssCapturedFrame frame =
            CreateFrame();

        DssAssistantReadinessSnapshot result =
            evaluator.Evaluate(
                state,
                context,
                frame,
                GeometryForAngularRadius(
                    frame,
                    9));

        Assert.Equal(
            DssAssistantReadinessState.TooFar,
            result.State);
    }

    private static GameStateSnapshot
        CreateTargetState() =>
        GameStateSnapshot.Empty with
        {
            SystemAddress = 123,
            DestinationSystemAddress = 123,
            DestinationBodyId = 10,
            DestinationName = "Test 10"
        };

    private static DssPrototypeSessionContext
        CreateContext(
            int bodyId,
            double radiusMeters) =>
        new(
            "Test",
            "Test",
            123,
            $"Test {bodyId}",
            bodyId,
            radiusMeters,
            56.817001,
            26,
            20,
            "Sensor_Expanded",
            3,
            1920,
            1080);

    private static DssCapturedFrame
        CreateFrame() =>
        new(
            DateTimeOffset.UtcNow,
            0,
            0,
            1920,
            1080,
            1920 * 4,
            new byte[1920 * 1080 * 4]);

    private static DssHudGeometry
        GeometryForAngularRadius(
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

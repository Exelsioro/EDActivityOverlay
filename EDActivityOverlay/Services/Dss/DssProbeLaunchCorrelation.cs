using System;
using System.Linq;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssProbeLaunchFrameSnapshot(
    long FrameSequence,
    DateTimeOffset FrameUtc,
    GameStateSnapshot State,
    DssAssistantReadinessSnapshot Readiness,
    DssHudGeometry Geometry,
    DssAimMissObservation? MissObservation = null,
    DssCoverageObservation? CoverageObservation = null,
    int ConfirmedImpactCount = 0,
    long UsedCoverageCandidates = 0);

internal sealed record DssProbeLaunchRecord(
    int LaunchSequence,
    DateTimeOffset InputUtc,
    DateTimeOffset FrameUtc,
    double FrameAgeMilliseconds,
    long FrameSequence,
    string FireAction,
    string BindingSlot,
    string BindingDevice,
    string BindingKey,
    bool GeometryValid,
    string ReadinessState,
    double AngularDiameterDegrees,
    double BodyCenterX,
    double BodyCenterY,
    double HorizonRadiusPixels,
    double ReticleX,
    double ReticleY,
    double AimNormalizedX,
    double AimNormalizedY,
    double AimNormalizedRadius,
    double AimAngleDegrees,
    int NearestPatternPoint,
    double NearestPatternX,
    double NearestPatternY,
    double NearestErrorNormalized,
    double NearestErrorPixels,
    int EfficiencyTarget,
    string PatternSource,
    bool HudMissVisible = false,
    double HudMissActiveRatio = 0);

internal sealed record DssSequentialTargetTelemetry(
    int Step,
    bool Available,
    double NormalizedX,
    double NormalizedY,
    double NormalizedRadius,
    double ErrorPixels,
    int CandidateId = 0,
    string TargetSource = "",
    double CoverageFraction = 0,
    double UncoveredScore = 0)
{
    public static DssSequentialTargetTelemetry Empty(
        int step) =>
        new(
            step,
            false,
            0,
            0,
            0,
            -1);
}

internal static class DssSequentialTargetTelemetryBuilder
{
    public static DssSequentialTargetTelemetry Build(
        int sequentialStep,
        bool scanComplete,
        DssProbeLaunchFrameSnapshot? frame,
        DssProbeLaunchRecord launch)
    {
        if (scanComplete
            || frame is null)
        {
            return DssSequentialTargetTelemetry.Empty(
                sequentialStep);
        }

        double angularDiameterDegrees =
            frame.Readiness.AngularDiameterDegrees;

        if (!DssProbeAimSolver.TryResolvePredictiveTarget(
                frame.State,
                sequentialStep,
                angularDiameterDegrees,
                frame.ConfirmedImpactCount,
                frame.CoverageObservation,
                frame.UsedCoverageCandidates,
                out DssPredictiveAimTarget resolvedTarget,
                out _))
        {
            return DssSequentialTargetTelemetry.Empty(
                sequentialStep);
        }

        double targetX =
            resolvedTarget.NormalizedX;
        double targetY =
            resolvedTarget.NormalizedY;
        int candidateId =
            resolvedTarget.CandidateId;
        double uncoveredScore =
            resolvedTarget.CoverageScore;
        string targetSource =
            resolvedTarget.Role;
        double targetRadius =
            Math.Sqrt(
                targetX * targetX
                + targetY * targetY);

        double errorPixels = -1d;

        if (launch.GeometryValid
            && launch.HorizonRadiusPixels > 0)
        {
            double dx =
                launch.AimNormalizedX - targetX;
            double dy =
                launch.AimNormalizedY - targetY;

            errorPixels =
                Math.Sqrt(
                    dx * dx + dy * dy)
                * launch.HorizonRadiusPixels;
        }

        DssCoverageObservation coverage =
            frame.CoverageObservation
            ?? DssCoverageObservation.Empty;

        return new DssSequentialTargetTelemetry(
            sequentialStep,
            true,
            targetX,
            targetY,
            targetRadius,
            errorPixels,
            candidateId,
            targetSource,
            coverage.CoveredFraction,
            uncoveredScore);
    }
}
internal static class DssProbeLaunchCorrelator
{
    public static DssProbeLaunchRecord Correlate(
        int launchSequence,
        DssFireInputEvent input,
        DssProbeLaunchFrameSnapshot? frame)
    {
        if (frame is null)
        {
            return Empty(
                launchSequence,
                input);
        }

        DssHudGeometry geometry =
            frame.Geometry;

        double ageMs =
            Math.Max(
                0,
                (input.TimestampUtc
                 - frame.FrameUtc)
                .TotalMilliseconds);

        bool valid =
            geometry.BodyCenterFound
            && geometry.HorizonMarkerFound
            && geometry.HorizonRadiusPixels > 25;

        if (!valid)
        {
            return new DssProbeLaunchRecord(
                launchSequence,
                input.TimestampUtc,
                frame.FrameUtc,
                ageMs,
                frame.FrameSequence,
                input.Binding.Action,
                input.Binding.Slot,
                input.Binding.Input.Device,
                input.Binding.Input.Key,
                false,
                frame.Readiness.State.ToString(),
                frame.Readiness.AngularDiameterDegrees,
                geometry.BodyCenterX,
                geometry.BodyCenterY,
                geometry.HorizonRadiusPixels,
                geometry.ReticleX,
                geometry.ReticleY,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                string.Empty,
                frame.MissObservation?.Visible == true,
                frame.MissObservation?.ActiveRatio ?? 0);
        }

        double normalizedX =
            (geometry.ReticleX
             - geometry.BodyCenterX)
            / geometry.HorizonRadiusPixels;

        double normalizedY =
            (geometry.ReticleY
             - geometry.BodyCenterY)
            / geometry.HorizonRadiusPixels;

        double normalizedRadius =
            Math.Sqrt(
                normalizedX * normalizedX
                + normalizedY * normalizedY);

        double angleDegrees =
            Math.Atan2(
                normalizedY,
                normalizedX)
            * 180d
            / Math.PI;

        DssProjectedAimPlan plan =
            DssProbeAimSolver.Solve(
                frame.State,
                frame.Readiness,
                geometry);

        DssProjectedAimPoint? nearest =
            plan.IsAvailable
                ? plan.Points
                    .OrderBy(
                        point =>
                            DistanceSquared(
                                normalizedX,
                                normalizedY,
                                point.NormalizedX,
                                point.NormalizedY))
                    .FirstOrDefault()
                : null;

        double nearestErrorNormalized =
            nearest is null
                ? 0
                : Math.Sqrt(
                    DistanceSquared(
                        normalizedX,
                        normalizedY,
                        nearest.NormalizedX,
                        nearest.NormalizedY));

        return new DssProbeLaunchRecord(
            launchSequence,
            input.TimestampUtc,
            frame.FrameUtc,
            ageMs,
            frame.FrameSequence,
            input.Binding.Action,
            input.Binding.Slot,
            input.Binding.Input.Device,
            input.Binding.Input.Key,
            true,
            frame.Readiness.State.ToString(),
            frame.Readiness.AngularDiameterDegrees,
            geometry.BodyCenterX,
            geometry.BodyCenterY,
            geometry.HorizonRadiusPixels,
            geometry.ReticleX,
            geometry.ReticleY,
            normalizedX,
            normalizedY,
            normalizedRadius,
            angleDegrees,
            nearest?.Sequence ?? 0,
            nearest?.NormalizedX ?? 0,
            nearest?.NormalizedY ?? 0,
            nearestErrorNormalized,
            nearestErrorNormalized
                * geometry.HorizonRadiusPixels,
            plan.EfficiencyTarget,
            plan.Source,
            frame.MissObservation?.Visible == true,
            frame.MissObservation?.ActiveRatio ?? 0);
    }

    private static DssProbeLaunchRecord Empty(
        int launchSequence,
        DssFireInputEvent input) =>
        new(
            launchSequence,
            input.TimestampUtc,
            DateTimeOffset.MinValue,
            -1,
            0,
            input.Binding.Action,
            input.Binding.Slot,
            input.Binding.Input.Device,
            input.Binding.Input.Key,
            false,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            string.Empty);

    private static double DistanceSquared(
        double x1,
        double y1,
        double x2,
        double y2)
    {
        double dx =
            x1 - x2;

        double dy =
            y1 - y2;

        return dx * dx + dy * dy;
    }
}

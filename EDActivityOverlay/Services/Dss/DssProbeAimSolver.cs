using System;
using System.Collections.Generic;
using System.Linq;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;

namespace EDActivityOverlay.Services.Dss;

internal sealed record DssProjectedAimPoint(
    int Sequence,
    double NormalizedX,
    double NormalizedY,
    double ScreenX,
    double ScreenY,
    DssAimZone Zone);

internal sealed record DssProjectedAimPlan(
    int EfficiencyTarget,
    string Source,
    IReadOnlyList<DssProjectedAimPoint> Points)
{
    public static DssProjectedAimPlan Empty { get; } =
        new(
            0,
            string.Empty,
            Array.Empty<DssProjectedAimPoint>());

    public bool IsAvailable =>
        Points.Count > 0;
}

/// <summary>
/// First live DSS aim-point projection layer.
///
/// This is intentionally not a probe-ballistics model yet. Elite's probes
/// curve, so the final aim -> impact mapping must be calibrated from real
/// shots.
///
/// For now the solver projects the project's existing normalized DSS pattern
/// into live screen coordinates:
///
///     Pscreen = C + Pnormalized * Rh
///
/// This gives us stable, reproducible test aim positions and isolates the
/// future empirical trajectory correction behind one service.
/// </summary>
internal static class DssProbeAimSolver
{
    public static DssProjectedAimPlan Solve(
        GameStateSnapshot state,
        DssAssistantReadinessSnapshot readiness,
        DssHudGeometry geometry)
    {
        if (!readiness.IsReady
            || !geometry.BodyCenterFound
            || !geometry.HorizonMarkerFound
            || geometry.HorizonRadiusPixels <= 25)
        {
            return DssProjectedAimPlan.Empty;
        }

        (int target, string source) =
            ResolveEfficiencyTarget(
                state);

        DssProbePattern pattern =
            DssProbePatternCatalog.Get(
                target);

        DssProjectedAimPoint[] points =
            pattern.Points
                .OrderBy(
                    point => point.Sequence)
                .Select(
                    point =>
                        new DssProjectedAimPoint(
                            point.Sequence,
                            point.X,
                            point.Y,
                            geometry.BodyCenterX
                                + point.X
                                  * geometry.HorizonRadiusPixels,
                            geometry.BodyCenterY
                                + point.Y
                                  * geometry.HorizonRadiusPixels,
                            point.Zone))
                .ToArray();

        return new DssProjectedAimPlan(
            pattern.EfficiencyTarget,
            source,
            points);
    }

    private static (int Target, string Source)
        ResolveEfficiencyTarget(
            GameStateSnapshot state)
    {
        if (state.DestinationBodyId >= 0)
        {
            ExplorationBodySnapshot? body =
                state.ExplorationBodies
                    .FirstOrDefault(
                        item =>
                            item.BodyId
                            == state.DestinationBodyId);

            if (body?.EfficiencyTarget > 0)
            {
                return (
                    Math.Clamp(
                        body.EfficiencyTarget,
                        DssProbePatternCatalog.MinimumTarget,
                        DssProbePatternCatalog.MaximumTarget),
                    "BODY");
            }
        }

        int configured =
            SettingsService.Instance.Settings
                .DssEfficiencyTarget;

        return (
            Math.Clamp(
                configured,
                DssProbePatternCatalog.MinimumTarget,
                DssProbePatternCatalog.MaximumTarget),
            "SETTINGS");
    }
}

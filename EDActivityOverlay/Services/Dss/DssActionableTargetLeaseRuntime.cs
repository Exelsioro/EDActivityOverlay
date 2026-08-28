using System;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Shared authoritative lease for the currently actionable DSS target.
///
/// The target solver is fed by several live CV/native-HUD signals. A transient
/// one-frame/short dropout must not make a target non-actionable after it has
/// already been shown to the commander. Otherwise display and fire handling can
/// observe different solver states around the same physical trigger press.
///
/// v60 therefore leases the most recent valid plan for the current body+step.
/// While geometry remains usable, a temporary raw-plan dropout returns the same
/// target reprojected through the newest centre/horizon geometry.
///
/// The lease ends only when:
/// - targeting step changes;
/// - DSS scan completes;
/// - target body/system changes.
///
/// If geometry itself is unusable, the lease is retained but not exposed. This
/// prevents firing against stale screen geometry while still allowing the same
/// target to return immediately when C/Rh tracking recovers.
/// </summary>
internal static class DssActionableTargetLeaseRuntime
{
    private static readonly object Gate =
        new();

    private static long systemAddress;
    private static int bodyId = -1;
    private static string bodyName =
        string.Empty;

    private static int step;
    private static DssProjectedAimPlan leasedPlan =
        DssProjectedAimPlan.Empty;

    private static bool holdingRawDropout;

    internal static DssProjectedAimPlan Resolve(
        GameStateSnapshot state,
        int sequentialStep,
        bool scanComplete,
        DssHudGeometry geometry,
        DssProjectedAimPlan currentRawPlan)
    {
        lock (Gate)
        {
            if (scanComplete)
            {
                ResetLocked();
                return
                    DssProjectedAimPlan.Empty;
            }

            if (!IsSameContextLocked(
                    state))
            {
                ResetLocked();
                CaptureContextLocked(
                    state);
            }

            if (step != sequentialStep)
            {
                step =
                    sequentialStep;

                leasedPlan =
                    DssProjectedAimPlan.Empty;

                holdingRawDropout =
                    false;
            }

            if (currentRawPlan.IsAvailable)
            {
                bool recovered =
                    holdingRawDropout;

                leasedPlan =
                    currentRawPlan;

                holdingRawDropout =
                    false;

                if (recovered)
                {
                    Logger.Logger.Info(
                        $"DSS TARGET LEASE RECOVER: step={sequentialStep}; " +
                        $"source='{currentRawPlan.Source}'.");
                }

                return
                    currentRawPlan;
            }

            if (!leasedPlan.IsAvailable
                || !IsGeometryUsable(
                    geometry))
            {
                return
                    DssProjectedAimPlan.Empty;
            }

            if (!holdingRawDropout)
            {
                holdingRawDropout =
                    true;

                Logger.Logger.Info(
                    $"DSS TARGET LEASE HOLD: step={sequentialStep}; " +
                    $"source='{leasedPlan.Source}'.");
            }

            return
                Reproject(
                    leasedPlan,
                    geometry);
        }
    }

    private static DssProjectedAimPlan Reproject(
        DssProjectedAimPlan plan,
        DssHudGeometry geometry)
    {
        if (plan.Points.Count != 1)
        {
            return
                plan;
        }

        DssProjectedAimPoint point =
            plan.Points[0];

        var projected =
            new DssProjectedAimPoint(
                point.Sequence,
                point.NormalizedX,
                point.NormalizedY,
                geometry.BodyCenterX
                    + point.NormalizedX
                      * geometry.HorizonRadiusPixels,
                geometry.BodyCenterY
                    + point.NormalizedY
                      * geometry.HorizonRadiusPixels,
                point.Zone,
                point.CandidateId,
                point.CoverageScore);

        return
            new DssProjectedAimPlan(
                plan.EfficiencyTarget,
                plan.Source,
                new[] { projected });
    }

    private static bool IsGeometryUsable(
        DssHudGeometry geometry) =>
        geometry.BodyCenterFound
        && geometry.HorizonMarkerFound
        && geometry.HorizonRadiusPixels > 25d;

    private static bool IsSameContextLocked(
        GameStateSnapshot state)
    {
        if (systemAddress == 0
            || state.SystemAddress == 0
            || systemAddress
               != state.SystemAddress)
        {
            return false;
        }

        if (bodyId >= 0
            && state.DestinationBodyId >= 0)
        {
            return bodyId
                   == state.DestinationBodyId;
        }

        return !string.IsNullOrWhiteSpace(
                   bodyName)
               && !string.IsNullOrWhiteSpace(
                   state.DestinationName)
               && bodyName.Equals(
                   state.DestinationName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void CaptureContextLocked(
        GameStateSnapshot state)
    {
        systemAddress =
            state.SystemAddress;

        bodyId =
            state.DestinationBodyId;

        bodyName =
            state.DestinationName
            ?? string.Empty;
    }

    private static void ResetLocked()
    {
        systemAddress = 0;
        bodyId = -1;
        bodyName =
            string.Empty;
        step = 0;
        leasedPlan =
            DssProjectedAimPlan.Empty;
        holdingRawDropout =
            false;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            ResetLocked();
        }
    }
}

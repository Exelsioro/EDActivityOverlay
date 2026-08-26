using System;

namespace EDActivityOverlay.Services.Dss;

/// <summary>
/// Display-only latch for the single actionable NEXT AIM.
///
/// CENTER is special: the native white reticle can hide the CV centre for
/// several seconds while the user is doing exactly the correct thing. A centre
/// target therefore remains visible until the targeting step changes or the
/// scan completes. Other targets retain only a short dropout grace.
/// </summary>
internal sealed class DssAimVisualTargetLatch
{
    internal static readonly TimeSpan NonCenterHoldDuration =
        TimeSpan.FromSeconds(2);

    private int step;
    private DateTimeOffset lastValidUtc =
        DateTimeOffset.MinValue;
    private DssProjectedAimPlan plan =
        DssProjectedAimPlan.Empty;
    private bool holdUntilStepChange;

    public void Reset()
    {
        step = 0;
        lastValidUtc =
            DateTimeOffset.MinValue;
        plan =
            DssProjectedAimPlan.Empty;
        holdUntilStepChange = false;
    }

    public DssProjectedAimPlan Resolve(
        DateTimeOffset timestampUtc,
        int targetingStep,
        bool scanComplete,
        DssProjectedAimPlan current)
    {
        if (scanComplete)
        {
            Reset();
            return DssProjectedAimPlan.Empty;
        }

        if (current.IsAvailable)
        {
            step = targetingStep;
            lastValidUtc = timestampUtc;
            plan = current;
            holdUntilStepChange =
                IsCenterTarget(
                    current);

            return current;
        }

        if (targetingStep != step)
        {
            Reset();
            return DssProjectedAimPlan.Empty;
        }

        if (!plan.IsAvailable
            || lastValidUtc
               == DateTimeOffset.MinValue)
        {
            return DssProjectedAimPlan.Empty;
        }

        if (holdUntilStepChange)
        {
            return plan;
        }

        if (timestampUtc - lastValidUtc
            <= NonCenterHoldDuration)
        {
            return plan;
        }

        Reset();
        return DssProjectedAimPlan.Empty;
    }

    private static bool IsCenterTarget(
        DssProjectedAimPlan candidate)
    {
        if (candidate.Points.Count != 1)
        {
            return false;
        }

        DssProjectedAimPoint point =
            candidate.Points[0];

        double radius =
            Math.Sqrt(
                point.NormalizedX
                * point.NormalizedX
                + point.NormalizedY
                  * point.NormalizedY);

        return radius <= 0.08d;
    }
}

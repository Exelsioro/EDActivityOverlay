using System;
using System.Linq;
using EDActivityOverlay.Models;

namespace EDActivityOverlay.Services.Dss;

internal enum DssAssistantReadinessState
{
    SelectBodyTarget,
    NeedBodyRadius,
    Calibrating,
    TooClose,
    Ready,
    TooFar
}

internal sealed record DssAssistantReadinessSnapshot(
    DssAssistantReadinessState State,
    bool BodyTargetSelected,
    double BodyRadiusMeters,
    double AngularRadiusDegrees,
    double AngularDiameterDegrees,
    double MeasurementAgeMilliseconds,
    double EstimatedCenterDistanceMeters,
    double RecommendedNearCenterDistanceMeters,
    double RecommendedTargetCenterDistanceMeters,
    double RecommendedFarCenterDistanceMeters)
{
    public bool HasAngularMeasurement =>
        AngularRadiusDegrees > 0;

    public bool HasDistanceEstimate =>
        EstimatedCenterDistanceMeters > 0;

    public bool IsReady =>
        State == DssAssistantReadinessState.Ready;

    public bool IsFarReadyEdge =>
        IsReady
        && AngularDiameterDegrees > 0
        && AngularDiameterDegrees < 24d;
}

/// <summary>
/// Experimental DSS readiness gate.
///
/// The two initial live comparison runs showed substantially better CV at an
/// angular body diameter of ~28 degrees than at ~55 degrees. Until we collect
/// the "too far" side of the curve, v12 uses an intentionally explicit
/// experimental band:
///
///     22° <= angular diameter <= 32°
///
/// The latest live run around 23° was one of the strongest complete CV runs,
/// while ~21° was already near the game's maximum probe-launch distance and
/// showed weaker horizon reliability. The 22..24° region is therefore a
/// usable FAR EDGE, not a failure state.
///
/// Hysteresis prevents READY from flickering near the boundaries.
///
/// The important architectural point is that readiness is expressed in angular
/// size, not an absolute Mm distance. A physical recommended distance is then
/// derived from the selected body's radius:
///
///     D_center = R / sin(theta)
///
/// where theta is the angular radius to the geometric horizon.
/// </summary>
internal sealed class DssAssistantReadinessEvaluator
{
    internal const double MinimumReadyAngularDiameterDegrees = 22d;
    internal const double TargetAngularDiameterDegrees = 28d;
    internal const double MaximumReadyAngularDiameterDegrees = 32d;

    private const double ReadyStayMinimumDiameterDegrees = 21.5d;
    private const double ReadyStayMaximumDiameterDegrees = 33d;

    private static readonly TimeSpan MeasurementHold =
        TimeSpan.FromSeconds(2.5);

    private static readonly TimeSpan MeasurementResetGap =
        TimeSpan.FromSeconds(1.5);

    private const double AngularEmaAlpha = 0.22;

    // v26: at very long range Frontier can expose a clean body-centre marker
    // long before the horizon triplet is large enough to measure. The v25
    // logs show normal READY acquisitions obtaining H within ~0.7-1.9 s after
    // the first strong C, while far-only sessions remain C-only for >3 s.
    internal static readonly TimeSpan CenterOnlyTooFarInferenceDelay =
        TimeSpan.FromSeconds(3);

    private const double CenterOnlyTooFarMinimumConfidence = 0.75;

    // v28: C-only TooFar is trustworthy only while the body centre remains
    // reasonably close to the reticle. Recorded v27 families were separated:
    // genuine far-only C ~= 7.73..12.23 deg; false off-axis inference started
    // at ~=15.48 deg. 14 deg keeps a conservative gap between them.
    internal const double CenterOnlyTooFarMaximumAimOffsetDegrees = 14d;

    private double smoothedAngularRadiusDegrees;
    private DateTimeOffset lastAngularMeasurementUtc =
        DateTimeOffset.MinValue;
    private DateTimeOffset centerOnlyWithoutHorizonSinceUtc =
        DateTimeOffset.MinValue;
    private bool readyLatched;

    private string lastRadiusLookupKey =
        string.Empty;
    private double cachedHistoricalRadiusMeters;
    private DateTimeOffset lastRadiusLookupUtc =
        DateTimeOffset.MinValue;

    private static readonly TimeSpan RadiusLookupRetry =
        TimeSpan.FromSeconds(5);

    public void Reset()
    {
        smoothedAngularRadiusDegrees = 0;
        lastAngularMeasurementUtc =
            DateTimeOffset.MinValue;
        centerOnlyWithoutHorizonSinceUtc =
            DateTimeOffset.MinValue;
        readyLatched = false;

        lastRadiusLookupKey =
            string.Empty;
        cachedHistoricalRadiusMeters = 0;
        lastRadiusLookupUtc =
            DateTimeOffset.MinValue;

        DssAssistantStateService.Instance.Clear();
    }

    public DssAssistantReadinessSnapshot Evaluate(
        GameStateSnapshot state,
        DssPrototypeSessionContext context,
        DssCapturedFrame frame,
        DssHudGeometry geometry)
    {
        bool bodyTargetSelected =
            IsBodyTargetSelected(state);

        double bodyRadiusMeters =
            ResolveBodyRadiusMeters(
                state,
                context,
                frame.TimestampUtc);

        if (!bodyTargetSelected)
        {
            readyLatched = false;
            centerOnlyWithoutHorizonSinceUtc =
                DateTimeOffset.MinValue;

            return Publish(
                BuildWithoutMeasurement(
                    DssAssistantReadinessState.SelectBodyTarget,
                    bodyTargetSelected,
                    bodyRadiusMeters),
                context,
                frame,
                geometry);
        }

        UpdateAngularMeasurement(
            frame,
            geometry,
            context.VerticalFovDegrees);

        UpdateCenterOnlyFarInference(
            frame,
            geometry);

        double measurementAgeMilliseconds =
            lastAngularMeasurementUtc
                == DateTimeOffset.MinValue
                ? -1
                : Math.Max(
                    0,
                    (frame.TimestampUtc
                     - lastAngularMeasurementUtc)
                    .TotalMilliseconds);

        // A direct Frontier horizon observation is required to establish /
        // update the angular-size estimate. After that, validity belongs to
        // the DSS geometry tracker: if it still exposes a trusted horizon and
        // a tracked centre, Frontier merely blinking the white horizon triplet
        // must not demote READY back to CALIBRATING.
        //
        // The old 2.5 s timeout duplicated tracker validity and caused exactly
        // that behaviour: the cyan circle remained reconstructed from trusted
        // Rh, while the projected aim points disappeared because readiness
        // expired.
        bool trackedTrustedGeometry =
            geometry.BodyCenterFound
            && geometry.HorizonMarkerFound
            && geometry.HorizonRadiusPixels > 25;

        bool measurementFresh =
            smoothedAngularRadiusDegrees > 0
            && measurementAgeMilliseconds >= 0
            && (
                measurementAgeMilliseconds
                    <= MeasurementHold.TotalMilliseconds
                || trackedTrustedGeometry);

        (double nearMeters,
         double targetMeters,
         double farMeters) =
            CalculateRecommendedCenterDistancesMeters(
                bodyRadiusMeters);

        if (!measurementFresh)
        {
            readyLatched = false;

            // Physical radius is deliberately NOT required here. At long range
            // we are inferring only a directional action (move closer) from a
            // strong C with no measurable H. v28 additionally requires the body
            // centre to remain close enough to the reticle that missing H is
            // meaningful evidence of distance rather than off-axis geometry.
            bool inferredTooFar =
                geometry.BodyCenterFound
                && !geometry.HorizonMarkerFound
                && geometry.AimOffsetDegrees
                   <= CenterOnlyTooFarMaximumAimOffsetDegrees
                && centerOnlyWithoutHorizonSinceUtc
                    != DateTimeOffset.MinValue
                && frame.TimestampUtc
                   - centerOnlyWithoutHorizonSinceUtc
                   >= CenterOnlyTooFarInferenceDelay;

            return Publish(
                new DssAssistantReadinessSnapshot(
                    inferredTooFar
                        ? DssAssistantReadinessState.TooFar
                        : DssAssistantReadinessState.Calibrating,
                    true,
                    bodyRadiusMeters,
                    0,
                    0,
                    measurementAgeMilliseconds,
                    0,
                    nearMeters,
                    targetMeters,
                    farMeters),
                context,
                frame,
                geometry);
        }

        // A real angular measurement supersedes the C-only inference.
        centerOnlyWithoutHorizonSinceUtc =
            DateTimeOffset.MinValue;

        double angularRadiusDegrees =
            smoothedAngularRadiusDegrees;

        double angularDiameterDegrees =
            angularRadiusDegrees * 2d;

        double estimatedCenterDistanceMeters =
            CalculateCenterDistanceMeters(
                bodyRadiusMeters,
                angularRadiusDegrees);

        bool insideReadyBand =
            readyLatched
                ? angularDiameterDegrees
                      >= ReadyStayMinimumDiameterDegrees
                  && angularDiameterDegrees
                      <= ReadyStayMaximumDiameterDegrees
                : angularDiameterDegrees
                      >= MinimumReadyAngularDiameterDegrees
                  && angularDiameterDegrees
                      <= MaximumReadyAngularDiameterDegrees;

        DssAssistantReadinessState readinessState;

        if (insideReadyBand)
        {
            readyLatched = true;
            readinessState =
                DssAssistantReadinessState.Ready;
        }
        else
        {
            readyLatched = false;

            readinessState =
                angularDiameterDegrees
                    > MaximumReadyAngularDiameterDegrees
                    ? DssAssistantReadinessState.TooClose
                    : DssAssistantReadinessState.TooFar;
        }

        return Publish(
            new DssAssistantReadinessSnapshot(
                readinessState,
                true,
                bodyRadiusMeters,
                angularRadiusDegrees,
                angularDiameterDegrees,
                measurementAgeMilliseconds,
                estimatedCenterDistanceMeters,
                nearMeters,
                targetMeters,
                farMeters),
            context,
            frame,
            geometry);
    }

    private static DssAssistantReadinessSnapshot Publish(
        DssAssistantReadinessSnapshot snapshot,
        DssPrototypeSessionContext context,
        DssCapturedFrame frame,
        DssHudGeometry geometry)
    {
        DssAssistantStateService.Instance.Publish(
            context,
            snapshot,
            geometry,
            frame.TimestampUtc);

        return snapshot;
    }

    private void UpdateCenterOnlyFarInference(
        DssCapturedFrame frame,
        DssHudGeometry geometry)
    {
        // Once H exists, either a real measurement is available now or the
        // tracker is carrying a radius established by an earlier observation.
        // In either case the normal angular readiness path owns the state.
        if (geometry.HorizonMarkerFound)
        {
            centerOnlyWithoutHorizonSinceUtc =
                DateTimeOffset.MinValue;
            return;
        }

        if (!geometry.BodyCenterFound)
        {
            return;
        }

        // At a large reticle/body offset, missing H is ambiguous: the v27
        // follow-up showed this can persist after crossing the READY band.
        // Clear the clock so recentering has to establish a fresh 3 s interval.
        if (geometry.AimOffsetDegrees
            > CenterOnlyTooFarMaximumAimOffsetDegrees)
        {
            centerOnlyWithoutHorizonSinceUtc =
                DateTimeOffset.MinValue;
            return;
        }

        // Start only from a strong observed/acquired centre. Once started,
        // brief Predicting confidence decay does not restart the 3 s clock.
        if (centerOnlyWithoutHorizonSinceUtc
                == DateTimeOffset.MinValue
            && geometry.BodyCenterConfidence
                >= CenterOnlyTooFarMinimumConfidence)
        {
            centerOnlyWithoutHorizonSinceUtc =
                frame.TimestampUtc;
        }
    }

    private void UpdateAngularMeasurement(
        DssCapturedFrame frame,
        DssHudGeometry geometry,
        double verticalFovDegrees)
    {
        // Update the readiness estimate only from a directly observed
        // Frontier horizon marker. The DSS tracker may preserve/reproject Rh
        // while Frontier blinks the dash, but a stale pixel radius should not
        // be interpreted as a new physical distance while the ship is moving.
        if (!geometry.BodyCenterFound
            || !geometry.HorizonMarkerFound
            || !geometry.HorizonMarkerObserved
            || geometry.BodyCenterConfidence < 0.55
            || geometry.HorizonMarkerConfidence < 0.50)
        {
            return;
        }

        double measuredAngularRadiusDegrees =
            CalculateAngularSeparationDegrees(
                frame.Width,
                frame.Height,
                verticalFovDegrees,
                geometry.BodyCenterX,
                geometry.BodyCenterY,
                geometry.HorizonMarkerX,
                geometry.HorizonMarkerY);

        // Reject impossible / obviously bad CV results before they can poison
        // the readiness EMA.
        if (measuredAngularRadiusDegrees < 2
            || measuredAngularRadiusDegrees > 45)
        {
            return;
        }

        bool resetSmoothing =
            lastAngularMeasurementUtc
                == DateTimeOffset.MinValue
            || frame.TimestampUtc
               - lastAngularMeasurementUtc
               > MeasurementResetGap
            || smoothedAngularRadiusDegrees <= 0;

        if (resetSmoothing)
        {
            smoothedAngularRadiusDegrees =
                measuredAngularRadiusDegrees;
        }
        else
        {
            smoothedAngularRadiusDegrees =
                smoothedAngularRadiusDegrees
                    * (1d - AngularEmaAlpha)
                + measuredAngularRadiusDegrees
                    * AngularEmaAlpha;
        }

        lastAngularMeasurementUtc =
            frame.TimestampUtc;
    }

    private static bool IsBodyTargetSelected(
        GameStateSnapshot state)
    {
        if (state.DestinationBodyId < 0
            || string.IsNullOrWhiteSpace(
                state.DestinationName))
        {
            return false;
        }

        if (state.DestinationSystemAddress != 0
            && state.SystemAddress != 0
            && state.DestinationSystemAddress
               != state.SystemAddress)
        {
            return false;
        }

        return true;
    }

    private double ResolveBodyRadiusMeters(
        GameStateSnapshot state,
        DssPrototypeSessionContext context,
        DateTimeOffset nowUtc)
    {
        if (state.DestinationBodyId >= 0)
        {
            ExplorationBodySnapshot? currentBody =
                state.ExplorationBodies
                    .FirstOrDefault(
                        body =>
                            body.BodyId
                            == state.DestinationBodyId);

            if (currentBody?.RadiusMeters > 0)
            {
                return currentBody.RadiusMeters;
            }
        }

        bool contextMatchesTarget =
            context.BodyId >= 0
            && context.BodyId
               == state.DestinationBodyId;

        if (contextMatchesTarget
            && context.BodyRadiusMeters > 0)
        {
            return context.BodyRadiusMeters;
        }

        if (state.DestinationBodyId < 0
            || string.IsNullOrWhiteSpace(
                state.JournalDirectory))
        {
            return 0;
        }

        string lookupKey =
            state.JournalDirectory
            + "|"
            + state.SystemAddress
            + "|"
            + state.DestinationBodyId
            + "|"
            + state.DestinationName;

        bool targetChanged =
            !lookupKey.Equals(
                lastRadiusLookupKey,
                StringComparison.OrdinalIgnoreCase);

        bool retryDue =
            lastRadiusLookupUtc
                == DateTimeOffset.MinValue
            || nowUtc - lastRadiusLookupUtc
               >= RadiusLookupRetry;

        if (targetChanged)
        {
            lastRadiusLookupKey =
                lookupKey;
            cachedHistoricalRadiusMeters = 0;
            lastRadiusLookupUtc =
                DateTimeOffset.MinValue;
            retryDue = true;
        }

        if (cachedHistoricalRadiusMeters > 0)
        {
            return cachedHistoricalRadiusMeters;
        }

        if (!retryDue)
        {
            return 0;
        }

        lastRadiusLookupUtc =
            nowUtc;

        DssBodyScanSnapshot historical =
            DssJournalContextReader.ResolveBodyScan(
                state.JournalDirectory,
                state.SystemAddress,
                state.DestinationBodyId,
                state.DestinationName);

        if (historical.RadiusMeters > 0)
        {
            cachedHistoricalRadiusMeters =
                historical.RadiusMeters;
        }

        return cachedHistoricalRadiusMeters;
    }

    private static DssAssistantReadinessSnapshot
        BuildWithoutMeasurement(
            DssAssistantReadinessState state,
            bool bodyTargetSelected,
            double bodyRadiusMeters)
    {
        if (bodyRadiusMeters > 0)
        {
            (double nearMeters,
             double targetMeters,
             double farMeters) =
                CalculateRecommendedCenterDistancesMeters(
                    bodyRadiusMeters);

            return new DssAssistantReadinessSnapshot(
                state,
                bodyTargetSelected,
                bodyRadiusMeters,
                0,
                0,
                -1,
                0,
                nearMeters,
                targetMeters,
                farMeters);
        }

        return new DssAssistantReadinessSnapshot(
            state,
            bodyTargetSelected,
            bodyRadiusMeters,
            0,
            0,
            -1,
            0,
            0,
            0,
            0);
    }

    internal static double
        CalculateAngularSeparationDegrees(
            int frameWidth,
            int frameHeight,
            double verticalFovDegrees,
            double firstX,
            double firstY,
            double secondX,
            double secondY)
    {
        double focalPixels =
            DssHudGeometryDetector.GetFocalPixels(
                frameHeight,
                verticalFovDegrees);

        double cx = frameWidth / 2d;
        double cy = frameHeight / 2d;

        (double X, double Y, double Z) first =
            (
                firstX - cx,
                firstY - cy,
                focalPixels);

        (double X, double Y, double Z) second =
            (
                secondX - cx,
                secondY - cy,
                focalPixels);

        double dot =
            first.X * second.X
            + first.Y * second.Y
            + first.Z * second.Z;

        double firstLength =
            Math.Sqrt(
                first.X * first.X
                + first.Y * first.Y
                + first.Z * first.Z);

        double secondLength =
            Math.Sqrt(
                second.X * second.X
                + second.Y * second.Y
                + second.Z * second.Z);

        if (firstLength <= 0
            || secondLength <= 0)
        {
            return 0;
        }

        double cosine =
            Math.Clamp(
                dot
                / (firstLength
                   * secondLength),
                -1d,
                1d);

        return Math.Acos(cosine)
               * 180d
               / Math.PI;
    }

    internal static double
        CalculateCenterDistanceMeters(
            double bodyRadiusMeters,
            double angularRadiusDegrees)
    {
        if (bodyRadiusMeters <= 0
            || angularRadiusDegrees <= 0
            || angularRadiusDegrees >= 89.9)
        {
            return 0;
        }

        double sine =
            Math.Sin(
                angularRadiusDegrees
                * Math.PI / 180d);

        return sine > 0
            ? bodyRadiusMeters / sine
            : 0;
    }

    internal static (
        double NearMeters,
        double TargetMeters,
        double FarMeters)
        CalculateRecommendedCenterDistancesMeters(
            double bodyRadiusMeters)
    {
        if (bodyRadiusMeters <= 0)
        {
            return (0, 0, 0);
        }

        // Near boundary corresponds to the largest accepted apparent body.
        double nearMeters =
            CalculateCenterDistanceMeters(
                bodyRadiusMeters,
                MaximumReadyAngularDiameterDegrees / 2d);

        double targetMeters =
            CalculateCenterDistanceMeters(
                bodyRadiusMeters,
                TargetAngularDiameterDegrees / 2d);

        // Far boundary corresponds to the smallest accepted apparent body.
        double farMeters =
            CalculateCenterDistanceMeters(
                bodyRadiusMeters,
                MinimumReadyAngularDiameterDegrees / 2d);

        return (
            nearMeters,
            targetMeters,
            farMeters);
    }
}

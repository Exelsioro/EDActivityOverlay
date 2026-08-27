using System;

namespace EDActivityOverlay.Services.Dss;

internal enum DssCenterTrackState
{
    Acquiring,
    Tracking,
    // Center is at or very near the screen reticle; guide-ray detection cannot
    // work in this zone, so we synthesize center = reticle until the player
    // pans the camera away.
    NearCenter,
    Predicting,
    Lost
}

internal enum DssHorizonTrackState
{
    Acquiring,
    Tracking,
    Predicting,
    Lost
}

internal sealed record DssHudTrackResult(
    DssHudGeometry Geometry,
    DssCenterTrackState CenterState,
    DssHorizonTrackState HorizonState,
    string SearchMode,
    double CenterVelocityX,
    double CenterVelocityY,
    bool GlobalSearchUsed,
    bool LocalSearchUsed,
    double ImageTrackConfidence);

/// <summary>
/// DSS tracker v9.
///
/// v8's radial centre detector is retained, but state transitions are now
/// conservative:
///
/// - continuous local observations update immediately;
/// - a stale/global candidate cannot teleport the trusted centre;
/// - reacquisition is a hypothesis that needs several mutually consistent
///   frames before it replaces the old track;
/// - brief misses freeze the last centre; longer misses hide geometry;
/// - trusted horizon radius survives visual-signature/visibility gaps and is
///   reset only with the actual DSS session.
/// </summary>
internal sealed partial class DssHudGeometryTracker
{
    // Real GLOBAL / REACQUIRE detector cycles can occasionally take
    // 200-300+ ms. The old 180 ms hold could therefore expire because of our
    // own CV latency while the player was moving the camera.
    private static readonly TimeSpan CenterHold =
        TimeSpan.FromMilliseconds(650);

    private static readonly TimeSpan CenterPredictionWindow =
        TimeSpan.FromMilliseconds(380);

    private const double MaximumPredictedCenterDisplacementPixels = 120d;

    // When the tracked planet center is this close to the screen reticle,
    // the Frontier guide-ray cannot be measured (too short). We synthesize
    // the center at the reticle position and suppress guide-ray detection
    // until the player pans the camera away.
    private const double NearReticleLockRadiusPixels = 80d;

    private static readonly TimeSpan FreshObservationWindow =
        TimeSpan.FromMilliseconds(320);

    private static readonly TimeSpan CenterPendingWindow =
        TimeSpan.FromMilliseconds(520);

    private static readonly TimeSpan HorizonPendingWindow =
        TimeSpan.FromMilliseconds(700);

    private const int LocalFailureBudget = 2;

    private const int InitialCenterConfirmations = 2;
    private const int ReacquireCenterConfirmations = 3;
    private const int HorizonConfirmationsRequired = 3;
    private const int StrictHorizonReacquireConfirmations = 4;

    private static readonly TimeSpan StrictHorizonReacquireAge =
        TimeSpan.FromMilliseconds(1200);

    private const double ImmediateCenterStepPixels = 125d;
    private const double PendingCenterTolerancePixels = 110d;

    private bool hasTrustedCenter;
    private double centerX;
    private double centerY;
    private double velocityX;
    private double velocityY;

    private DateTimeOffset lastCenterObservedUtc =
        DateTimeOffset.MinValue;

    private int localMisses;

    // Historical-state flag survives active-track loss. It is not rendered once
    // CenterHold expires; it only selects REACQUIRE mode and the stricter
    // temporal confirmation count. Screen-space distance is not a hard gate.
    private bool hasHistoricalCenter;

    private double pendingCenterX;
    private double pendingCenterY;
    private int pendingCenterCount;
    private DateTimeOffset pendingCenterUtc =
        DateTimeOffset.MinValue;

    private bool hasTrustedHorizon;
    private double horizonRadius;
    private double horizonConfidence;
    private DateTimeOffset lastHorizonObservedUtc =
        DateTimeOffset.MinValue;

    private double pendingHorizonRadius;
    private int pendingHorizonCount;
    private DateTimeOffset pendingHorizonUtc =
        DateTimeOffset.MinValue;

    public void Reset()
    {
        hasTrustedCenter = false;
        centerX = 0;
        centerY = 0;
        velocityX = 0;
        velocityY = 0;
        lastCenterObservedUtc =
            DateTimeOffset.MinValue;

        localMisses = 0;

        ResetImageMotionTracking();

        hasHistoricalCenter = false;

        ResetPendingCenter();

        hasTrustedHorizon = false;
        horizonRadius = 0;
        horizonConfidence = 0;
        lastHorizonObservedUtc =
            DateTimeOffset.MinValue;

        ResetPendingHorizon();
    }

    public DssHudTrackResult Process(
        DssCapturedFrame frame,
        DssHudGeometryDetector detector,
        double verticalFovDegrees)
    {
        DateTimeOffset timestampUtc =
            frame.TimestampUtc;

        if (TryProcessImageMotionFrame(
                frame,
                verticalFovDegrees,
                timestampUtc,
                out DssHudTrackResult imageTracking))
        {
            return imageTracking;
        }

        bool strictHorizonReacquisition =
            RequiresStrictHorizonReacquisition(
                timestampUtc);

        DssHudGeometry raw;
        bool globalSearch = false;
        bool localSearch = false;
        string searchMode;

        if (CanUseLocalTracking(timestampUtc))
        {
            raw =
                detector.DetectLocal(
                    frame,
                    verticalFovDegrees,
                    new DssDetectionHint(
                        centerX,
                        centerY,
                        92,
                        hasTrustedHorizon
                            ? horizonRadius
                            : null,
                        strictHorizonReacquisition));

            localSearch = true;
            searchMode = "LOCAL";

            if (!raw.BodyCenterFound)
            {
                localMisses++;

                if (localMisses >= LocalFailureBudget)
                {
                    raw =
                        detector.DetectGlobal(
                            frame,
                            verticalFovDegrees,
                            hasTrustedHorizon
                                ? horizonRadius
                                : null,
                            strictHorizonReacquisition);

                    globalSearch = true;
                    searchMode = "LOCAL+GLOBAL";
                }
            }
        }
        else
        {
            raw =
                detector.DetectGlobal(
                    frame,
                    verticalFovDegrees,
                    hasTrustedHorizon
                        ? horizonRadius
                        : null,
                    strictHorizonReacquisition);

            globalSearch = true;

            searchMode =
                hasHistoricalCenter
                    ? "REACQUIRE"
                    : "GLOBAL";
        }

        DssCenterTrackState centerState =
            UpdateCenter(
                raw,
                timestampUtc);

        DssHudGeometry geometry =
            BuildStableCenterGeometry(
                raw,
                frame,
                verticalFovDegrees,
                timestampUtc,
                centerState);

        DssHorizonTrackState horizonState =
            UpdateHorizon(
                raw,
                geometry,
                timestampUtc,
                centerState);

        geometry =
            BuildHorizonGeometry(
                geometry,
                timestampUtc,
                horizonState);

        UpdateImageMotionAnchor(
            frame,
            raw,
            centerState,
            timestampUtc);

        return new DssHudTrackResult(
            geometry,
            centerState,
            horizonState,
            searchMode,
            velocityX,
            velocityY,
            globalSearch,
            localSearch,
            0);
    }

    private bool CanUseLocalTracking(
        DateTimeOffset timestampUtc)
    {
        if (!hasTrustedCenter)
        {
            return false;
        }

        double ageMs =
            (timestampUtc
             - lastCenterObservedUtc)
            .TotalMilliseconds;

        return ageMs >= 0
               && ageMs
                  <= FreshObservationWindow
                      .TotalMilliseconds;
    }

    private DssCenterTrackState UpdateCenter(
        DssHudGeometry raw,
        DateTimeOffset timestampUtc)
    {
        bool qualityObservation =
            raw.BodyCenterFound
            && raw.BodyCenterConfidence >= 0.66;

        // Near-reticle check: when the tracked center is inside the
        // guide-ray dead-zone (~80 px) around the screen reticle, detection
        // always fails because there is not enough guide path to score.
        // Recognise this as an intentional centre-aim, not as a lost track.
        if (!qualityObservation && hasTrustedCenter)
        {
            double nearDist =
                Distance(
                    centerX,
                    centerY,
                    raw.ReticleX,
                    raw.ReticleY);

            double scaleEstimate =
                Math.Max(1d, raw.ReticleY / 540d);

            if (nearDist
                <= NearReticleLockRadiusPixels * scaleEstimate)
            {
                // Synthesize: keep track alive with center at reticle.
                centerX = raw.ReticleX;
                centerY = raw.ReticleY;
                velocityX = 0;
                velocityY = 0;
                lastCenterObservedUtc = timestampUtc;
                return DssCenterTrackState.NearCenter;
            }
        }

        if (qualityObservation)
        {
            if (hasTrustedCenter)
            {
                double ageMs =
                    (timestampUtc
                     - lastCenterObservedUtc)
                    .TotalMilliseconds;

                double step =
                    Distance(
                        raw.BodyCenterX,
                        raw.BodyCenterY,
                        centerX,
                        centerY);

                bool fresh =
                    ageMs >= 0
                    && ageMs
                       <= FreshObservationWindow
                           .TotalMilliseconds;

                bool plausibleImmediateStep =
                    step <= ImmediateCenterStepPixels;

                if (fresh && plausibleImmediateStep)
                {
                    AcceptCenterObservation(
                        raw.BodyCenterX,
                        raw.BodyCenterY,
                        timestampUtc,
                        updateVelocity: true);

                    ResetPendingCenter();

                    return
                        DssCenterTrackState.Tracking;
                }

                // A stale/global/large-jump candidate is a reacquisition
                // hypothesis, not an immediate new centre.
                RegisterPendingCenter(
                    raw.BodyCenterX,
                    raw.BodyCenterY,
                    timestampUtc);

                if (pendingCenterCount
                    >= ReacquireCenterConfirmations)
                {
                    AcceptCenterObservation(
                        pendingCenterX,
                        pendingCenterY,
                        timestampUtc,
                        updateVelocity: false);

                    ResetPendingCenter();

                    return
                        DssCenterTrackState.Tracking;
                }

                return GetHeldOrLostState(
                    timestampUtc);
            }

            RegisterPendingCenter(
                raw.BodyCenterX,
                raw.BodyCenterY,
                timestampUtc);

            int required =
                hasHistoricalCenter
                    ? ReacquireCenterConfirmations
                    : InitialCenterConfirmations;

            if (pendingCenterCount >= required)
            {
                AcceptCenterObservation(
                    pendingCenterX,
                    pendingCenterY,
                    timestampUtc,
                    updateVelocity: false);

                ResetPendingCenter();

                return
                    DssCenterTrackState.Tracking;
            }

            return
                DssCenterTrackState.Acquiring;
        }

        if (hasTrustedCenter)
        {
            return GetHeldOrLostState(
                timestampUtc);
        }

        // Keep a partially built reacquire candidate only inside its own short
        // window. Do not clear it on a single missed global frame.
        if (pendingCenterCount > 0
            && timestampUtc - pendingCenterUtc
               > CenterPendingWindow)
        {
            ResetPendingCenter();
        }

        return hasHistoricalCenter
            ? DssCenterTrackState.Lost
            : DssCenterTrackState.Acquiring;
    }

    private DssCenterTrackState GetHeldOrLostState(
        DateTimeOffset timestampUtc)
    {
        double ageMs =
            (timestampUtc
             - lastCenterObservedUtc)
            .TotalMilliseconds;

        if (ageMs >= 0
            && ageMs
               <= CenterHold
                   .TotalMilliseconds)
        {
            return
                DssCenterTrackState.Predicting;
        }

        if (hasTrustedCenter)
        {
            hasTrustedCenter = false;
            velocityX = 0;
            velocityY = 0;
            localMisses = 0;
        }

        return
            DssCenterTrackState.Lost;
    }

    private void AcceptCenterObservation(
        double x,
        double y,
        DateTimeOffset timestampUtc,
        bool updateVelocity)
    {
        if (updateVelocity
            && hasTrustedCenter)
        {
            double dt =
                (timestampUtc
                 - lastCenterObservedUtc)
                .TotalSeconds;

            if (dt > 0.01 && dt < 0.32)
            {
                UpdateDirectVelocity(
                    x,
                    y,
                    dt);
            }
            else
            {
                velocityX = 0;
                velocityY = 0;
            }
        }
        else
        {
            velocityX = 0;
            velocityY = 0;
        }

        centerX = x;
        centerY = y;
        lastCenterObservedUtc =
            timestampUtc;

        hasTrustedCenter = true;
        localMisses = 0;

        hasHistoricalCenter = true;
    }

    private DssHudGeometry BuildStableCenterGeometry(
        DssHudGeometry raw,
        DssCapturedFrame frame,
        double verticalFovDegrees,
        DateTimeOffset timestampUtc,
        DssCenterTrackState state)
    {
        if (state
            == DssCenterTrackState.Tracking
            && hasTrustedCenter)
        {
            return GeometryForCenter(
                frame,
                verticalFovDegrees,
                centerX,
                centerY,
                raw.BodyCenterConfidence);
        }

        if (state
            == DssCenterTrackState.Predicting
            && hasTrustedCenter)
        {
            double ageMs =
                (timestampUtc
                 - lastCenterObservedUtc)
                .TotalMilliseconds;

            double confidence =
                0.58
                * Math.Clamp(
                    1d
                    - ageMs
                      / CenterHold
                          .TotalMilliseconds,
                    0.12d,
                    1d);

            // Short bounded prediction only to bridge detector stalls.
            // This is intentionally not the old long image/camera prediction:
            // no star-field tracking, max 380 ms of existing smoothed screen
            // velocity, and max 120 px total displacement.
            double predictionSeconds =
                Math.Clamp(
                    ageMs,
                    0d,
                    CenterPredictionWindow
                        .TotalMilliseconds)
                / 1000d;

            double predictedDx =
                velocityX * predictionSeconds;

            double predictedDy =
                velocityY * predictionSeconds;

            double predictedDistance =
                Math.Sqrt(
                    predictedDx * predictedDx
                    + predictedDy * predictedDy);

            if (predictedDistance
                > MaximumPredictedCenterDisplacementPixels
                && predictedDistance > 0)
            {
                double scale =
                    MaximumPredictedCenterDisplacementPixels
                    / predictedDistance;

                predictedDx *= scale;
                predictedDy *= scale;
            }

            return GeometryForCenter(
                frame,
                verticalFovDegrees,
                centerX + predictedDx,
                centerY + predictedDy,
                confidence);
        }

        return
            DssHudGeometry.Empty(
                frame.Width,
                frame.Height);
    }

    private static DssHudGeometry GeometryForCenter(
        DssCapturedFrame frame,
        double verticalFovDegrees,
        double x,
        double y,
        double confidence)
    {
        int reticleX =
            frame.Width / 2;

        int reticleY =
            frame.Height / 2;

        double dx =
            x - reticleX;

        double dy =
            y - reticleY;

        double focal =
            DssHudGeometryDetector
                .GetFocalPixels(
                    frame.Height,
                    verticalFovDegrees);

        double aimOffsetDegrees =
            Math.Atan2(
                Math.Sqrt(
                    dx * dx
                    + dy * dy),
                focal)
            * 180d
            / Math.PI;

        return
            DssHudGeometry.Empty(
                frame.Width,
                frame.Height) with
            {
                BodyCenterFound = true,
                BodyCenterX = x,
                BodyCenterY = y,
                BodyCenterConfidence =
                    confidence,
                AimOffsetDegrees =
                    aimOffsetDegrees
            };
    }

    private void RegisterPendingCenter(
        double x,
        double y,
        DateTimeOffset timestampUtc)
    {
        bool stale =
            pendingCenterCount == 0
            || timestampUtc - pendingCenterUtc
               > CenterPendingWindow;

        if (stale)
        {
            pendingCenterX = x;
            pendingCenterY = y;
            pendingCenterCount = 1;
            pendingCenterUtc =
                timestampUtc;

            return;
        }

        double distance =
            Distance(
                x,
                y,
                pendingCenterX,
                pendingCenterY);

        if (distance
            <= PendingCenterTolerancePixels)
        {
            pendingCenterX =
                (pendingCenterX
                 * pendingCenterCount
                 + x)
                / (pendingCenterCount + 1);

            pendingCenterY =
                (pendingCenterY
                 * pendingCenterCount
                 + y)
                / (pendingCenterCount + 1);

            pendingCenterCount++;
            pendingCenterUtc =
                timestampUtc;

            return;
        }

        pendingCenterX = x;
        pendingCenterY = y;
        pendingCenterCount = 1;
        pendingCenterUtc =
            timestampUtc;
    }

    private void ResetPendingCenter()
    {
        pendingCenterX = 0;
        pendingCenterY = 0;
        pendingCenterCount = 0;
        pendingCenterUtc =
            DateTimeOffset.MinValue;
    }

    private bool RequiresStrictHorizonReacquisition(
        DateTimeOffset timestampUtc)
    {
        if (!hasTrustedHorizon
            || lastHorizonObservedUtc
               == DateTimeOffset.MinValue)
        {
            return false;
        }

        return timestampUtc
               - lastHorizonObservedUtc
               >= StrictHorizonReacquireAge;
    }
    private DssHorizonTrackState UpdateHorizon(
        DssHudGeometry raw,
        DssHudGeometry stableGeometry,
        DateTimeOffset timestampUtc,
        DssCenterTrackState centerState)
    {
        bool canUseRawHorizon =
            centerState
                == DssCenterTrackState.Tracking
            && stableGeometry.BodyCenterFound
            && stableGeometry.BodyCenterConfidence >= 0.66
            && raw.HorizonMarkerObserved
            && IsSafeRawHorizon(
                raw);

        if (canUseRawHorizon)
        {
            if (hasTrustedHorizon)
            {
                double relativeDifference =
                    Math.Abs(
                        raw.HorizonRadiusPixels
                        - horizonRadius)
                    / Math.Max(
                        1d,
                        horizonRadius);

                if (relativeDifference
                    <= 0.055)
                {
                    if (RequiresStrictHorizonReacquisition(
                            timestampUtc))
                    {
                        RegisterPendingHorizon(
                            raw.HorizonRadiusPixels,
                            timestampUtc);

                        if (pendingHorizonCount
                            < StrictHorizonReacquireConfirmations)
                        {
                            return
                                DssHorizonTrackState.Predicting;
                        }

                        horizonRadius =
                            pendingHorizonRadius;

                        horizonConfidence =
                            Math.Max(
                                0.66,
                                raw.HorizonMarkerConfidence);

                        lastHorizonObservedUtc =
                            timestampUtc;

                        ResetPendingHorizon();

                        return
                            DssHorizonTrackState.Tracking;
                    }

                    const double alpha =
                        0.18;

                    horizonRadius =
                        horizonRadius
                        * (1d - alpha)
                        + raw.HorizonRadiusPixels
                          * alpha;

                    horizonConfidence =
                        Math.Max(
                            raw.HorizonMarkerConfidence,
                            horizonConfidence
                            * 0.92);

                    lastHorizonObservedUtc =
                        timestampUtc;

                    ResetPendingHorizon();

                    return
                        DssHorizonTrackState.Tracking;
                }

                // A trusted Rh is sticky. One incompatible static HUD/limb
                // candidate cannot replace it.
                return
                    DssHorizonTrackState.Predicting;
            }

            RegisterPendingHorizon(
                raw.HorizonRadiusPixels,
                timestampUtc);

            if (pendingHorizonCount
                >= HorizonConfirmationsRequired)
            {
                horizonRadius =
                    pendingHorizonRadius;

                horizonConfidence =
                    Math.Max(
                        0.64,
                        raw.HorizonMarkerConfidence);

                lastHorizonObservedUtc =
                    timestampUtc;

                hasTrustedHorizon = true;

                ResetPendingHorizon();

                return
                    DssHorizonTrackState.Tracking;
            }

            return
                DssHorizonTrackState.Acquiring;
        }

        if (RequiresStrictHorizonReacquisition(
                timestampUtc)
            && pendingHorizonCount > 0)
        {
            // Long-gap reacquisition requires consecutive isolated H samples.
            // A missed frame breaks the hypothesis instead of letting sparse
            // planet-limb detections accumulate across the 700 ms window.
            ResetPendingHorizon();
        }

        if (hasTrustedHorizon
            && stableGeometry.BodyCenterFound)
        {
            return
                DssHorizonTrackState.Predicting;
        }

        if (hasTrustedHorizon)
        {
            // Keep Rh in memory even while the centre/overlay is temporarily
            // hidden. It becomes renderable again after a valid centre
            // reacquisition.
            return
                DssHorizonTrackState.Predicting;
        }

        return centerState
               == DssCenterTrackState.Lost
            ? DssHorizonTrackState.Lost
            : DssHorizonTrackState.Acquiring;
    }

    private static bool IsSafeRawHorizon(
        DssHudGeometry raw)
    {
        if (raw.HorizonRadiusPixels <= 0)
        {
            return false;
        }

        double frameWidth =
            raw.ReticleX * 2d;

        double frameHeight =
            raw.ReticleY * 2d;

        if (frameWidth <= 0
            || frameHeight <= 0)
        {
            return false;
        }

        double xRatio =
            raw.HorizonMarkerX
            / frameWidth;

        double yRatio =
            raw.HorizonMarkerY
            / frameHeight;

        double radiusRatio =
            raw.HorizonRadiusPixels
            / frameHeight;

        return xRatio >= 0.12
               && xRatio <= 0.88
               && yRatio >= 0.10
               && yRatio <= 0.78
               && radiusRatio >= 0.18
               && radiusRatio <= 0.62;
    }

    private DssHudGeometry BuildHorizonGeometry(
        DssHudGeometry geometry,
        DateTimeOffset timestampUtc,
        DssHorizonTrackState state)
    {
        if (!geometry.BodyCenterFound
            || !hasTrustedHorizon)
        {
            return geometry with
            {
                HorizonMarkerFound = false
            };
        }

        double vx =
            geometry.ReticleX
            - geometry.BodyCenterX;

        double vy =
            geometry.ReticleY
            - geometry.BodyCenterY;

        double aimRadius =
            Math.Sqrt(
                vx * vx
                + vy * vy);

        if (aimRadius < 1)
        {
            return geometry with
            {
                HorizonMarkerFound = false
            };
        }

        double dx =
            vx / aimRadius;

        double dy =
            vy / aimRadius;

        double markerX =
            geometry.BodyCenterX
            + dx * horizonRadius;

        double markerY =
            geometry.BodyCenterY
            + dy * horizonRadius;

        bool directlyObserved =
            state
                == DssHorizonTrackState.Tracking;

        double ageMs =
            lastHorizonObservedUtc
                == DateTimeOffset.MinValue
                ? -1
                : Math.Max(
                    0,
                    (timestampUtc
                     - lastHorizonObservedUtc)
                    .TotalMilliseconds);

        return geometry with
        {
            HorizonMarkerFound = true,
            HorizonMarkerObserved =
                directlyObserved,
            HorizonMarkerX = markerX,
            HorizonMarkerY = markerY,
            HorizonMarkerConfidence =
                directlyObserved
                    ? horizonConfidence
                    : Math.Max(
                        0.30,
                        horizonConfidence
                        * 0.84),
            HorizonObservationAgeMilliseconds =
                directlyObserved
                    ? 0
                    : ageMs,
            HorizonRadiusPixels =
                horizonRadius,
            HorizonAimErrorPixels =
                aimRadius
                - horizonRadius
        };
    }

    private void RegisterPendingHorizon(
        double candidateRadius,
        DateTimeOffset timestampUtc)
    {
        bool stale =
            pendingHorizonCount == 0
            || timestampUtc
               - pendingHorizonUtc
               > HorizonPendingWindow;

        if (stale)
        {
            pendingHorizonRadius =
                candidateRadius;

            pendingHorizonCount = 1;

            pendingHorizonUtc =
                timestampUtc;

            return;
        }

        double relativeDifference =
            Math.Abs(
                candidateRadius
                - pendingHorizonRadius)
            / Math.Max(
                1d,
                pendingHorizonRadius);

        if (relativeDifference
            <= 0.032)
        {
            pendingHorizonRadius =
                (pendingHorizonRadius
                 * pendingHorizonCount
                 + candidateRadius)
                / (pendingHorizonCount + 1);

            pendingHorizonCount++;

            pendingHorizonUtc =
                timestampUtc;

            return;
        }

        pendingHorizonRadius =
            candidateRadius;

        pendingHorizonCount = 1;

        pendingHorizonUtc =
            timestampUtc;
    }

    private void ResetPendingHorizon()
    {
        pendingHorizonRadius = 0;
        pendingHorizonCount = 0;
        pendingHorizonUtc =
            DateTimeOffset.MinValue;
    }


    private static double Distance(
        double x1,
        double y1,
        double x2,
        double y2)
    {
        double dx =
            x2 - x1;

        double dy =
            y2 - y1;

        return Math.Sqrt(
            dx * dx
            + dy * dy);
    }
}

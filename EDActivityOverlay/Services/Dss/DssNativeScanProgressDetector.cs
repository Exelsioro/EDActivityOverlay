using System;
using System.Collections.Generic;
using System.Linq;

namespace EDActivityOverlay.Services.Dss;

internal readonly record struct DssNativeScanProgressObservation(
    bool CoverageAvailable,
    int CoveragePercent,
    double CoverageConfidence,
    bool HitCountAvailable,
    int HitCount,
    double HitCountConfidence);

internal readonly record struct DssNativeScanProgressSnapshot(
    int CoveragePercent,
    int HitCount,
    double CoverageConfidence,
    double HitCountConfidence,
    DateTimeOffset CoverageLastObservedUtc,
    DateTimeOffset HitCountLastObservedUtc,
    DateTimeOffset CoverageStableSinceUtc,
    DateTimeOffset HitCountChangedUtc);

/// <summary>
/// Stable native DSS progress state.
///
/// Two Frontier-native values are observed:
/// - the large bottom-left DSS coverage percentage;
/// - the lower-right "Ударов: N" hit counter.
///
/// The visual impact detector remains useful research telemetry, but it no
/// longer authorizes correction shots. That decision now follows Elite's own
/// counters.
/// </summary>
internal static class DssNativeScanProgressRuntime
{
    private static readonly object Gate =
        new();

    private static readonly TimeSpan MinimumAttemptInterval =
        TimeSpan.FromMilliseconds(45);

    private static readonly TimeSpan TelemetryFreshness =
        TimeSpan.FromSeconds(2.0);

    // Live v52 evidence:
    // - native hits=6 / coverage=88% was visible about 1.1 s before
    //   SAAScanComplete;
    // - another run still showed native hits=5 / coverage=83% while the
    //   experimental impact detector had already claimed 6 impacts.
    //
    // A 2.25 s stable window avoids exposing a correction during Frontier's
    // delayed coverage integration while adding only a small delay when a real
    // correction is actually required.
    private static readonly TimeSpan MinimumSettledDuration =
        TimeSpan.FromMilliseconds(2250);

    private const int RequiredStableFrames = 3;
    private const double MinimumCoverageConfidence = 0.30d;
    private const double MinimumHitCountConfidence = 0.38d;

    private static DateTimeOffset lastAttemptUtc =
        DateTimeOffset.MinValue;

    private static int coveragePercent = -1;
    private static double coverageConfidence;
    private static DateTimeOffset coverageLastObservedUtc =
        DateTimeOffset.MinValue;
    private static DateTimeOffset coverageStableSinceUtc =
        DateTimeOffset.MinValue;

    private static int pendingCoverage = -1;
    private static int pendingCoverageCount;

    private static int hitCount = -1;
    private static double hitCountConfidence;
    private static DateTimeOffset hitCountLastObservedUtc =
        DateTimeOffset.MinValue;
    private static DateTimeOffset hitCountChangedUtc =
        DateTimeOffset.MinValue;

    private static int pendingHitCount = -1;
    private static int pendingHitCountFrames;

    // Correction-flight gate. Absolute native hit count alone is unsafe:
    // a prior extra shot can already satisfy the next correction's numeric
    // threshold while the previous correction is still in flight.
    private static int correctionGateTargetN = -1;
    private static int correctionGateStep;
    private static int previousCorrectionLaunchHitBaseline = -1;
    private static DateTimeOffset previousCorrectionStepObservedUtc =
        DateTimeOffset.MinValue;

    internal static void Observe(
        DssCapturedFrame frame)
    {
        if (frame.Bgra32.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            if (frame.TimestampUtc
                - lastAttemptUtc
                < MinimumAttemptInterval)
            {
                return;
            }

            lastAttemptUtc =
                frame.TimestampUtc;
        }

        DssNativeScanProgressObservation observation;

        try
        {
            observation =
                DssNativeScanProgressDetector.Detect(
                    frame);
        }
        catch
        {
            // Progress CV is advisory to the capture pipeline and must never
            // be able to break frame publication.
            return;
        }

        bool coverageChanged = false;
        bool hitChanged = false;
        int loggedCoverage = -1;
        int loggedHits = -1;

        lock (Gate)
        {
            if (observation.CoverageAvailable
                && observation.CoverageConfidence
                   >= MinimumCoverageConfidence)
            {
                if (observation.CoveragePercent
                    == coveragePercent)
                {
                    coverageLastObservedUtc =
                        frame.TimestampUtc;

                    coverageConfidence =
                        Math.Max(
                            observation.CoverageConfidence,
                            coverageConfidence * 0.85d);

                    pendingCoverage = -1;
                    pendingCoverageCount = 0;
                }
                else
                {
                    if (observation.CoveragePercent
                        == pendingCoverage)
                    {
                        pendingCoverageCount++;
                    }
                    else
                    {
                        pendingCoverage =
                            observation.CoveragePercent;

                        pendingCoverageCount = 1;
                    }

                    if (pendingCoverageCount
                        >= RequiredStableFrames)
                    {
                        coveragePercent =
                            observation.CoveragePercent;

                        coverageConfidence =
                            observation.CoverageConfidence;

                        coverageLastObservedUtc =
                            frame.TimestampUtc;

                        coverageStableSinceUtc =
                            frame.TimestampUtc;

                        pendingCoverage = -1;
                        pendingCoverageCount = 0;

                        coverageChanged = true;
                        loggedCoverage = coveragePercent;
                    }
                }
            }

            if (observation.HitCountAvailable
                && observation.HitCountConfidence
                   >= MinimumHitCountConfidence)
            {
                if (observation.HitCount
                    == hitCount)
                {
                    hitCountLastObservedUtc =
                        frame.TimestampUtc;

                    hitCountConfidence =
                        Math.Max(
                            observation.HitCountConfidence,
                            hitCountConfidence * 0.85d);

                    pendingHitCount = -1;
                    pendingHitCountFrames = 0;
                }
                else
                {
                    if (observation.HitCount
                        == pendingHitCount)
                    {
                        pendingHitCountFrames++;
                    }
                    else
                    {
                        pendingHitCount =
                            observation.HitCount;

                        pendingHitCountFrames = 1;
                    }

                    if (pendingHitCountFrames
                        >= RequiredStableFrames)
                    {
                        hitCount =
                            observation.HitCount;

                        hitCountConfidence =
                            observation.HitCountConfidence;

                        hitCountLastObservedUtc =
                            frame.TimestampUtc;

                        hitCountChangedUtc =
                            frame.TimestampUtc;

                        pendingHitCount = -1;
                        pendingHitCountFrames = 0;

                        hitChanged = true;
                        loggedHits = hitCount;
                    }
                }
            }
        }

        if (coverageChanged)
        {
            Logger.Logger.Info(
                $"DSS NATIVE COVERAGE CV: {loggedCoverage}%.");
        }

        if (hitChanged)
        {
            Logger.Logger.Info(
                $"DSS NATIVE HITS CV: {loggedHits}.");
        }
    }

    internal static bool TryGetFresh(
        out DssNativeScanProgressSnapshot snapshot)
    {
        lock (Gate)
        {
            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            bool coverageFresh =
                coveragePercent >= 0
                && now
                   - coverageLastObservedUtc
                   <= TelemetryFreshness;

            bool hitsFresh =
                hitCount >= 0
                && now
                   - hitCountLastObservedUtc
                   <= TelemetryFreshness;

            if (!coverageFresh
                && !hitsFresh)
            {
                snapshot = default;
                return false;
            }

            snapshot =
                new DssNativeScanProgressSnapshot(
                    coverageFresh
                        ? coveragePercent
                        : -1,
                    hitsFresh
                        ? hitCount
                        : -1,
                    coverageConfidence,
                    hitCountConfidence,
                    coverageLastObservedUtc,
                    hitCountLastObservedUtc,
                    coverageStableSinceUtc,
                    hitCountChangedUtc);

            return true;
        }
    }

    internal static void ObserveTargetingStep(
        int targetN,
        int sequentialStep)
    {
        lock (Gate)
        {
            if (targetN <= 0)
            {
                correctionGateTargetN = -1;
                correctionGateStep = 0;
                previousCorrectionLaunchHitBaseline = -1;
                previousCorrectionStepObservedUtc =
                    DateTimeOffset.MinValue;
                return;
            }

            // Base mode and correction #1 have no previous correction flight
            // to acknowledge.
            if (sequentialStep <= targetN + 1)
            {
                correctionGateTargetN =
                    targetN;

                correctionGateStep =
                    sequentialStep;

                previousCorrectionLaunchHitBaseline =
                    -1;

                previousCorrectionStepObservedUtc =
                    DateTimeOffset.MinValue;

                return;
            }

            if (correctionGateTargetN == targetN
                && correctionGateStep == sequentialStep)
            {
                return;
            }

            correctionGateTargetN =
                targetN;

            correctionGateStep =
                sequentialStep;

            // Entering correction #2+ means fire-owned targeting has just
            // advanced after launching the previous correction.
            previousCorrectionLaunchHitBaseline =
                hitCount;

            previousCorrectionStepObservedUtc =
                DateTimeOffset.UtcNow;
        }
    }

    internal static bool CanOfferCorrection(
        int requiredHitCount,
        int correctionIndex,
        out DssNativeScanProgressSnapshot snapshot)
    {
        if (!TryGetFresh(
                out snapshot))
        {
            return false;
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        // Native 100% is an immediate "do not shoot". Journal completion may
        // arrive a little later.
        if (snapshot.CoveragePercent >= 100)
        {
            return false;
        }

        if (snapshot.CoveragePercent < 0
            || snapshot.HitCount
               < requiredHitCount)
        {
            return false;
        }

        if (correctionIndex > 1)
        {
            int launchBaseline;
            DateTimeOffset stepObservedUtc;

            lock (Gate)
            {
                launchBaseline =
                    previousCorrectionLaunchHitBaseline;

                stepObservedUtc =
                    previousCorrectionStepObservedUtc;
            }

            // Correction k+1 must not appear until Elite has reported a native
            // hit strictly after correction k was launched.
            if (launchBaseline < 0
                || snapshot.HitCount
                   <= launchBaseline
                || stepObservedUtc
                   == DateTimeOffset.MinValue
                || snapshot.HitCountChangedUtc
                   <= stepObservedUtc)
            {
                return false;
            }
        }

        if (snapshot.CoverageStableSinceUtc
                == DateTimeOffset.MinValue
            || snapshot.HitCountChangedUtc
               == DateTimeOffset.MinValue)
        {
            return false;
        }

        bool coverageSettled =
            now
            - snapshot.CoverageStableSinceUtc
            >= MinimumSettledDuration;

        bool hitCounterSettled =
            now
            - snapshot.HitCountChangedUtc
            >= MinimumSettledDuration;

        return
            coverageSettled
            && hitCounterSettled;
    }

    internal static void ResetForBodyChange()
    {
        lock (Gate)
        {
            lastAttemptUtc =
                DateTimeOffset.MinValue;

            coveragePercent = -1;
            coverageConfidence = 0d;
            coverageLastObservedUtc =
                DateTimeOffset.MinValue;
            coverageStableSinceUtc =
                DateTimeOffset.MinValue;
            pendingCoverage = -1;
            pendingCoverageCount = 0;

            hitCount = -1;
            hitCountConfidence = 0d;
            hitCountLastObservedUtc =
                DateTimeOffset.MinValue;
            hitCountChangedUtc =
                DateTimeOffset.MinValue;
            pendingHitCount = -1;
            pendingHitCountFrames = 0;

            correctionGateTargetN = -1;
            correctionGateStep = 0;
            previousCorrectionLaunchHitBaseline = -1;
            previousCorrectionStepObservedUtc =
                DateTimeOffset.MinValue;
        }
    }
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            lastAttemptUtc =
                DateTimeOffset.MinValue;

            coveragePercent = -1;
            coverageConfidence = 0d;
            coverageLastObservedUtc =
                DateTimeOffset.MinValue;
            coverageStableSinceUtc =
                DateTimeOffset.MinValue;
            pendingCoverage = -1;
            pendingCoverageCount = 0;

            hitCount = -1;
            hitCountConfidence = 0d;
            hitCountLastObservedUtc =
                DateTimeOffset.MinValue;
            hitCountChangedUtc =
                DateTimeOffset.MinValue;
            pendingHitCount = -1;
            pendingHitCountFrames = 0;

            correctionGateTargetN = -1;
            correctionGateStep = 0;
            previousCorrectionLaunchHitBaseline = -1;
            previousCorrectionStepObservedUtc =
                DateTimeOffset.MinValue;
        }
    }

    internal static void SetForTests(
        int coverage,
        int hits,
        TimeSpan stableAge)
    {
        lock (Gate)
        {
            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            coveragePercent = coverage;
            coverageConfidence = 1d;
            coverageLastObservedUtc = now;
            coverageStableSinceUtc =
                now - stableAge;

            hitCount = hits;
            hitCountConfidence = 1d;
            hitCountLastObservedUtc = now;
            hitCountChangedUtc =
                now - stableAge;

            pendingCoverage = -1;
            pendingCoverageCount = 0;
            pendingHitCount = -1;
            pendingHitCountFrames = 0;
        }
    }
}

/// <summary>
/// Fixed-layout CV for Frontier-native DSS scan progress.
///
/// Coverage percentage:
/// - bottom-left, x=125..232, y=920..975 in 1080p reference space;
/// - cyan while incomplete, neutral-white at 100%;
/// - only the numeric columns are sampled, excluding the percent marker.
///
/// Hit count:
/// - the small neutral-gray "Ударов: N" number in the existing lower-right
///   efficiency panel;
/// - same digit font/prototypes as native "Зондов: N".
///
/// v49 normalizes captures above 1080p before this detector runs.
/// </summary>
internal static class DssNativeScanProgressDetector
{
    private const int ReferenceHeight = 1080;
    private const int GlyphCanvasWidth = 12;
    private const int GlyphCanvasHeight = 14;

    private const double CoverageMaximumAcceptedError = 0.20d;
    private const double NeutralMaximumAcceptedError = 0.16d;

    internal static DssNativeScanProgressObservation Detect(
        DssCapturedFrame frame)
    {
        if (frame.Bgra32.Length == 0
            || frame.Width < 640
            || frame.Height < 480)
        {
            return default;
        }

        double scale =
            frame.Height
            / (double)ReferenceHeight;

        NumericObservation coverage =
            DetectCoveragePercent(
                frame,
                scale);

        NumericObservation hits =
            DetectNativeHitCount(
                frame,
                scale);

        return
            new DssNativeScanProgressObservation(
                coverage.Available,
                coverage.Value,
                coverage.Confidence,
                hits.Available,
                hits.Value,
                hits.Confidence);
    }

    private static NumericObservation DetectCoveragePercent(
        DssCapturedFrame frame,
        double scale)
    {
        int left =
            Scale(
                125d,
                scale);

        int right =
            Math.Min(
                frame.Width,
                Scale(
                    232d,
                    scale));

        int top =
            Scale(
                920d,
                scale);

        int bottom =
            Math.Min(
                frame.Height,
                Scale(
                    975d,
                    scale));

        if (right <= left
            || bottom <= top)
        {
            return NumericObservation.Empty;
        }

        List<GlyphComponent> components =
            FindGlyphComponents(
                frame,
                left,
                top,
                right,
                bottom,
                scale,
                coveragePixels: true,
                minimumReferenceHeight: 24d,
                minimumReferenceWidth: 5d,
                minimumReferenceArea: 40d);

        if (components.Count == 0)
        {
            return NumericObservation.Empty;
        }

        var matches =
            new List<(GlyphComponent Component, DigitMatch Match)>();

        foreach (GlyphComponent component
                 in components.OrderBy(
                     component => component.X))
        {
            DigitMatch match =
                ClassifyDigit(
                    frame,
                    component,
                    coveragePixels: true,
                    CoverageMaximumAcceptedError);

            if ((!match.Available
                 || match.Confidence < 0.30d)
                && TryClassifyLargeCoverageDigit(
                    frame,
                    component,
                    out DigitMatch largeDigit))
            {
                match = largeDigit;
            }

            if (!match.Available)
            {
                // An unrecognized active digit must invalidate the whole
                // number. Otherwise 20% could be reported as 0% when only
                // its second digit matches a known shape.
                return NumericObservation.Empty;
            }

            matches.Add(
                (component, match));
        }

        if (matches.Count < 1
            || matches.Count > 3)
        {
            return NumericObservation.Empty;
        }

        // Any valid digits must form the left-to-right numeric run. The % sign
        // begins just to the right of this ROI; its diagonal can enter the ROI
        // on 2-digit values but fails the digit-template error threshold.
        int value = 0;
        double confidence = 1d;

        foreach ((GlyphComponent _, DigitMatch match)
                 in matches)
        {
            value =
                value * 10
                + match.Digit;

            confidence =
                Math.Min(
                    confidence,
                    match.Confidence);
        }

        if (value < 0
            || value > 100)
        {
            return NumericObservation.Empty;
        }

        return
            new NumericObservation(
                true,
                value,
                confidence);
    }

    private static NumericObservation DetectNativeHitCount(
        DssCapturedFrame frame,
        double scale)
    {
        // In the current English DSS HUD the number precedes "IMPACTED"
        // (right edge ~x=1700 at 1080p). Older localized captures placed the
        // number at ~x=1780. Probe both layouts without accepting the label
        // letters as the hit count.
        NumericObservation current = DetectNativeHitCountAtOffset(
            frame,
            scale,
            rightOffset: 220d);

        return current.Available
            ? current
            : DetectNativeHitCountAtOffset(
                frame,
                scale,
                rightOffset: 140d);
    }

    private static bool TryClassifyLargeCoverageDigit(
        DssCapturedFrame frame,
        GlyphComponent component,
        out DigitMatch match)
    {
        match = DigitMatch.Empty;

        // Elite's large coverage font differs from the small neutral HUD
        // digits. Recorded 000%, 007% and 008% frames are distinguished by
        // their upper, middle, lower and side occupancy instead of trying to
        // match the wrong font. Unsupported shapes stay unknown.
        if (component.Width < 16
            || component.Height < 28)
        {
            return false;
        }

        int topRows = Math.Max(1, component.Height / 6);
        int middleStart = component.Height * 17 / 40;
        int middleEnd = Math.Max(
            middleStart + 1,
            component.Height * 23 / 40);
        int bottomStart = component.Height * 3 / 4;
        int sideStart = component.Height / 4;
        int sideEnd = component.Height * 3 / 4;
        int sideWidth = Math.Max(1, component.Width / 4);
        int topPixels = 0;
        int middlePixels = 0;
        int bottomPixels = 0;
        long bottomX = 0;
        int leftSidePixels = 0;
        int rightSidePixels = 0;

        for (int y = 0; y < component.Height; y++)
        {
            int row = (component.Y + y) * frame.Stride;
            for (int x = 0; x < component.Width; x++)
            {
                if (GetCoverageGlyphLuma(
                        frame,
                        row + (component.X + x) * 4) == 0)
                {
                    continue;
                }

                if (y < topRows)
                {
                    topPixels++;
                }

                if (y >= middleStart && y < middleEnd)
                {
                    middlePixels++;
                }

                if (y >= bottomStart)
                {
                    bottomPixels++;
                    bottomX += x;
                }

                if (y >= sideStart && y < sideEnd)
                {
                    if (x < sideWidth)
                    {
                        leftSidePixels++;
                    }

                    if (x >= component.Width - sideWidth)
                    {
                        rightSidePixels++;
                    }
                }
            }
        }

        double topFill = topPixels
            / (double)(component.Width * topRows);
        double bottomFill = bottomPixels
            / (double)(component.Width
                * (component.Height - bottomStart));
        double middleFill = middlePixels
            / (double)(component.Width
                * (middleEnd - middleStart));
        double bottomCenter = bottomPixels > 0
            ? bottomX / (double)(bottomPixels * component.Width)
            : 1d;
        int sideArea = sideWidth * Math.Max(1, sideEnd - sideStart);
        double leftSideFill = leftSidePixels / (double)sideArea;
        double rightSideFill = rightSidePixels / (double)sideArea;

        int digit = ClassifyLargeCoverageShape(
            topFill,
            middleFill,
            bottomFill,
            bottomCenter,
            leftSideFill,
            rightSideFill);

        if (digit < 0)
        {
            return false;
        }

        match = new DigitMatch(true, digit, 0.80d);
        return true;
    }

    internal static int ClassifyLargeCoverageShape(
        double topFill,
        double middleFill,
        double bottomFill,
        double bottomCenter,
        double leftSideFill,
        double rightSideFill)
    {
        if (topFill >= 0.78d
            && bottomFill <= 0.35d
            && bottomCenter <= 0.70d)
        {
            return 7;
        }

        if (topFill is < 0.55d or > 0.78d
            || bottomFill is < 0.48d or > 0.70d
            || leftSideFill < 0.30d
            || rightSideFill < 0.30d)
        {
            return -1;
        }

        if (middleFill >= 0.75d)
        {
            return 8;
        }

        return middleFill is >= 0.55d and <= 0.72d
            ? 0
            : -1;
    }

    private static NumericObservation DetectNativeHitCountAtOffset(
        DssCapturedFrame frame,
        double scale,
        double rightOffset)
    {
        int left =
            Math.Max(
                0,
                frame.Width
                    - Scale(
                    rightOffset + 40d,
                    scale));

        int right =
            Math.Min(
                frame.Width,
                frame.Width
                    - Scale(
                    rightOffset - 10d,
                    scale));

        int top =
            Scale(
                833d,
                scale);

        int bottom =
            Math.Min(
                frame.Height,
                Scale(
                    856d,
                    scale));

        if (right <= left
            || bottom <= top)
        {
            return NumericObservation.Empty;
        }

        List<GlyphComponent> components =
            FindGlyphComponents(
                frame,
                left,
                top,
                right,
                bottom,
                scale,
                coveragePixels: false,
                minimumReferenceHeight: 8d,
                minimumReferenceWidth: 2d,
                minimumReferenceArea: 5d);

        if (components.Count == 0)
        {
            return NumericObservation.Empty;
        }

        int expectedRightEdge =
            frame.Width
            - Scale(
                rightOffset,
                scale);

        int rightTolerance =
            Math.Max(
                3,
                Scale(
                    6d,
                    scale));

        GlyphComponent? ones =
            null;

        int bestRightError =
            int.MaxValue;

        foreach (GlyphComponent component
                 in components)
        {
            int componentRight =
                component.X
                + component.Width;

            int error =
                Math.Abs(
                    componentRight
                    - expectedRightEdge);

            if (error > rightTolerance)
            {
                continue;
            }

            if (ones is null
                || error < bestRightError
                || (error == bestRightError
                    && component.Height
                       > ones.Value.Height))
            {
                ones = component;
                bestRightError = error;
            }
        }

        if (ones is null)
        {
            return NumericObservation.Empty;
        }

        int mergedDigitWidthThreshold =
            Math.Max(
                8,
                Scale(
                    12d,
                    scale));

        if (ones.Value.Width
                >= mergedDigitWidthThreshold
            && TryClassifyMergedTwoDigitComponent(
                frame,
                ones.Value,
                out int mergedHits,
                out double mergedConfidence))
        {
            return
                new NumericObservation(
                    true,
                    mergedHits,
                    mergedConfidence);
        }

        DigitMatch onesMatch =
            ClassifyDigit(
                frame,
                ones.Value,
                coveragePixels: false,
                NeutralMaximumAcceptedError);

        if (!onesMatch.Available)
        {
            return NumericObservation.Empty;
        }

        GlyphComponent? tens =
            null;

        int maximumTensGap =
            Math.Max(
                3,
                Scale(
                    7d,
                    scale));

        foreach (GlyphComponent component
                 in components)
        {
            if (component.X
                >= ones.Value.X)
            {
                continue;
            }

            int gap =
                ones.Value.X
                - (component.X
                   + component.Width);

            if (gap < 0
                || gap > maximumTensGap)
            {
                continue;
            }

            if (component.Height
                < ones.Value.Height * 0.82d)
            {
                continue;
            }

            if (tens is null
                || component.X
                   > tens.Value.X)
            {
                tens = component;
            }
        }

        int value =
            onesMatch.Digit;

        double confidence =
            onesMatch.Confidence;

        if (tens is not null)
        {
            DigitMatch tensMatch =
                ClassifyDigit(
                    frame,
                    tens.Value,
                    coveragePixels: false,
                    NeutralMaximumAcceptedError);

            if (!tensMatch.Available)
            {
                return NumericObservation.Empty;
            }

            value =
                tensMatch.Digit * 10
                + onesMatch.Digit;

            confidence =
                Math.Min(
                    confidence,
                    tensMatch.Confidence);
        }

        if (value < 0
            || value > 99)
        {
            return NumericObservation.Empty;
        }

        return
            new NumericObservation(
                true,
                value,
                confidence);
    }

    private static bool TryClassifyMergedTwoDigitComponent(
        DssCapturedFrame frame,
        GlyphComponent component,
        out int value,
        out double confidence)
    {
        value = 0;
        confidence = 0d;

        int minimumPartWidth =
            Math.Max(
                3,
                (int)Math.Floor(
                    component.Height * 0.30d));

        int lastSplit =
            component.Width
            - minimumPartWidth;

        if (lastSplit <= minimumPartWidth)
        {
            return false;
        }

        double bestScore =
            double.NegativeInfinity;

        for (int split =
                 minimumPartWidth;
             split <= lastSplit;
             split++)
        {
            var left =
                new GlyphComponent(
                    component.X,
                    component.Y,
                    split,
                    component.Height,
                    0);

            var right =
                new GlyphComponent(
                    component.X + split,
                    component.Y,
                    component.Width - split,
                    component.Height,
                    0);

            DigitMatch tens =
                ClassifyDigit(
                    frame,
                    left,
                    coveragePixels: false,
                    NeutralMaximumAcceptedError);

            DigitMatch ones =
                ClassifyDigit(
                    frame,
                    right,
                    coveragePixels: false,
                    NeutralMaximumAcceptedError);

            if (!tens.Available
                || !ones.Available)
            {
                continue;
            }

            int candidate =
                tens.Digit * 10
                + ones.Digit;

            if (candidate < 10
                || candidate > 99)
            {
                continue;
            }

            double pairConfidence =
                Math.Min(
                    tens.Confidence,
                    ones.Confidence);

            double widthImbalance =
                Math.Abs(
                    left.Width
                    - right.Width)
                / (double)Math.Max(
                    1,
                    component.Width);

            double score =
                pairConfidence
                - widthImbalance * 0.08d;

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            value = candidate;
            confidence = pairConfidence;
        }

        return value > 0;
    }

    private static List<GlyphComponent> FindGlyphComponents(
        DssCapturedFrame frame,
        int left,
        int top,
        int right,
        int bottom,
        double scale,
        bool coveragePixels,
        double minimumReferenceHeight,
        double minimumReferenceWidth,
        double minimumReferenceArea)
    {
        int width =
            right - left;

        int height =
            bottom - top;

        var mask =
            new bool[
                width
                * height];

        for (int y = 0;
             y < height;
             y++)
        {
            int frameY =
                top + y;

            int row =
                frameY
                * frame.Stride;

            for (int x = 0;
                 x < width;
                 x++)
            {
                int index =
                    row
                    + (left + x) * 4;

                mask[
                    y * width + x] =
                    (coveragePixels
                        ? GetCoverageGlyphLuma(
                            frame,
                            index)
                        : GetNeutralGlyphLuma(
                            frame,
                            index))
                    > 0;
            }
        }

        var visited =
            new bool[
                mask.Length];

        var result =
            new List<GlyphComponent>();

        int minimumHeight =
            Math.Max(
                1,
                Scale(
                    minimumReferenceHeight,
                    scale));

        int minimumWidth =
            Math.Max(
                1,
                Scale(
                    minimumReferenceWidth,
                    scale));

        int minimumArea =
            Math.Max(
                1,
                (int)Math.Round(
                    minimumReferenceArea
                    * scale
                    * scale));

        int[] queue =
            new int[
                mask.Length];

        for (int seed = 0;
             seed < mask.Length;
             seed++)
        {
            if (!mask[seed]
                || visited[seed])
            {
                continue;
            }

            int head = 0;
            int tail = 0;

            queue[tail++] = seed;
            visited[seed] = true;

            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;
            int area = 0;

            while (head < tail)
            {
                int current =
                    queue[head++];

                int cy =
                    current / width;

                int cx =
                    current
                    - cy * width;

                minX =
                    Math.Min(
                        minX,
                        cx);

                minY =
                    Math.Min(
                        minY,
                        cy);

                maxX =
                    Math.Max(
                        maxX,
                        cx);

                maxY =
                    Math.Max(
                        maxY,
                        cy);

                area++;

                for (int dy = -1;
                     dy <= 1;
                     dy++)
                {
                    for (int dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        if (dx == 0
                            && dy == 0)
                        {
                            continue;
                        }

                        int nx =
                            cx + dx;

                        int ny =
                            cy + dy;

                        if ((uint)nx
                                >= (uint)width
                            || (uint)ny
                               >= (uint)height)
                        {
                            continue;
                        }

                        int next =
                            ny * width
                            + nx;

                        if (!mask[next]
                            || visited[next])
                        {
                            continue;
                        }

                        visited[next] = true;
                        queue[tail++] = next;
                    }
                }
            }

            int componentWidth =
                maxX - minX + 1;

            int componentHeight =
                maxY - minY + 1;

            if (componentHeight
                    < minimumHeight
                || componentWidth
                   < minimumWidth
                || area
                   < minimumArea)
            {
                continue;
            }

            result.Add(
                new GlyphComponent(
                    left + minX,
                    top + minY,
                    componentWidth,
                    componentHeight,
                    area));
        }

        return result;
    }

    private static DigitMatch ClassifyDigit(
        DssCapturedFrame frame,
        GlyphComponent component,
        bool coveragePixels,
        double maximumAcceptedError)
    {
        Span<byte> normalized =
            stackalloc byte[
                GlyphCanvasWidth
                * GlyphCanvasHeight];

        int maximumLuma = 0;

        for (int y = component.Y;
             y < component.Y
                 + component.Height;
             y++)
        {
            int row =
                y * frame.Stride;

            for (int x = component.X;
                 x < component.X
                     + component.Width;
                 x++)
            {
                int value =
                    coveragePixels
                        ? GetCoverageGlyphLuma(
                            frame,
                            row + x * 4)
                        : GetNeutralGlyphLuma(
                            frame,
                            row + x * 4);

                maximumLuma =
                    Math.Max(
                        maximumLuma,
                        value);
            }
        }

        if (maximumLuma < 25)
        {
            return DigitMatch.Empty;
        }

        int normalizedWidth =
            Math.Clamp(
                (int)Math.Round(
                    component.Width
                    * GlyphCanvasHeight
                    / (double)component.Height),
                1,
                GlyphCanvasWidth);

        int xOffset =
            (GlyphCanvasWidth
             - normalizedWidth)
            / 2;

        for (int y = 0;
             y < GlyphCanvasHeight;
             y++)
        {
            int sourceY =
                component.Y
                + Math.Min(
                    component.Height - 1,
                    (int)(
                        (2L * y + 1L)
                        * component.Height
                        / (2L * GlyphCanvasHeight)));

            int row =
                sourceY
                * frame.Stride;

            for (int x = 0;
                 x < normalizedWidth;
                 x++)
            {
                int sourceX =
                    component.X
                    + Math.Min(
                        component.Width - 1,
                        (int)(
                            (2L * x + 1L)
                            * component.Width
                            / (2L * normalizedWidth)));

                int value =
                    coveragePixels
                        ? GetCoverageGlyphLuma(
                            frame,
                            row + sourceX * 4)
                        : GetNeutralGlyphLuma(
                            frame,
                            row + sourceX * 4);

                normalized[
                    y * GlyphCanvasWidth
                    + xOffset
                    + x] =
                    (byte)Math.Clamp(
                        (int)Math.Round(
                            value
                            * 255d
                            / maximumLuma),
                        0,
                        255);
            }
        }

        DssNativeDigitClassification classification =
            DssNativeEfficiencyTargetDetector
                .ClassifyNormalizedDigit(
                    normalized,
                    maximumAcceptedError,
                    useRecordedSmallHudVariants:
                        !coveragePixels);

        if (!classification.Available)
        {
            return DigitMatch.Empty;
        }

        return
            new DigitMatch(
                true,
                classification.Digit,
                classification.Confidence);
    }

    private static int GetCoverageGlyphLuma(
        DssCapturedFrame frame,
        int index)
    {
        if (index < 0
            || index + 2
               >= frame.Bgra32.Length)
        {
            return 0;
        }

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        int maximum =
            Math.Max(
                red,
                Math.Max(
                    green,
                    blue));

        int minimum =
            Math.Min(
                red,
                Math.Min(
                    green,
                    blue));

        int luma =
            (red * 54
             + green * 183
             + blue * 19) >> 8;

        bool neutralWhite =
            maximum - minimum <= 50
            && luma >= 140;

        bool cyan =
            blue >= 100
            && green >= 70
            && blue >= red + 40
            && green >= red + 20;

        return
            neutralWhite || cyan
                ? maximum
                : 0;
    }

    private static int GetNeutralGlyphLuma(
        DssCapturedFrame frame,
        int index)
    {
        if (index < 0
            || index + 2
               >= frame.Bgra32.Length)
        {
            return 0;
        }

        int blue =
            frame.Bgra32[index];

        int green =
            frame.Bgra32[index + 1];

        int red =
            frame.Bgra32[index + 2];

        int maximum =
            Math.Max(
                red,
                Math.Max(
                    green,
                    blue));

        int minimum =
            Math.Min(
                red,
                Math.Min(
                    green,
                    blue));

        if (maximum - minimum > 110)
        {
            return 0;
        }

        int luma =
            (red * 54
             + green * 183
             + blue * 19) >> 8;

        return
            luma >= 25
                ? luma
                : 0;
    }

    private static int Scale(
        double referencePixels,
        double scale) =>
        Math.Max(
            1,
            (int)Math.Round(
                referencePixels
                * scale));

    private readonly record struct NumericObservation(
        bool Available,
        int Value,
        double Confidence)
    {
        public static NumericObservation Empty { get; } =
            new(false, 0, 0d);
    }

    private readonly record struct GlyphComponent(
        int X,
        int Y,
        int Width,
        int Height,
        int Area);

    private readonly record struct DigitMatch(
        bool Available,
        int Digit,
        double Confidence)
    {
        public static DigitMatch Empty { get; } =
            new(false, -1, 0d);
    }
}
